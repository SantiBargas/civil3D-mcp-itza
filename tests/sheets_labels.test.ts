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

import { STANDARDS_DOMAIN_DEFINITION } from "../src/tools/domains/standardsDomain.js";
import { PLAN_PRODUCTION_DOMAIN_DEFINITION } from "../src/tools/domains/planProductionDomain.js";
import { isApprovalRequired } from "../src/tools/approvalPolicy.js";
import { GENERATED_TOOL_CATALOG_ENTRIES } from "../src/tools/toolManifest.js";

const { label_add_many: addMany } = STANDARDS_DOMAIN_DEFINITION.actions;
const { layout_create: layoutCreate, layout_add_entities: addEntities, layer_list: layerList } = PLAN_PRODUCTION_DOMAIN_DEFINITION.actions;

describe("batch labels and sheets", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("advertises the new actions; writes need approval, the layer listing does not", () => {
    const standards = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_standards");
    const plan = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_plan_production");
    expect(standards!.operations).toEqual(expect.arrayContaining(["label_add_many"]));
    expect(plan!.operations).toEqual(expect.arrayContaining(["layout_create", "layout_add_entities", "layer_list"]));

    for (const [toolName, action, definition, expected] of [
      ["civil3d_standards", "label_add_many", addMany, true],
      ["civil3d_plan_production", "layout_create", layoutCreate, true],
      ["civil3d_plan_production", "layout_add_entities", addEntities, true],
      ["civil3d_plan_production", "layer_list", layerList, false],
    ] as const) {
      expect(isApprovalRequired({
        toolName,
        action,
        capabilities: definition.capabilities,
        safeForRetry: definition.safeForRetry,
        requiresActiveDrawing: true,
      })).toBe(expected);
    }
  });

  it("requires the site for parcel labels and the network for pipe labels", () => {
    expect(addMany.inputSchema.safeParse({ action: "label_add_many", kind: "parcel_area", siteName: "Loteo" }).success).toBe(true);
    expect(addMany.inputSchema.safeParse({ action: "label_add_many", kind: "parcel_segment" }).success).toBe(false);
    expect(addMany.inputSchema.safeParse({ action: "label_add_many", kind: "pipe", networkName: "Pluvial" }).success).toBe(true);
    expect(addMany.inputSchema.safeParse({ action: "label_add_many", kind: "structure", siteName: "Loteo" }).success).toBe(false);
  });

  it("deletes labels by handle with approval, in one deleteLabels command", async () => {
    const deleteMany = STANDARDS_DOMAIN_DEFINITION.actions.label_delete_many;
    expect(isApprovalRequired({
      toolName: "civil3d_standards",
      action: "label_delete_many",
      capabilities: deleteMany.capabilities,
      safeForRetry: deleteMany.safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);
    expect(deleteMany.inputSchema.safeParse({ action: "label_delete_many", handles: [] }).success).toBe(false);

    sendCommandMock.mockResolvedValue({ deleted: 2 });
    await deleteMany.execute(deleteMany.inputSchema.parse({ action: "label_delete_many", handles: ["7BFB", "7BFC"] }));
    expect(sendCommandMock).toHaveBeenCalledWith("deleteLabels", { handles: ["7BFB", "7BFC"] });
  });

  it("reads and copies label styles; only the copy needs approval", async () => {
    const { label_style_get: styleGet, label_style_create: styleCreate } = STANDARDS_DOMAIN_DEFINITION.actions;
    for (const [action, definition, expected] of [["label_style_get", styleGet, false], ["label_style_create", styleCreate, true]] as const) {
      expect(isApprovalRequired({ toolName: "civil3d_standards", action, capabilities: definition.capabilities, safeForRetry: definition.safeForRetry, requiresActiveDrawing: true })).toBe(expected);
    }
    expect(styleCreate.inputSchema.safeParse({ action: "label_style_create", kind: "pipe", baseStyle: "Name Only", newName: "ITZA", components: [{ name: "Text", height: 0 }] }).success).toBe(false);

    sendCommandMock.mockResolvedValue({});
    const components = [{ name: "Text", contents: "<[Name(CP)]>", height: 0.0018 }];
    await styleCreate.execute(styleCreate.inputSchema.parse({ action: "label_style_create", kind: "pipe", baseStyle: "Name Only", newName: "ITZA Caño", components }));
    expect(sendCommandMock).toHaveBeenCalledWith("createLabelStyle", { kind: "pipe", baseStyle: "Name Only", newName: "ITZA Caño", components });
  });

  it("sends a whole label batch in one addLabels command", async () => {
    sendCommandMock.mockResolvedValue({ created: 35 });
    await addMany.execute(addMany.inputSchema.parse({ action: "label_add_many", kind: "pipe", networkName: "Pluvial", labelStyle: "Length Description and Slope", layer: "ETIQ-PLUVIAL" }));

    expect(sendCommandMock).toHaveBeenCalledTimes(1);
    expect(sendCommandMock).toHaveBeenCalledWith("addLabels", expect.objectContaining({
      kind: "pipe",
      networkName: "Pluvial",
      labelStyle: "Length Description and Slope",
      layer: "ETIQ-PLUVIAL",
    }));
  });

  it("rescales viewports with approval, in one setViewportScale command", async () => {
    const rescale = PLAN_PRODUCTION_DOMAIN_DEFINITION.actions.layout_viewport_scale;
    expect(isApprovalRequired({ toolName: "civil3d_plan_production", action: "layout_viewport_scale", capabilities: rescale.capabilities, safeForRetry: rescale.safeForRetry, requiresActiveDrawing: true })).toBe(true);
    expect(rescale.inputSchema.safeParse({ action: "layout_viewport_scale", layoutName: "L1", scale: 0 }).success).toBe(false);

    sendCommandMock.mockResolvedValue({});
    await rescale.execute(rescale.inputSchema.parse({ action: "layout_viewport_scale", layoutName: "L1", scale: 1000 }));
    expect(sendCommandMock).toHaveBeenCalledWith("setViewportScale", { layoutName: "L1", scale: 1000, viewportHandle: undefined, annotationScale: undefined });
  });

  it("edits a label style and freezes viewport layers, both with approval", async () => {
    const styleEdit = STANDARDS_DOMAIN_DEFINITION.actions.label_style_edit;
    const freeze = PLAN_PRODUCTION_DOMAIN_DEFINITION.actions.layout_viewport_freeze;
    expect(isApprovalRequired({ toolName: "civil3d_standards", action: "label_style_edit", capabilities: styleEdit.capabilities, safeForRetry: styleEdit.safeForRetry, requiresActiveDrawing: true })).toBe(true);
    expect(isApprovalRequired({ toolName: "civil3d_plan_production", action: "layout_viewport_freeze", capabilities: freeze.capabilities, safeForRetry: freeze.safeForRetry, requiresActiveDrawing: true })).toBe(true);
    expect(styleEdit.inputSchema.safeParse({ action: "label_style_edit", kind: "pipe", styleName: "X", components: [] }).success).toBe(false);

    sendCommandMock.mockResolvedValue({});
    await styleEdit.execute(styleEdit.inputSchema.parse({ action: "label_style_edit", kind: "parcel_area", styleName: "ITZA Lote", components: [{ name: "Area", height: 0.0014 }] }));
    expect(sendCommandMock).toHaveBeenLastCalledWith("editLabelStyle", { kind: "parcel_area", styleName: "ITZA Lote", components: [{ name: "Area", height: 0.0014 }] });
    await freeze.execute(freeze.inputSchema.parse({ action: "layout_viewport_freeze", layoutName: "L4", layers: ["C-ROAD-ASSM"] }));
    expect(sendCommandMock).toHaveBeenLastCalledWith("freezeViewportLayers", { layoutName: "L4", layers: ["C-ROAD-ASSM"], thaw: false });
  });

  it("deletes a layout only with approval", async () => {
    const remove = PLAN_PRODUCTION_DOMAIN_DEFINITION.actions.layout_delete;
    expect(isApprovalRequired({ toolName: "civil3d_plan_production", action: "layout_delete", capabilities: remove.capabilities, safeForRetry: remove.safeForRetry, requiresActiveDrawing: true })).toBe(true);
    sendCommandMock.mockResolvedValue({ deleted: true });
    await remove.execute(remove.inputSchema.parse({ action: "layout_delete", layoutName: "TEST" }));
    expect(sendCommandMock).toHaveBeenCalledWith("deleteLayout", { layoutName: "TEST" });
    expect(addMany.inputSchema.safeParse({ action: "label_add_many", kind: "parcel_segment", siteName: "Loteo", position: 1.5 }).success).toBe(false);
  });

  it("validates layouts and paper-space drafting", async () => {
    sendCommandMock.mockResolvedValue({});
    const viewport = { x: 200, y: 150, width: 380, height: 250, viewCenterX: 5120, viewCenterY: 10080, scale: 1000, frozenLayers: ["ARBOLADO"] };
    await layoutCreate.execute(layoutCreate.inputSchema.parse({ action: "layout_create", layoutName: "L1", viewports: [viewport] }));
    expect(sendCommandMock).toHaveBeenCalledWith("createLayout", expect.objectContaining({ layoutName: "L1", viewports: [viewport] }));

    expect(layoutCreate.inputSchema.safeParse({ action: "layout_create", layoutName: "L1", viewports: [{ ...viewport, scale: 0 }] }).success).toBe(false);
    expect(addEntities.inputSchema.safeParse({ action: "layout_add_entities", layoutName: "L1" }).success).toBe(false);

    const table = { x: 10, y: 100, rowHeight: 6, columnWidths: [40, 20], rows: [["Lotes", "80"]] };
    await addEntities.execute(addEntities.inputSchema.parse({ action: "layout_add_entities", layoutName: "L1", texts: [{ text: "N", x: 1, y: 2, height: 3, align: "middle-center" }], tables: [table] }));
    expect(sendCommandMock).toHaveBeenLastCalledWith("addLayoutEntities", {
      layoutName: "L1",
      layer: undefined,
      lines: [],
      polylines: [],
      texts: [{ text: "N", x: 1, y: 2, height: 3, align: "middle-center" }],
      tables: [table],
    });
  });
});
