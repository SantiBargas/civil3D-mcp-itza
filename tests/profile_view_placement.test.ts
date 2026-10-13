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

import { PROFILE_DOMAIN_DEFINITION } from "../src/tools/domains/profileDomain.js";
import { GENERATED_TOOL_CATALOG_ENTRIES } from "../src/tools/toolManifest.js";

const { view_create: viewCreate, view_move: viewMove } = PROFILE_DOMAIN_DEFINITION.actions;

describe("civil3d_profile view placement", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
    sendCommandMock.mockResolvedValue({});
  });

  it("advertises view_move", () => {
    const profile = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_profile");
    expect(profile!.operations).toContain("view_move");
  });

  it("accepts both insertion coordinates or neither, never just one", () => {
    const base = { action: "view_create", alignmentName: "A", profileViewName: "PV" };
    expect(viewCreate.inputSchema.safeParse(base).success).toBe(true);
    expect(viewCreate.inputSchema.safeParse({ ...base, insertX: 1, insertY: 2 }).success).toBe(true);
    expect(viewCreate.inputSchema.safeParse({ ...base, insertX: 1 }).success).toBe(false);
    expect(viewMove.inputSchema.safeParse({ action: "view_move", profileViewName: "PV", insertY: 2 }).success).toBe(false);
  });

  it("sends null coordinates for automatic placement", async () => {
    await viewCreate.execute({ action: "view_create", alignmentName: "A", profileViewName: "PV" });
    await viewMove.execute({ action: "view_move", profileViewName: "PV" });

    expect(sendCommandMock).toHaveBeenNthCalledWith(1, "profileViewCreate", expect.objectContaining({
      insertX: null,
      insertY: null,
    }));
    expect(sendCommandMock).toHaveBeenNthCalledWith(2, "profileViewMove", {
      profileViewName: "PV",
      insertX: null,
      insertY: null,
    });
  });
});
