import type { FlowStep, Transfer } from "./types";

export type FlowNodeId =
  | "browser-start"
  | "gateway"
  | "transaction-start"
  | "event-bus"
  | "sender-bank"
  | "recipient-bank"
  | "transaction-confirm"
  | "realtime"
  | "browser-end"
  | "compensation"
  | "presence"
  | "bots";

export type FlowNodeStatus = "idle" | "active" | "success" | "failure";

export type FlowNodeState = {
  status: FlowNodeStatus;
  evidence: "idle" | "inferred" | "event";
};

export type FlowProgress = {
  nodes: Record<FlowNodeId, FlowNodeState>;
  activeEdgeIndex: number;
  terminal: boolean;
};

export const flowPlaybackCompleteStage = 9;

const idle = (): FlowNodeState => ({ status: "idle", evidence: "idle" });

export function buildFlowProgress(flow: FlowStep[], transfer: Transfer | null): FlowProgress {
  const eventTypes = new Set(flow.map((step) => step.eventType));
  const requested = eventTypes.has("PixTransferRequested.v1");
  const debitSucceeded = eventTypes.has("FundsDebited.v1") || eventTypes.has("PixDebitSucceeded.v1");
  const debitFailed = eventTypes.has("FundsDebitRejected.v1") || eventTypes.has("PixDebitFailed.v1");
  const creditSucceeded = eventTypes.has("FundsCredited.v1") || eventTypes.has("PixCreditSucceeded.v1");
  const creditFailed = eventTypes.has("FundsCreditRejected.v1");
  const refunded = eventTypes.has("FundsRefunded.v1") || transfer?.sagaState === "compensated";
  const refundFailed = eventTypes.has("FundsRefundRejected.v1") || transfer?.sagaState === "manual_intervention";
  const compensating = transfer?.sagaState === "compensation_pending";
  const completed =
    eventTypes.has("PixTransferCompleted.v1") ||
    eventTypes.has("PixTransferCompleted.v2") ||
    transfer?.sagaState === "completed" ||
    transfer?.status === "completed";
  const failed =
    eventTypes.has("PixTransferFailed.v1") ||
    eventTypes.has("PixTransferFailed.v2") ||
    transfer?.sagaState === "failed" ||
    transfer?.sagaState === "manual_intervention";
  const terminal = completed || failed || refunded;
  const hasTransfer = transfer !== null;

  const nodes: Record<FlowNodeId, FlowNodeState> = {
    "browser-start": hasTransfer
      ? { status: "success", evidence: "inferred" }
      : { status: "active", evidence: "inferred" },
    gateway: hasTransfer ? { status: "success", evidence: "inferred" } : idle(),
    "transaction-start": requested
      ? { status: "success", evidence: "event" }
      : hasTransfer
        ? { status: "active", evidence: "inferred" }
        : idle(),
    "event-bus": requested
      ? {
          status: debitSucceeded || debitFailed || creditSucceeded || creditFailed || terminal
            ? "success"
            : "active",
          evidence: "event"
        }
      : idle(),
    "sender-bank": debitFailed
      ? { status: "failure", evidence: "event" }
      : refundFailed
        ? { status: "failure", evidence: "event" }
        : refunded
        ? { status: "success", evidence: "event" }
        : compensating
          ? { status: "active", evidence: "event" }
          : debitSucceeded
            ? { status: "success", evidence: "event" }
          : requested
            ? { status: "active", evidence: "event" }
            : idle(),
    "recipient-bank": creditFailed
      ? { status: "failure", evidence: "event" }
      : creditSucceeded
        ? { status: "success", evidence: "event" }
        : debitSucceeded
          ? { status: "active", evidence: "event" }
          : idle(),
    "transaction-confirm": failed
      ? { status: "failure", evidence: "event" }
      : completed
        ? { status: "success", evidence: "event" }
        : creditSucceeded || creditFailed || compensating
          ? { status: "active", evidence: "event" }
          : idle(),
    realtime:
      terminal
        ? { status: failed ? "failure" : "success", evidence: "event" }
        : flow.length > 0
          ? { status: "active", evidence: "event" }
          : idle(),
    "browser-end":
      terminal
        ? { status: failed ? "failure" : "success", evidence: "event" }
        : idle(),
    compensation: refundFailed
      ? { status: "failure", evidence: "event" }
      : refunded
        ? { status: "success", evidence: "event" }
        : compensating
          ? { status: "active", evidence: "event" }
          : idle(),
    presence: idle(),
    bots: idle()
  };

  let activeEdgeIndex = -1;
  if (hasTransfer) activeEdgeIndex = 1;
  if (requested) activeEdgeIndex = 2;
  if (debitSucceeded || debitFailed) activeEdgeIndex = 3;
  if (creditSucceeded || creditFailed) activeEdgeIndex = 4;
  if (terminal) activeEdgeIndex = 7;

  return { nodes, activeEdgeIndex, terminal };
}

