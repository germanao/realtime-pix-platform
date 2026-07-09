#!/usr/bin/env node

const apiBaseUrl = process.env.API_BASE_URL;
if (!apiBaseUrl) {
  throw new Error("Set API_BASE_URL to the APIM API URL.");
}

const apiBase = apiBaseUrl.replace(/\/$/, "");
const runId = process.env.GITHUB_RUN_ID ?? Date.now().toString();
const clientId = `smoke-${runId}-${Math.random().toString(16).slice(2)}`;

async function api(path, options = {}) {
  const response = await fetch(`${apiBase}${path}`, {
    ...options,
    headers: {
      "content-type": "application/json",
      ...(options.headers ?? {})
    }
  });

  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (!response.ok) {
    throw new Error(`${options.method ?? "GET"} ${path} failed with ${response.status}: ${text}`);
  }

  return body;
}

async function waitForTransfer(transferId) {
  for (let attempt = 0; attempt < 30; attempt += 1) {
    const transfer = await api(`/pix/transfers/${encodeURIComponent(transferId)}`);
    if (transfer.status === "completed" || transfer.status === "failed") {
      return transfer;
    }

    await new Promise((resolve) => setTimeout(resolve, 2000));
  }

  throw new Error(`Transfer ${transferId} did not reach a final state.`);
}

function findBankA(accounts, userId) {
  const account = accounts.find((item) => item.userId === userId && item.bankName === "Bank A");
  if (!account) {
    throw new Error(`Bank A account was not returned for ${userId}.`);
  }

  return account;
}

await api("/health");

const session = await api("/sessions/anonymous", {
  method: "POST",
  body: JSON.stringify({ clientId })
});

await api(`/wallet/users/${encodeURIComponent(session.userId)}/bootstrap`, { method: "POST" });
let senderAccounts = await api(`/wallet/accounts?userId=${encodeURIComponent(session.userId)}`);
const senderAccount = findBankA(senderAccounts, session.userId);

await api(`/wallet/accounts/${encodeURIComponent(senderAccount.accountId)}/deposit`, {
  method: "POST",
  body: JSON.stringify({ userId: session.userId, amount: 125, reason: "Cloud smoke test deposit" })
});

await api("/presence/users");
const recipientUserId = "bot-aurora-ledger";
await api(`/wallet/users/${encodeURIComponent(recipientUserId)}/bootstrap`, { method: "POST" });
const recipientAccounts = await api(`/wallet/accounts?userId=${encodeURIComponent(recipientUserId)}`);
const recipientAccount = findBankA(recipientAccounts, recipientUserId);

const idempotencyKey = `smoke-transfer-${runId}`;
const transferRequest = {
  idempotencyKey,
  senderUserId: session.userId,
  senderAccountId: senderAccount.accountId,
  recipientUserId,
  recipientAccountId: recipientAccount.accountId,
  amount: 25
};

const firstTransfer = await api("/pix/transfers", {
  method: "POST",
  body: JSON.stringify(transferRequest)
});
const secondTransfer = await api("/pix/transfers", {
  method: "POST",
  body: JSON.stringify(transferRequest)
});

if (firstTransfer.transferId !== secondTransfer.transferId) {
  throw new Error("Idempotent transfer retry returned a different transfer id.");
}

const completedTransfer = await waitForTransfer(firstTransfer.transferId);
if (completedTransfer.status !== "completed") {
  throw new Error(`Expected completed transfer, got ${completedTransfer.status}.`);
}

senderAccounts = await api(`/wallet/accounts?userId=${encodeURIComponent(session.userId)}`);
const senderAfter = findBankA(senderAccounts, session.userId);
if (senderAfter.balance < 100) {
  throw new Error(`Sender balance was lower than expected after transfer: ${senderAfter.balance}.`);
}

const timeline = await api("/events/timeline");
if (!timeline.some((item) => item.transferId === completedTransfer.transferId)) {
  throw new Error("Public timeline did not include the completed smoke transfer.");
}

const flow = await api(`/events/transfers/${encodeURIComponent(completedTransfer.transferId)}/flow`);
const eventTypes = new Set(flow.map((item) => item.eventType));
for (const expected of ["PixTransferRequested.v1", "PixDebitSucceeded.v1", "PixCreditSucceeded.v1", "PixTransferCompleted.v1"]) {
  if (!eventTypes.has(expected)) {
    throw new Error(`Transfer flow is missing ${expected}.`);
  }
}

console.log(JSON.stringify({
  status: "ok",
  userId: session.userId,
  transferId: completedTransfer.transferId,
  timelineEvents: timeline.length,
  flowSteps: flow.length
}));
