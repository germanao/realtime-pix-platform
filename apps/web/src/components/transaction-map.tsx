"use client";

import {
  Activity,
  Bot,
  Check,
  CircleUserRound,
  CloudCog,
  Database,
  GitBranch,
  Monitor,
  RadioTower,
  RotateCcw,
  Server,
  Users,
  X
} from "lucide-react";
import { motion } from "motion/react";
import {
  Background,
  Controls,
  Handle,
  Position,
  ReactFlow,
  type Edge,
  type Node,
  type NodeProps
} from "@xyflow/react";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  buildProceduralFlowProgress,
  buildFlowProgress,
  flowEdges,
  flowPlaybackCompleteStage,
  getAvailableFlowStage,
  primaryFlowNodes,
  type FlowNodeId,
  type FlowNodeStatus
} from "@/lib/flow";
import { money } from "@/lib/presentation";
import type { FlowStep, Transfer } from "@/lib/types";

type TransactionMapProps = {
  expertMode: boolean;
  flow: FlowStep[];
  transfer: Transfer | null;
  replayKey: number;
  onReplay: () => void;
};

type ServiceNodeData = {
  nodeId: FlowNodeId;
  label: string;
  subtitle: string;
  status: FlowNodeStatus;
  evidence: "idle" | "inferred" | "event";
  expertMode: boolean;
  lane: "primary" | "supporting";
  acceptsSupport: boolean;
};

type ServiceFlowNode = Node<ServiceNodeData, "service">;

const positions: Record<FlowNodeId, { x: number; y: number }> = {
  "browser-start": { x: 0, y: 125 },
  gateway: { x: 190, y: 125 },
  "transaction-start": { x: 380, y: 125 },
  "event-bus": { x: 570, y: 125 },
  wallet: { x: 760, y: 125 },
  "transaction-confirm": { x: 950, y: 125 },
  realtime: { x: 1140, y: 125 },
  "browser-end": { x: 1330, y: 125 },
  presence: { x: 190, y: 305 },
  bots: { x: 760, y: 305 }
};

export const simpleFlowLabels: Record<FlowNodeId, [string, string]> = {
  "browser-start": ["You", "Start the PIX"],
  gateway: ["Front Door", "Receives your request"],
  "transaction-start": ["Transfer Coordinator", "Starts the journey"],
  "event-bus": ["Message Route", "Carries the update"],
  wallet: ["Money Keeper", "Moves both balances"],
  "transaction-confirm": ["Completion Check", "Confirms the outcome"],
  realtime: ["Live Guide", "Reports every result"],
  "browser-end": ["Your result", "Shows what happened"],
  presence: ["Who's here", "Keeps people visible"],
  bots: ["Always available", "Demo participants"]
};

export const expertFlowLabels: Record<FlowNodeId, [string, string]> = {
  "browser-start": ["Next.js client", "POST /pix/transfers"],
  gateway: ["api-gateway", "HTTP forwarding boundary"],
  "transaction-start": ["transaction-service / request", "Saga and idempotency"],
  "event-bus": ["integration event bus", "At-least-once delivery"],
  wallet: ["wallet-ledger-service", "Debit, credit, ledger"],
  "transaction-confirm": ["transaction-service / completion", "Final saga transition"],
  realtime: ["realtime-events-service", "SignalR projection"],
  "browser-end": ["React projection", "Client state update"],
  presence: ["identity-presence-service", "Connection ownership"],
  bots: ["bot-service", "Always-on participants"]
};

const friendlyDetails: Record<FlowNodeId, string> = {
  "browser-start": "Your click creates one protected transfer attempt.",
  gateway: "The front door safely forwards your request to the right place.",
  "transaction-start": "The coordinator gives the transfer an identity and starts the journey.",
  "event-bus": "A shared message route lets each part work independently.",
  wallet: "The money keeper checks your balance, subtracts the value, and credits the recipient.",
  "transaction-confirm": "The coordinator checks the outcome and closes the transfer exactly once.",
  realtime: "The live guide turns backend updates into the animation you are watching.",
  "browser-end": "Your screen receives the final result without requiring a refresh.",
  presence: "This service tracks who is currently available to receive a PIX.",
  bots: "Demo participants stay online so there is always someone available."
};

function nodeIcon(nodeId: FlowNodeId) {
  switch (nodeId) {
    case "browser-start":
      return CircleUserRound;
    case "browser-end":
      return Monitor;
    case "gateway":
      return Server;
    case "transaction-start":
    case "transaction-confirm":
      return GitBranch;
    case "event-bus":
      return CloudCog;
    case "wallet":
      return Database;
    case "realtime":
      return RadioTower;
    case "presence":
      return Users;
    case "bots":
      return Bot;
  }
}