export function getAvailableFlowStage(flow: FlowStep[], transfer: Transfer | null) {
  if (!transfer) {
    return 0;
  }

  const eventTypes = new Set(flow.map((step) => step.eventType));
  const requested = eventTypes.has("PixTransferRequested.v1");
  const debitRecorded =
    eventTypes.has("FundsDebited.v1") ||
    eventTypes.has("FundsDebitRejected.v1") ||
    eventTypes.has("PixDebitSucceeded.v1") ||
    eventTypes.has("PixDebitFailed.v1");
  const creditRecorded =
    eventTypes.has("FundsCredited.v1") ||
    eventTypes.has("FundsCreditRejected.v1") ||
    eventTypes.has("PixCreditSucceeded.v1");
  const terminal =
    eventTypes.has("PixTransferCompleted.v1") ||
    eventTypes.has("PixTransferCompleted.v2") ||
    eventTypes.has("PixTransferFailed.v1") ||
    eventTypes.has("PixTransferFailed.v2") ||
    eventTypes.has("PixTransferCompensated.v1") ||
    transfer.status === "completed" ||
    transfer.status === "failed" ||
    ["completed", "compensated", "failed", "manual_intervention"].includes(transfer.sagaState ?? "");

  if (terminal) return flowPlaybackCompleteStage;
  if (creditRecorded) return 6;
  if (debitRecorded) return 5;
  if (requested) return 4;
  return 2;
}

export function buildProceduralFlowProgress(
  stage: number,
  flow: FlowStep[],
  transfer: Transfer
): FlowProgress {
  const finalProgress = buildFlowProgress(flow, transfer);
  const nodes = Object.fromEntries(
    (Object.keys(finalProgress.nodes) as FlowNodeId[]).map((nodeId) => [nodeId, idle()])
  ) as Record<FlowNodeId, FlowNodeState>;
  const safeStage = Math.min(Math.max(stage, 0), flowPlaybackCompleteStage);

  primaryFlowNodes.forEach((nodeId, index) => {
    if (index < safeStage || safeStage === flowPlaybackCompleteStage) {
      const finalState = finalProgress.nodes[nodeId];
      nodes[nodeId] = {
        status: finalState.status === "failure" ? "failure" : "success",
        evidence: finalState.evidence === "idle" ? "inferred" : finalState.evidence
      };
      return;
    }

    if (index === safeStage) {
      nodes[nodeId] = {
        status: "active",
        evidence: index <= 2 ? "inferred" : "event"
      };
    }
  });

  if (safeStage === flowPlaybackCompleteStage) {
    nodes.compensation = finalProgress.nodes.compensation;
  }

  return {
    nodes,
    activeEdgeIndex: safeStage === 0 ? -1 : Math.min(safeStage - 1, flowEdges.length - 1),
    terminal: safeStage === flowPlaybackCompleteStage
  };
}

export const flowEdges = [
  ["browser-start", "gateway"],
  ["gateway", "transaction-start"],
  ["transaction-start", "event-bus"],
  ["event-bus", "sender-bank"],
  ["sender-bank", "recipient-bank"],
  ["recipient-bank", "transaction-confirm"],
  ["transaction-confirm", "realtime"],
  ["realtime", "browser-end"]
] as const;

export const primaryFlowNodes: FlowNodeId[] = [
  "browser-start",
  "gateway",
  "transaction-start",
  "event-bus",
  "sender-bank",
  "recipient-bank",
  "transaction-confirm",
  "realtime",
  "browser-end"
];
