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

import { PARCEL_DOMAIN_DEFINITION } from "../src/tools/domains/parcelDomain.js";
import { isApprovalRequired } from "../src/tools/approvalPolicy.js";
import { GENERATED_TOOL_CATALOG_ENTRIES } from "../src/tools/toolManifest.js";

const square = (x: number): Array<[number, number]> => [[x, 0], [x + 10, 0], [x + 10, 30], [x, 30]];
const createMany = PARCEL_DOMAIN_DEFINITION.actions.create_many;

describe("civil3d_parcel create_many", () => {
  beforeEach(() => {
    sendCommandMock.mockReset();
  });

  it("is advertised by the canonical parcel tool", () => {
    const parcel = GENERATED_TOOL_CATALOG_ENTRIES.find((entry) => entry.toolName === "civil3d_parcel");
    expect(parcel!.operations).toContain("create_many");
  });

  it("requires approval because it creates objects", () => {
    expect(isApprovalRequired({
      toolName: "civil3d_parcel",
      action: "create_many",
      capabilities: createMany.capabilities,
      safeForRetry: createMany.safeForRetry,
      requiresActiveDrawing: true,
    })).toBe(true);
  });

  it("validates the batch input", () => {
    const schema = createMany.inputSchema;
    const valid = { action: "create_many", siteName: "Loteo", parcels: [{ name: "L1", points: square(0) }] };

    expect(schema.safeParse(valid).success).toBe(true);
    expect(schema.safeParse({ ...valid, parcels: [] }).success).toBe(false);
    expect(schema.safeParse({ ...valid, parcels: [{ points: [[0, 0], [1, 0]] }] }).success).toBe(false);
    expect(schema.safeParse({
      ...valid,
      parcels: Array.from({ length: 301 }, (_, index) => ({ points: square(index * 10) })),
    }).success).toBe(false);
  });

  it("sends every parcel to the plugin in a single createParcels command", async () => {
    sendCommandMock.mockResolvedValue({ siteName: "Loteo", createdCount: 2, totalArea: 600, parcels: [] });

    await createMany.execute({
      action: "create_many",
      siteName: "Loteo",
      parcels: [
        { name: "L1", points: square(0) },
        { points: square(10), style: "Property" },
      ],
    });

    expect(sendCommandMock).toHaveBeenCalledTimes(1);
    expect(sendCommandMock).toHaveBeenCalledWith("createParcels", {
      siteName: "Loteo",
      parcels: [
        { name: "L1", points: square(0), style: null, areaLabelStyle: null },
        { name: null, points: square(10), style: "Property", areaLabelStyle: null },
      ],
    });
  });
});
