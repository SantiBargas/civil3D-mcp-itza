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

import { GEOMETRY_DOMAIN_DEFINITION } from "../src/tools/domains/geometryDomain.js";
import { isApprovalRequired } from "../src/tools/approvalPolicy.js";
import { GENERATED_TOOL_CATALOG_ENTRIES } from "../src/tools/toolManifest.js";

const { block_define: define, block_insert_many: insertMany, block_list: list } = GEOMETRY_DOMAIN_DEFINITION.actions;

describe("civil3d_geometry blocks", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("advertises the block actions; writes need approval, the listing does not", () => {
    const geometry = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_geometry");
    expect(geometry!.operations).toEqual(expect.arrayContaining(["block_define", "block_insert_many", "block_list"]));

    for (const [action, definition, expected] of [
      ["block_define", define, true],
      ["block_insert_many", insertMany, true],
      ["block_list", list, false],
    ] as const) {
      expect(isApprovalRequired({
        toolName: "civil3d_geometry",
        action,
        capabilities: definition.capabilities,
        safeForRetry: definition.safeForRetry,
        requiresActiveDrawing: true,
      })).toBe(expected);
    }
  });

  it("validates block definitions and insert batches", () => {
    expect(define.inputSchema.safeParse({ action: "block_define", blockName: "SIMBOLO", circles: [{ radius: 0.3 }] }).success).toBe(true);
    expect(define.inputSchema.safeParse({ action: "block_define", blockName: "SIMBOLO" }).success).toBe(false);
    expect(define.inputSchema.safeParse({ action: "block_define", blockName: "SIMBOLO", circles: [{ radius: 0 }] }).success).toBe(false);
    expect(insertMany.inputSchema.safeParse({ action: "block_insert_many", blockName: "SIMBOLO", items: [] }).success).toBe(false);
  });

  it("sends a whole insert batch in one insertBlocks command", async () => {
    sendCommandMock.mockResolvedValue({ inserted: 2 });
    const items = [{ x: 1, y: 2 }, { x: 3, y: 4, rotation: 90, scale: 2 }];

    await insertMany.execute(insertMany.inputSchema.parse({ action: "block_insert_many", blockName: "SIMBOLO", layer: "CAPA", items }));

    expect(sendCommandMock).toHaveBeenCalledTimes(1);
    expect(sendCommandMock).toHaveBeenCalledWith("insertBlocks", { blockName: "SIMBOLO", layer: "CAPA", items });
  });

  it("maps define and list to their plugin commands", async () => {
    sendCommandMock.mockResolvedValue({});

    await define.execute(define.inputSchema.parse({ action: "block_define", blockName: "SIMBOLO", lines: [{ x1: -1, y1: 0, x2: 1, y2: 0 }] }));
    await list.execute(list.inputSchema.parse({ action: "block_list" }));

    expect(sendCommandMock).toHaveBeenNthCalledWith(1, "defineBlock", {
      blockName: "SIMBOLO",
      circles: [],
      lines: [{ x1: -1, y1: 0, x2: 1, y2: 0 }],
    });
    expect(sendCommandMock).toHaveBeenNthCalledWith(2, "listBlocks", { blockName: undefined, includePositions: false });
  });
});