function ServiceNode({ data }: NodeProps<ServiceFlowNode>) {
  const Icon = nodeIcon(data.nodeId);
  const StatusIcon = data.status === "failure" ? X : data.status === "success" ? Check : Activity;

  return (
    <motion.div
      animate={data.status === "active" ? { scale: [1, 1.025, 1] } : { scale: 1 }}
      className={`serviceNode ${data.lane} ${data.status}`}
      data-flow-node={data.nodeId}
      data-lane={data.lane}
      transition={{ duration: 1.4, repeat: data.status === "active" ? Infinity : 0 }}
    >
      {data.lane === "primary" && (
        <Handle
          className="flowHandle"
          id="main-target"
          position={Position.Left}
          type="target"
        />
      )}
      {data.acceptsSupport && (
        <Handle
          className="flowHandle"
          id="support-target"
          position={Position.Bottom}
          type="target"
        />
      )}
      {data.lane === "supporting" && (
        <Handle
          className="flowHandle"
          id="support-source"
          position={Position.Top}
          type="source"
        />
      )}
      <span className="serviceIcon">
        <Icon size={20} />
      </span>
      <span className="serviceCopy">
        <strong>{data.label}</strong>
        <small>{data.subtitle}</small>
      </span>
      <span className="nodeStatus" title={data.evidence === "inferred" ? "Confirmed by HTTP response" : undefined}>
        <StatusIcon size={14} />
      </span>
      {data.lane === "primary" && (
        <Handle
          className="flowHandle"
          id="main-source"
          position={Position.Right}
          type="source"
        />
      )}
    </motion.div>
  );
}

const nodeTypes = { service: ServiceNode };
const playbackStepMilliseconds = 1200;

