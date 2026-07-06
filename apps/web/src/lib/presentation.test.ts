import { describe, expect, it } from "vitest";
import { money, moneyParts, sortRecipients, validateTransferAmount } from "./presentation";

describe("validateTransferAmount", () => {
  it("accepts quick and custom values within the available balance", () => {
    expect(validateTransferAmount("25", 10_000)).toBeNull();
    expect(validateTransferAmount("50,75", 10_000)).toBeNull();
  });

  it("rejects invalid precision and values above the balance", () => {
    expect(validateTransferAmount("10.999", 10_000)).toMatch(/two decimal/);
    expect(validateTransferAmount("10001", 10_000)).toMatch(/available balance/);
  });
});

describe("money", () => {
  it.each([
    [0, "$0.00"],
    [25, "$25.00"],
    [10_025, "$10,025.00"],
    [50.75, "$50.75"],
    [-25, "-$25.00"]
  ])("formats %s with US dollar separators", (value, expected) => {
    expect(money(value)).toBe(expected);
  });

  it("exposes separate symbol and numeric parts for the balance hero", () => {
    expect(moneyParts(10_025)).toEqual({
      symbol: "$",
      amount: "10,025.00"
    });
  });
});

describe("sortRecipients", () => {
  it("excludes the current user and prioritizes live people before bots", () => {
    const result = sortRecipients(
      [
        { userId: "me", displayName: "Me", isBot: false, isOnline: true, lastSeenAt: "" },
        { userId: "bot", displayName: "Always", isBot: true, isOnline: true, lastSeenAt: "" },
        { userId: "person", displayName: "Person", isBot: false, isOnline: true, lastSeenAt: "" }
      ],
      "me"
    );

    expect(result.map((user) => user.userId)).toEqual(["person", "bot"]);
  });
});
