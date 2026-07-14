export type Session = {
  clientId: string;
  userId: string;
  displayName: string;
  sessionToken: string;
  lastSeenAt: string;
};

export type PresenceUser = {
  userId: string;
  displayName: string;
  isBot: boolean;
  isOnline: boolean;
  lastSeenAt: string;
};

export type Account = {
  accountId: string;
  userId: string;
  bankId: "bank-a" | "bank-b" | string;
  bankName: string;
  balance: number;
};

export type WalletBootstrap = {
  primaryAccount: Account;
  welcomeCreditApplied: boolean;
};

export type LedgerEntry = {
  ledgerEntryId: string;
  accountId: string;
  userId: string;
  amount: number;
  balanceAfter: number;
  direction: string;
  description: string;
  occurredAt: string;
  entryType: "welcome" | "deposit" | "pix-sent" | "pix-received" | string;
  transferId?: string | null;
  counterpartyUserId?: string | null;
};

export type Transfer = {
  transferId: string;
  idempotencyKey: string;
  senderUserId: string;
  senderAccountId: string;
  senderBankId?: string;
  recipientUserId: string;
  recipientAccountId: string;
  recipientBankId?: string;
  amount: number;
  status: string;
  sagaState?: string;
  currentStep?: string;
  compensationState?: string;
  failureCode?: string | null;
  failureReason?: string | null;
  version?: number;
  simulationMode?: "normal" | "credit_rejected" | "credit_timeout" | string;
  createdAt: string;
  updatedAt: string;
  deadlineAt?: string;
  completedAt?: string | null;
  compensationStartedAt?: string | null;
  compensatedAt?: string | null;
};

export type SagaSimulationMode = "normal" | "credit_rejected" | "credit_timeout";

export type EventPayload = Record<string, unknown>;

export type TimelineEvent = {
  eventId: string;
  eventType: string;
  producer: string;
  transferId?: string | null;
  correlationId: string;
  occurredAt: string;
  payload: EventPayload;
};

export type FlowStep = {
  stepId: string;
  transferId?: string | null;
  eventType: string;
  stage: string;
  title: string;
  detail: string;
  recordedAt: string;
  sourceEventId: string;
  producer: string;
  correlationId: string;
  causationId?: string | null;
  outcome: "pending" | "success" | "failure" | "info" | string;
};

export type DepositInputs = Record<string, string>;
