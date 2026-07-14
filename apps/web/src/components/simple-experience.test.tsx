import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { money } from "@/lib/presentation";
import { AmountPicker } from "./simple-experience";
import { simpleFlowLabels } from "./transaction-map";

describe("AmountPicker", () => {
  it("supports $25, $50, and a custom amount", () => {
    const onChoiceChange = vi.fn();
    const onAmountChange = vi.fn();
    const { rerender } = render(
      <AmountPicker
        amount="25"
        choice="25"
        onAmountChange={onAmountChange}
        onChoiceChange={onChoiceChange}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: money(50) }));
    expect(onChoiceChange).toHaveBeenCalledWith("50");

    rerender(
      <AmountPicker
        amount=""
        choice="custom"
        onAmountChange={onAmountChange}
        onChoiceChange={onChoiceChange}
      />
    );
    fireEvent.change(screen.getByLabelText("Custom amount"), { target: { value: "72,50" } });
    expect(onAmountChange).toHaveBeenCalledWith("72,50");
  });
});

describe("simple flow vocabulary", () => {
  it("does not expose service or event implementation names", () => {
    const renderedLabels = Object.values(simpleFlowLabels).flat().join(" ");

    expect(renderedLabels).not.toContain("service");
    expect(renderedLabels).not.toContain(".v1");
    expect(renderedLabels).not.toContain("api-gateway");
    expect(renderedLabels).toContain("Front Door");
    expect(renderedLabels).toContain("Sender bank");
    expect(renderedLabels).toContain("Recipient bank");
  });
});
