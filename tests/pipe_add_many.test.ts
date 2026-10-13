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

import { PIPE_DOMAIN_DEFINITION } from "../src/tools/domains/pipeDomain.js";
import { isApprovalRequired } from "../src/tools/approvalPolicy.js";
import { GENERATED_TOOL_CATALOG_ENTRIES } from "../src/tools/toolManifest.js";

const addMany = PIPE_DOMAIN_DEFINITION.actions.add_many;

const validArgs = {
  action: "add_many",
  networkName: "Pluvial",
  structures: [
    { name: "SUM-1", x: 5110, y: 10061, partName: "Inlet", rimElevation: 98.3, sumpDepth: 1.2 },
  ],
  pipes: [
    { name: "PS-1", partName: "400 mm Concrete Pipe", startStructure: "SUM-1", endStructure: "C1-3" },
  ],
};

describe("civil3d_pipe add_many", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("is advertised and requires a single approval", () => {
    const pipe = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_pipe");
    expect(pipe!.operations).toContain("add_many");
    expect(isApprovalRequired({
      toolName: "civil3d_pipe",
      action: "add_many",
      capabilities: addMany.capabilities,
      safeForRetry: addMany.safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);
  });

  it("validates the batch", () => {
    expect(addMany.inputSchema.safeParse(validArgs).success).toBe(true);
    expect(addMany.inputSchema.safeParse({ action: "add_many", networkName: "Pluvial" }).success).toBe(false);
    expect(addMany.inputSchema.safeParse({
      ...validArgs,
      pipes: [{ partName: "400 mm Concrete Pipe", endStructure: "C1-3" }],
    }).success).toBe(false);
  });

  it("sends the whole batch in one addPartsToNetwork command", async () => {
    sendCommandMock.mockResolvedValue({ addedStructures: 1, addedPipes: 1 });

    await addMany.execute(addMany.inputSchema.parse(validArgs));

    expect(sendCommandMock).toHaveBeenCalledTimes(1);
    expect(sendCommandMock).toHaveBeenCalledWith("addPartsToNetwork", {
      networkName: "Pluvial",
      structures: validArgs.structures,
      pipes: validArgs.pipes,
    });
  });
});
