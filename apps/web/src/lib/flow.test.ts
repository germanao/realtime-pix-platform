import { describe, expect, it } from "vitest";
import {
  buildFlowProgress,
  buildProceduralFlowProgress,
  flowPlaybackCompleteStage,
  getAvailableFlowStage
} from "./flow";
import type { FlowStep, Transfer } from "./types";

const transfer: Transfer = {
  transferId: "transfer-1",
  idempotencyKey: "key-1",
  senderUserId: "sender",
  senderAccountId: "sender_bank-a",
  recipientUserId: "recipient",
  recipientAccountId: "recipient_bank-a",
  amount: 25,
  status: "requested",
  failureReason: null,
  createdAt: "2026-06-18T12:00:00Z",
  updatedAt: "2026-06-18T12:00:00Z"
};

function step(eventType: string, recordedAt: string): FlowStep {
  return {
    stepId: `${eventType}-${recordedAt}`,
    transferId: transfer.transferId,
    eventType,
    stage: eventType.includes("Debit") || eventType.includes("Credit")
      ? "wallet-ledger-service"
      : "transaction-service",
    title: eventType,
    detail: eventType,
    recordedAt,
    sourceEventId: `${eventType}-source`,
    producer: "test",
    correlationId: transfer.transferId,
    causationId: null,
    outcome: eventType.includes("Failed") ? "failure" : "success"
  };
}

describe("buildFlowProgress", () => {
  it("reaches the same completed state for duplicate and out-of-order events", () => {
    const flow = [
      step("PixTransferCompleted.v1", "2026-06-18T12:00:04Z"),
      step("PixCreditSucceeded.v1", "2026-06-18T12:00:03Z"),
      step("PixTransferRequested.v1", "2026-06-18T12:00:01Z"),
      step("PixDebitSucceeded.v1", "2026-06-18T12:00:02Z"),
      step("PixDebitSucceeded.v1", "2026-06-18T12:00:02Z")
    ];

    const result = buildFlowProgress(flow, transfer);

    expect(result.terminal).toBe(true);
    expect(result.activeEdgeIndex).toBe(6);
    expect(result.nodes.wallet.status).toBe("success");
    expect(result.nodes["transaction-start"].status).toBe("success");
    expect(result.nodes["transaction-confirm"].status).toBe("success");
    expect(result.nodes["browser-end"].status).toBe("success");
  });

  it("marks the wallet and final result as failed", () => {
    const failedTransfer = { ...transfer, status: "failed", failureReason: "Insufficient funds" };
    const flow = [
      step("PixTransferRequested.v1", "2026-06-18T12:00:01Z"),
      step("PixDebitFailed.v1", "2026-06-18T12:00:02Z"),
      step("PixTransferFailed.v1", "2026-06-18T12:00:03Z")
    ];

    const result = buildFlowProgress(flow, failedTransfer);

    expect(result.nodes.wallet.status).toBe("failure");
    expect(result.nodes["transaction-start"].status).toBe("success");
    expect(result.nodes["transaction-confirm"].status).toBe("failure");
    expect(result.nodes["browser-end"].status).toBe("failure");
  });

  it("moves chronologically from request through credit before completion", () => {
    const requested = buildFlowProgress(
      [step("PixTransferRequested.v1", "2026-06-18T12:00:01Z")],
      transfer
    );
    expect(requested.nodes["transaction-start"].status).toBe("success");
    expect(requested.nodes["event-bus"].status).toBe("active");
    expect(requested.nodes.wallet.status).toBe("active");
    expect(requested.nodes["transaction-confirm"].status).toBe("idle");

    const credited = buildFlowProgress(
      [
        step("PixTransferRequested.v1", "2026-06-18T12:00:01Z"),
        step("PixDebitSucceeded.v1", "2026-06-18T12:00:02Z"),
        step("PixCreditSucceeded.v1", "2026-06-18T12:00:03Z")
      ],
      transfer
    );
    expect(credited.nodes.wallet.status).toBe("success");
    expect(credited.nodes["transaction-confirm"].status).toBe("active");
    expect(credited.nodes.realtime.status).toBe("active");
  });

  it("reveals exactly one primary node at a time during playback", () => {
    const completedTransfer = { ...transfer, status: "completed" as const };
    const completedFlow = [
      step("PixTransferRequested.v1", "2026-06-18T12:00:01Z"),
      step("PixDebitSucceeded.v1", "2026-06-18T12:00:02Z"),
      step("PixCreditSucceeded.v1", "2026-06-18T12:00:03Z"),
      step("PixTransferCompleted.v1", "2026-06-18T12:00:04Z")
    ];

    const beginning = buildProceduralFlowProgress(0, completedFlow, completedTransfer);
    expect(beginning.nodes["browser-start"].status).toBe("active");
    expect(beginning.nodes.gateway.status).toBe("idle");
    expect(beginning.nodes["transaction-confirm"].status).toBe("idle");
    expect(beginning.nodes["browser-end"].status).toBe("idle");

    const middle = buildProceduralFlowProgress(4, completedFlow, completedTransfer);
    expect(middle.nodes["event-bus"].status).toBe("success");
    expect(middle.nodes.wallet.status).toBe("active");
    expect(middle.nodes["transaction-confirm"].status).toBe("idle");
    expect(middle.nodes["browser-end"].status).toBe("idle");

    const end = buildProceduralFlowProgress(
      flowPlaybackCompleteStage,
      completedFlow,
      completedTransfer
    );
    expect(end.terminal).toBe(true);
    expect(end.nodes["browser-start"].status).toBe("success");
    expect(end.nodes["browser-end"].status).toBe("success");
  });

  it("waits for backend milestones before advancing the visual playback", () => {
    expect(getAvailableFlowStage([], transfer)).toBe(2);
    expect(
      getAvailableFlowStage(
        [step("PixTransferRequested.v1", "2026-06-18T12:00:01Z")],
        transfer
      )
    ).toBe(4);
    expect(
      getAvailableFlowStage(
        [
          step("PixTransferRequested.v1", "2026-06-18T12:00:01Z"),
          step("PixCreditSucceeded.v1", "2026-06-18T12:00:03Z")
        ],
        transfer
      )
    ).toBe(5);
    expect(getAvailableFlowStage([], { ...transfer, status: "completed" })).toBe(
      flowPlaybackCompleteStage
    );
  });
});

