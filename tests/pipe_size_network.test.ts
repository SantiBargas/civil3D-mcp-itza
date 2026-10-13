import { beforeEach, describe, expect, it, vi } from "vitest";

const { sendCommandMock } = vi.hoisted(() => ({
  sendCommandMock: vi.fn(),
}));

vi.mock("../src/utils/ConnectionManager.js", () => ({
  withApplicationConnection: async <T>(
    operation: (client: { sendCommand: typeof sendCommandMock }) => Promise<T>,
  ) => await operation({
    sendCommand: sendCommandMock,
  }),
}));

import { PIPE_DOMAIN_DEFINITION, parsePartDiameter } from "../src/tools/domains/pipeDomain.js";

const sizeNetwork = PIPE_DOMAIN_DEFINITION.actions.size_network;

function mockNetworkAndCatalog(catalogEntry: Record<string, unknown>) {
  sendCommandMock.mockImplementation(async (method: string) => {
    if (method === "getPipeNetwork") {
      return { name: "Pluvial", partsList: "Lista", pipes: [{ name: "P-1", diameter: 0.4, slope: 1, length: 50 }] };
    }
    if (method === "listPipePartsCatalog") {
      return { partsLists: [{ name: "Lista", ...catalogEntry }] };
    }
    throw new Error(`Unexpected command ${method}`);
  });
}

describe("civil3d_pipe size_network", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("chooses only among pipe parts when the catalog separates them", async () => {
    mockNetworkAndCatalog({
      parts: ["Pipe 0.6", "Structure 0.2"],
      pipeParts: ["Pipe 0.6"],
      structureParts: ["Structure 0.2"],
    });

    const result = await sizeNetwork.execute(sizeNetwork.inputSchema.parse({
      action: "size_network",
      networkName: "Pluvial",
      defaultDesignFlow: 0.01,
    })) as { recommendations: Array<{ selectedPart: string }> };

    expect(result.recommendations[0].selectedPart).toBe("Pipe 0.6");
  });

  it("falls back to the flat parts list for older plugins", async () => {
    mockNetworkAndCatalog({ parts: ["Pipe 0.6"] });

    const result = await sizeNetwork.execute(sizeNetwork.inputSchema.parse({
      action: "size_network",
      networkName: "Pluvial",
      defaultDesignFlow: 0.01,
    })) as { recommendations: Array<{ selectedPart: string }> };

    expect(result.recommendations[0].selectedPart).toBe("Pipe 0.6");
  });
});

describe("parsePartDiameter", () => {
  it("reads metric catalog names with thousands separators as metres", () => {
    expect(parsePartDiameter("400 mm Concrete Pipe")).toBe(0.4);
    expect(parsePartDiameter("1,050 mm Concrete Pipe")).toBe(1.05);
  });

  it("keeps unitless or imperial sizes as written", () => {
    expect(parsePartDiameter("12in RCP")).toBe(12);
    expect(parsePartDiameter("Pipe 0.6")).toBe(0.6);
  });

  it("rejects non-circular sections", () => {
    expect(parsePartDiameter("1,000 x 500 mm Concrete Box Culvert")).toBeNull();
  });
});

describe("civil3d_pipe size_network with a metric catalog", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("picks the smallest concrete pipe that carries the flow", async () => {
    mockNetworkAndCatalog({
      parts: [],
      pipeParts: ["1,000 mm Concrete Pipe", "300 mm Concrete Pipe", "400 mm Concrete Pipe", "600 mm Concrete Pipe"],
    });

    const result = await sizeNetwork.execute(sizeNetwork.inputSchema.parse({
      action: "size_network",
      networkName: "Pluvial",
      defaultDesignFlow: 0.155,
      targetVelocityMin: 0.9,
      targetVelocityMax: 5,
    })) as { recommendations: Array<{ selectedPart: string; selectedDiameter: number }> };

    expect(result.recommendations[0]).toMatchObject({ selectedPart: "400 mm Concrete Pipe", selectedDiameter: 0.4 });
  });
});

describe("civil3d_pipe hydraulic_analysis", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("leaves unit-dependent defaults to the plugin and forwards per-pipe flows", async () => {
    sendCommandMock.mockResolvedValue({ networkName: "Pluvial" });
    const analysis = PIPE_DOMAIN_DEFINITION.actions.hydraulic_analysis;

    await analysis.execute(analysis.inputSchema.parse({
      action: "hydraulic_analysis",
      networkName: "Pluvial",
      perPipeDesignFlows: [{ pipeName: "P-1", designFlow: 0.155 }],
    }));

    expect(sendCommandMock).toHaveBeenCalledWith("analyzePipeNetworkHydraulics", {
      networkName: "Pluvial",
      designFlow: null,
      perPipeDesignFlows: [{ pipeName: "P-1", designFlow: 0.155 }],
      manningsN: 0.013,
      minCoverDepth: null,
      minVelocity: null,
      maxVelocity: null,
      minSlope: null,
    });
  });
});