export function TransactionMap({
  expertMode,
  flow,
  transfer,
  replayKey,
  onReplay
}: TransactionMapProps) {
  const [selectedNodeId, setSelectedNodeId] = useState<FlowNodeId>("transaction-start");
  const [playbackStage, setPlaybackStage] = useState<number | null>(null);
  const previousReplayKey = useRef(replayKey);
  const playbackStageRef = useRef<number | null>(null);
  const latestFlowRef = useRef(flow);
  const latestTransferRef = useRef(transfer);

  latestFlowRef.current = flow;
  latestTransferRef.current = transfer;

  useEffect(() => {
    if (previousReplayKey.current === replayKey) {
      return;
    }
    previousReplayKey.current = replayKey;
    if (!latestTransferRef.current) {
      playbackStageRef.current = null;
      setPlaybackStage(null);
      return;
    }

    playbackStageRef.current = 0;
    setPlaybackStage(0);
    let finishTimeout: number | undefined;
    const interval = window.setInterval(() => {
      const currentStage = playbackStageRef.current;
      const currentTransfer = latestTransferRef.current;
      if (currentStage === null || !currentTransfer) {
        return;
      }

      const availableStage = getAvailableFlowStage(latestFlowRef.current, currentTransfer);
      if (currentStage >= availableStage) {
        return;
      }

      const nextStage = currentStage + 1;
      playbackStageRef.current = nextStage;
      setPlaybackStage(nextStage);

      if (nextStage === flowPlaybackCompleteStage) {
        window.clearInterval(interval);
        finishTimeout = window.setTimeout(() => {
          playbackStageRef.current = null;
          setPlaybackStage(null);
        }, playbackStepMilliseconds);
      }
    }, playbackStepMilliseconds);

    return () => {
      window.clearInterval(interval);
      if (finishTimeout !== undefined) {
        window.clearTimeout(finishTimeout);
      }
    };
  }, [replayKey]);

  const progress = useMemo(
    () =>
      playbackStage !== null && transfer
        ? buildProceduralFlowProgress(playbackStage, flow, transfer)
        : buildFlowProgress(flow, transfer),
    [flow, playbackStage, transfer]
  );
  const labels = expertMode ? expertFlowLabels : simpleFlowLabels;

  const nodes = useMemo<ServiceFlowNode[]>(
    () =>
      (Object.keys(positions) as FlowNodeId[]).map((nodeId) => ({
        id: nodeId,
        position: positions[nodeId],
        type: "service",
        data: {
          nodeId,
          label: labels[nodeId][0],
          subtitle: labels[nodeId][1],
          status: progress.nodes[nodeId].status,
          evidence: progress.nodes[nodeId].evidence,
          expertMode,
          lane: nodeId === "presence" || nodeId === "bots" ? "supporting" : "primary",
          acceptsSupport: nodeId === "gateway" || nodeId === "wallet"
        },
        draggable: false,
        selectable: true
      })),
    [expertMode, labels, progress.nodes]
  );

  const edges = useMemo<Edge[]>(() => {
    const mainEdges = flowEdges.map(([source, target], index) => {
      const reached = progress.activeEdgeIndex >= index;
      const failed = progress.nodes[target].status === "failure";
      return {
        id: `${source}-${target}`,
        source,
        target,
        sourceHandle: "main-source",
        targetHandle: "main-target",
        type: "straight",
        animated: reached && !progress.terminal,
        className: reached ? (failed ? "flowEdge failed" : "flowEdge reached") : "flowEdge",
        style: { strokeWidth: reached ? 3 : 1.5 }
      };
    });

    return [
      ...mainEdges,
      {
        id: "presence-gateway",
        source: "presence",
        target: "gateway",
        sourceHandle: "support-source",
        targetHandle: "support-target",
        type: "straight",
        className: "flowEdge supporting"
      },
      {
        id: "bots-wallet",
        source: "bots",
        target: "wallet",
        sourceHandle: "support-source",
        targetHandle: "support-target",
        type: "straight",
        className: "flowEdge supporting"
      }
    ];
  }, [progress]);

  const selectedSteps = flow.filter((step) => {
    if (selectedNodeId === "wallet") return step.stage === "wallet-ledger-service";
    if (selectedNodeId === "transaction-start") {
      return step.eventType === "PixTransferRequested.v1";
    }
    if (selectedNodeId === "transaction-confirm") {
      return step.eventType === "PixTransferCompleted.v1" || step.eventType === "PixTransferFailed.v1";
    }
    if (selectedNodeId === "realtime") return step.producer === "realtime-events-service";
    return false;
  });

  return (
    <section className={`journeySection ${expertMode ? "expertJourney" : ""}`}>
      <div className="journeyHeading">
        <div>
          <span className="sectionKicker">{expertMode ? "Transfer topology" : "Your PIX journey"}</span>
          <h2>{transfer ? `Following ${money(transfer.amount)}` : "Ready for the next transfer"}</h2>
          <p>
            {expertMode
              ? "Solid activity is confirmed by integration events; dotted supporting paths remain idle."
              : "Watch each part wake up as your money moves. Choose any stop to understand its job."}
          </p>
        </div>
        <button
          className="secondaryAction"
          disabled={!flow.length}
          onClick={onReplay}
          type="button"
        >
          <RotateCcw size={16} />
          Replay
        </button>
      </div>

      <div className="mapCanvas" data-testid="transaction-map">
        <ReactFlow
          edges={edges}
          fitView
          fitViewOptions={{ padding: 0.08 }}
          maxZoom={1.15}
          minZoom={0.55}
          nodes={nodes}
          nodesConnectable={false}
          nodesDraggable={false}
          nodeTypes={nodeTypes}
          onNodeClick={(_, node) => setSelectedNodeId(node.id as FlowNodeId)}
          panOnDrag={expertMode}
          proOptions={{ hideAttribution: true }}
          zoomOnDoubleClick={false}
          zoomOnScroll={expertMode}
        >
          <Background color={expertMode ? "#28433f" : "#d9e3de"} gap={24} size={1} />
          {expertMode && <Controls position="bottom-right" showInteractive={false} />}
        </ReactFlow>
      </div>

      <div className="mobileJourney">
        {primaryFlowNodes.map((nodeId, index) => {
          const data = nodes.find((node) => node.id === nodeId)?.data;
          if (!data) return null;
          return (
            <button
              className={`mobileJourneyStep ${data.status}`}
              key={nodeId}
              onClick={() => setSelectedNodeId(nodeId)}
              type="button"
            >
              <span>{index + 1}</span>
              <div>
                <strong>{data.label}</strong>
                <small>{data.subtitle}</small>
              </div>
            </button>
          );
        })}
      </div>

      <div className="mapInspector" aria-live="polite">
        <span className={`inspectorState ${progress.nodes[selectedNodeId].status}`} />
        <div>
          <strong>{labels[selectedNodeId][0]}</strong>
          <p>
            {expertMode
              ? selectedSteps.at(-1)?.detail ?? `${labels[selectedNodeId][1]}. No transfer event recorded here yet.`
              : friendlyDetails[selectedNodeId]}
          </p>
        </div>
        {expertMode && selectedSteps.at(-1) && (
          <dl>
            <div>
              <dt>Event</dt>
              <dd>{selectedSteps.at(-1)?.eventType}</dd>
            </div>
            <div>
              <dt>Correlation</dt>
              <dd>{selectedSteps.at(-1)?.correlationId}</dd>
            </div>
          </dl>
        )}
      </div>
    </section>
  );
}

