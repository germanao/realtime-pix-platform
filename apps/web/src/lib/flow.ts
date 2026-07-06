import type { FlowStep, Transfer } from "./types";

export type FlowNodeId =
  | "browser-start"
  | "gateway"
  | "transaction-start"
  | "event-bus"
  | "wallet"
  | "transaction-confirm"
  | "realtime"
  | "browser-end"
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

const idle = (): FlowNodeState => ({ status: "idle", evidence: "idle" });

export function buildFlowProgress(flow: FlowStep[], transfer: Transfer | null): FlowProgress {
  const eventTypes = new Set(flow.map((step) => step.eventType));
  const requested = eventTypes.has("PixTransferRequested.v1");
  const debitSucceeded = eventTypes.has("PixDebitSucceeded.v1");
  const debitFailed = eventTypes.has("PixDebitFailed.v1");
  const creditSucceeded = eventTypes.has("PixCreditSucceeded.v1");
  const completed = eventTypes.has("PixTransferCompleted.v1") || transfer?.status === "completed";
  const failed = eventTypes.has("PixTransferFailed.v1") || transfer?.status === "failed";
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
          status: debitSucceeded || debitFailed || creditSucceeded || completed || failed
            ? "success"
            : "active",
          evidence: "event"
        }
      : idle(),
    wallet: debitFailed
      ? { status: "failure", evidence: "event" }
      : creditSucceeded
        ? { status: "success", evidence: "event" }
        : debitSucceeded
          ? { status: "active", evidence: "event" }
          : requested
            ? { status: "active", evidence: "event" }
            : idle(),
    "transaction-confirm": failed
      ? { status: "failure", evidence: "event" }
      : completed
        ? { status: "success", evidence: "event" }
        : creditSucceeded
          ? { status: "active", evidence: "event" }
          : idle(),
    realtime:
      completed || failed
        ? { status: failed ? "failure" : "success", evidence: "event" }
        : flow.length > 0
          ? { status: "active", evidence: "event" }
          : idle(),
    "browser-end":
      completed || failed
        ? { status: failed ? "failure" : "success", evidence: "event" }
        : idle(),
    presence: idle(),
    bots: idle()
  };

  let activeEdgeIndex = -1;
  if (hasTransfer) activeEdgeIndex = 1;
  if (requested) activeEdgeIndex = 2;
  if (debitSucceeded || debitFailed) activeEdgeIndex = 3;
  if (creditSucceeded) activeEdgeIndex = 4;
  if (completed || failed) activeEdgeIndex = 6;

  return { nodes, activeEdgeIndex, terminal: completed || failed };
}

export const flowEdges = [
  ["browser-start", "gateway"],
  ["gateway", "transaction-start"],
  ["transaction-start", "event-bus"],
  ["event-bus", "wallet"],
  ["wallet", "transaction-confirm"],
  ["transaction-confirm", "realtime"],
  ["realtime", "browser-end"]
] as const;

export const primaryFlowNodes: FlowNodeId[] = [
  "browser-start",
  "gateway",
  "transaction-start",
  "event-bus",
  "wallet",
  "transaction-confirm",
  "realtime",
  "browser-end"
];
