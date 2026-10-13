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

const deleteActions = [
  { action: "delete", args: { action: "delete", name: "Pluvial" }, method: "deletePipeNetwork", payload: { name: "Pluvial" } },
  {
    action: "delete_pipe",
    args: { action: "delete_pipe", networkName: "Pluvial", pipeName: "P-1" },
    method: "deletePipeFromNetwork",
    payload: { networkName: "Pluvial", pipeName: "P-1" },
  },
  {
    action: "delete_structure",
    args: { action: "delete_structure", networkName: "Pluvial", structureName: "C1-1" },
    method: "deleteStructureFromNetwork",
    payload: { networkName: "Pluvial", structureName: "C1-1" },
  },
] as const;

describe("civil3d_pipe delete actions", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it.each(deleteActions)("$action is advertised and requires approval", ({ action }) => {
    const pipe = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_pipe");
    expect(pipe!.operations).toContain(action);

    const definition = PIPE_DOMAIN_DEFINITION.actions[action];
    expect(isApprovalRequired({
      toolName: "civil3d_pipe",
      action,
      capabilities: definition.capabilities,
      safeForRetry: definition.safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);
  });

  it.each(deleteActions)("$action maps to $method", async ({ action, args, method, payload }) => {
    sendCommandMock.mockResolvedValue({ deleted: true });
    const definition = PIPE_DOMAIN_DEFINITION.actions[action];

    await definition.execute(definition.inputSchema.parse(args));

    expect(sendCommandMock).toHaveBeenCalledWith(method, payload);
  });
});
