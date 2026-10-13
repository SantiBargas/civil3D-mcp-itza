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

import { CORRIDOR_DOMAIN_DEFINITION } from "../src/tools/domains/corridorDomain.js";
import { ASSEMBLY_DOMAIN_DEFINITION } from "../src/tools/domains/assemblyDomain.js";
import { isApprovalRequired } from "../src/tools/approvalPolicy.js";
import { GENERATED_TOOL_CATALOG_ENTRIES } from "../src/tools/toolManifest.js";

const create = CORRIDOR_DOMAIN_DEFINITION.actions.create;
const createSubassembly = ASSEMBLY_DOMAIN_DEFINITION.actions.create_subassembly;

describe("civil3d_corridor create", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("is advertised by the canonical corridor tool", () => {
    const corridor = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_corridor");
    expect(corridor!.operations).toContain("create");
  });

  it("requires approval because it creates objects", () => {
    expect(isApprovalRequired({
      toolName: "civil3d_corridor",
      action: "create",
      capabilities: create.capabilities,
      safeForRetry: create.safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);
  });

  it("validates the input", () => {
    const valid = {
      action: "create",
      name: "Calle 3",
      alignmentName: "Calle 3",
      profileName: "Calle 3 - Rasante",
      assemblyName: "Calle 15 m",
    };
    expect(create.inputSchema.safeParse(valid).success).toBe(true);
    expect(create.inputSchema.safeParse({ ...valid, assemblyName: undefined }).success).toBe(false);
    expect(create.inputSchema.safeParse({ ...valid, frequency: 0 }).success).toBe(false);
    expect(create.inputSchema.safeParse({ ...valid, corridorSurfaces: [{ linkCodes: ["Top"] }] }).success).toBe(false);
  });

  it("maps the request to a single createCorridor command", async () => {
    sendCommandMock.mockResolvedValue({ corridorName: "Calle 3" });

    await create.execute({
      action: "create",
      name: "Calle 3",
      alignmentName: "Calle 3",
      profileName: "Calle 3 - Rasante",
      assemblyName: "Calle 15 m",
      targetSurface: "Terreno Natural",
      corridorSurfaces: [{ name: "Calle 3 - Datum", linkCodes: ["Datum"] }],
    });

    expect(sendCommandMock).toHaveBeenCalledTimes(1);
    expect(sendCommandMock).toHaveBeenCalledWith("createCorridor", {
      corridorName: "Calle 3",
      alignmentName: "Calle 3",
      profileName: "Calle 3 - Rasante",
      assemblyName: "Calle 15 m",
      startStation: null,
      endStation: null,
      frequency: 10,
      frequencyAtCurves: null,
      targetSurface: "Terreno Natural",
      corridorSurfaces: [{ name: "Calle 3 - Datum", linkCodes: ["Datum"] }],
    });
  });
});

describe("civil3d_corridor delete / surface / region_edit", () => {
  const { actions } = CORRIDOR_DOMAIN_DEFINITION;

  beforeEach(() => {
    sendCommandMock.mockReset();
    sendCommandMock.mockResolvedValue({});
  });

  it.each(["delete", "surface_add", "surface_delete", "region_edit"])("advertises and requires approval for %s", (action) => {
    const corridor = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_corridor");
    expect(corridor!.operations).toContain(action);
    expect(isApprovalRequired({
      toolName: "civil3d_corridor",
      action,
      capabilities: actions[action].capabilities,
      safeForRetry: actions[action].safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);
  });

  it("maps delete and surface actions to their plugin commands", async () => {
    await actions.delete.execute({ action: "delete", name: "Calle 3" });
    await actions.surface_add.execute({ action: "surface_add", name: "Calle 3", corridorSurfaces: [{ name: "Top" }] });
    await actions.surface_delete.execute({ action: "surface_delete", name: "Calle 3", corridorSurface: "Top" });

    expect(sendCommandMock).toHaveBeenNthCalledWith(1, "deleteCorridor", { corridorName: "Calle 3" });
    expect(sendCommandMock).toHaveBeenNthCalledWith(2, "addCorridorSurfaces", {
      corridorName: "Calle 3",
      corridorSurfaces: [{ name: "Top" }],
    });
    expect(sendCommandMock).toHaveBeenNthCalledWith(3, "deleteCorridorSurface", { corridorName: "Calle 3", corridorSurface: "Top" });
  });

  it("rejects surface_add without surfaces and region_edit without changes", () => {
    expect(actions.surface_add.inputSchema.safeParse({ action: "surface_add", name: "C", corridorSurfaces: [] }).success).toBe(false);
    expect(actions.region_edit.inputSchema.safeParse({ action: "region_edit", name: "C", regionIndex: 0 }).success).toBe(false);
    expect(actions.region_edit.inputSchema.safeParse({ action: "region_edit", name: "C", regionIndex: 0, frequency: 5 }).success).toBe(true);
  });

  it("maps region_edit with nulls for untouched fields", async () => {
    await actions.region_edit.execute({ action: "region_edit", name: "Calle 3", regionIndex: 0, endStation: 120 });
    expect(sendCommandMock).toHaveBeenCalledWith("editCorridorRegion", {
      corridorName: "Calle 3",
      baselineIndex: 0,
      regionIndex: 0,
      startStation: null,
      endStation: 120,
      assemblyName: null,
      frequency: null,
      frequencyAtCurves: null,
    });
  });
});

describe("civil3d_assembly delete", () => {
  it("is advertised, requires approval and maps to deleteAssembly", async () => {
    sendCommandMock.mockReset();
    sendCommandMock.mockResolvedValue({ deleted: true });
    const del = ASSEMBLY_DOMAIN_DEFINITION.actions.delete;
    const assembly = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_assembly");
    expect(assembly!.operations).toContain("delete");
    expect(isApprovalRequired({
      toolName: "civil3d_assembly",
      action: "delete",
      capabilities: del.capabilities,
      safeForRetry: del.safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);

    await del.execute({ action: "delete", name: "Calle 15 m" });
    expect(sendCommandMock).toHaveBeenCalledWith("deleteAssembly", { name: "Calle 15 m" });
  });
});

describe("civil3d_assembly create_subassembly chaining", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("passes name, attachTo and attachPointIndex to the plugin", async () => {
    sendCommandMock.mockResolvedValue({ added: true });

    await createSubassembly.execute({
      action: "create_subassembly",
      assemblyName: "Calle 15 m",
      subassemblyType: "UrbanCurbGutterGeneral",
      side: "Both",
      name: "Cordon",
      attachTo: "Carril",
    });

    expect(sendCommandMock).toHaveBeenCalledWith("createSubassembly", {
      assemblyName: "Calle 15 m",
      subassemblyType: "UrbanCurbGutterGeneral",
      side: "Both",
      parameters: {},
      name: "Cordon",
      attachTo: "Carril",
      attachPointIndex: null,
    });
  });

  it("rejects a negative attachPointIndex", () => {
    expect(createSubassembly.inputSchema.safeParse({
      action: "create_subassembly",
      assemblyName: "A",
      subassemblyType: "BasicLane",
      side: "Left",
      attachTo: "X",
      attachPointIndex: -1,
    }).success).toBe(false);
  });
});
