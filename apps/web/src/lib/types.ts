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
  recipientUserId: string;
  recipientAccountId: string;
  amount: number;
  status: string;
  failureReason?: string | null;
  createdAt: string;
  updatedAt: string;
};

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
