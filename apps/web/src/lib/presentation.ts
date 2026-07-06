import type { LedgerEntry, PresenceUser, TimelineEvent } from "./types";

const moneyFormatter = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD"
});

export function money(value: number) {
  return moneyFormatter.format(value);
}

export function moneyParts(value: number) {
  const parts = moneyFormatter.formatToParts(value);
  return {
    symbol: parts.find((part) => part.type === "currency")?.value ?? "$",
    amount: parts
      .filter((part) => part.type !== "currency")
      .map((part) => part.value)
      .join("")
  };
}

export function time(value: string) {
  return new Intl.DateTimeFormat("en", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(new Date(value));
}

export function uniqueBy<T>(items: T[], keySelector: (item: T) => string) {
  const seen = new Set<string>();
  return items.filter((item) => {
    const key = keySelector(item);
    if (seen.has(key)) {
      return false;
    }
    seen.add(key);
    return true;
  });
}

export function sortRecipients(users: PresenceUser[], currentUserId?: string) {
  return users
    .filter((user) => user.userId !== currentUserId && user.isOnline)
    .sort((left, right) => {
      if (left.isBot !== right.isBot) {
        return Number(left.isBot) - Number(right.isBot);
      }
      return left.displayName.localeCompare(right.displayName);
    });
}

export function validateTransferAmount(rawValue: string, availableBalance: number) {
  const normalized = rawValue.trim().replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return "Enter a valid amount with no more than two decimal places.";
  }

  const amount = Number(normalized);
  if (!Number.isFinite(amount) || amount <= 0) {
    return "The amount must be greater than zero.";
  }

  if (amount > availableBalance) {
    return "This amount is higher than your available balance.";
  }

  return null;
}

export function eventOutcome(eventType: string) {
  if (eventType.includes("Failed")) {
    return "failure";
  }
  if (eventType.includes("Requested")) {
    return "pending";
  }
  if (eventType.includes("Succeeded") || eventType.includes("Completed")) {
    return "success";
  }
  return "info";
}

export function describeLedgerEntry(entry: LedgerEntry, users: PresenceUser[]) {
  const counterparty = users.find((user) => user.userId === entry.counterpartyUserId)?.displayName;

  switch (entry.entryType) {
    case "welcome":
      return {
        title: "Welcome balance",
        detail: "Your starting money arrived",
        tone: "positive"
      };
    case "pix-sent":
      return {
        title: counterparty ? `Sent to ${counterparty}` : "PIX sent",
        detail: "Money left your account",
        tone: "negative"
      };
    case "pix-received":
      return {
        title: counterparty ? `Received from ${counterparty}` : "PIX received",
        detail: "Money arrived in your account",
        tone: "positive"
      };
    default:
      return {
        title: "Money added",
        detail: entry.description,
        tone: "positive"
      };
  }
}

export type CommunityActivity = {
  id: string;
  title: string;
  detail: string;
  occurredAt: string;
  tone: "neutral" | "positive" | "negative";
};

function payloadString(event: TimelineEvent, key: string) {
  const value = event.payload?.[key];
  return typeof value === "string" ? value : undefined;
}

function payloadNumber(event: TimelineEvent, key: string) {
  const value = event.payload?.[key];
  return typeof value === "number" ? value : undefined;
}

export function describeCommunityEvent(
  event: TimelineEvent,
  users: PresenceUser[]
): CommunityActivity | null {
  const userName = (userId?: string) =>
    users.find((user) => user.userId === userId)?.displayName ?? "Someone";

  if (event.eventType === "AnonymousUserJoined.v1") {
    const displayName = payloadString(event, "displayName") ?? "A new visitor";
    return {
      id: event.eventId,
      title: `${displayName} joined`,
      detail: "The room has a new participant",
      occurredAt: event.occurredAt,
      tone: "neutral"
    };
  }

  if (event.eventType === "UserPresenceChanged.v1") {
    const isOnline = event.payload?.isOnline === true;
    const isBot = event.payload?.isBot === true;
    if (isBot) {
      return null;
    }
    const displayName = payloadString(event, "displayName") ?? "A visitor";
    return {
      id: event.eventId,
      title: `${displayName} ${isOnline ? "came online" : "left"}`,
      detail: isOnline ? "Ready to receive a PIX" : "No longer available",
      occurredAt: event.occurredAt,
      tone: "neutral"
    };
  }

  if (event.eventType === "PixTransferCompleted.v1") {
    const sender = userName(payloadString(event, "senderUserId"));
    const recipient = userName(payloadString(event, "recipientUserId"));
    const amount = payloadNumber(event, "amount") ?? 0;
    return {
      id: event.eventId,
      title: `${sender} sent ${money(amount)}`,
      detail: `Delivered to ${recipient}`,
      occurredAt: event.occurredAt,
      tone: "positive"
    };
  }

  if (event.eventType === "PixTransferFailed.v1") {
    const sender = userName(payloadString(event, "senderUserId"));
    return {
      id: event.eventId,
      title: `${sender}'s PIX did not arrive`,
      detail: "The transfer was safely stopped",
      occurredAt: event.occurredAt,
      tone: "negative"
    };
  }

  return null;
}
