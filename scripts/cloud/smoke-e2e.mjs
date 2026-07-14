#!/usr/bin/env node

const apiBaseUrl = process.env.API_BASE_URL;
if (!apiBaseUrl) {
  throw new Error("Set API_BASE_URL to the APIM API URL.");
}

const apiBase = apiBaseUrl.replace(/\/$/, "");
const runId = process.env.GITHUB_RUN_ID ?? Date.now().toString();
const terminalStates = new Set(["completed", "compensated", "failed", "manual_intervention"]);

async function api(path, options = {}) {
  const headers = new Headers(options.headers);
  if (options.body && !headers.has("content-type")) {
    headers.set("content-type", "application/json");
  }

  const response = await fetch(`${apiBase}${path}`, { ...options, headers });
  const text = await response.text();
  let body = null;
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  }

  if (!response.ok) {
    throw new Error(`${options.method ?? "GET"} ${path} failed with ${response.status}: ${text}`);
  }

  return body;
}

async function verifyBrowserPreflight() {
  const origin = "https://realtime-pix-web.vercel.app";
  const response = await fetch(`${apiBase}/sessions/anonymous`, {
    method: "OPTIONS",
    headers: {
      Origin: origin,
      "Access-Control-Request-Method": "POST",
      "Access-Control-Request-Headers": "content-type"
    }
  });
  const allowedMethods = response.headers.get("access-control-allow-methods") ?? "";
  if (
    !response.ok ||
    response.headers.get("access-control-allow-origin") !== origin ||
    !allowedMethods.split(",").map((method) => method.trim()).includes("POST")
  ) {
    throw new Error(`APIM browser preflight failed with ${response.status}: ${await response.text()}`);
  }
}

async function waitForTransfer(transferId) {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    const transfer = await api(`/pix/transfers/${encodeURIComponent(transferId)}`);
    if (terminalStates.has(transfer.sagaState)) {
      return transfer;
    }

    await delay(2000);
  }

  throw new Error(`Transfer ${transferId} did not reach a terminal Saga state.`);
}

async function waitForTransferFlow(transferId, expectedEventTypes) {
  for (let attempt = 0; attempt < 45; attempt += 1) {
    const flow = await api(`/events/transfers/${encodeURIComponent(transferId)}/flow`);
    const eventTypes = new Set(flow.map((item) => item.eventType));
    if (expectedEventTypes.every((eventType) => eventTypes.has(eventType))) {
      assertUnique(flow.map((item) => item.sourceEventId), `flow source events for ${transferId}`);
      return flow;
    }

    await delay(2000);
  }

  const flow = await api(`/events/transfers/${encodeURIComponent(transferId)}/flow`);
  const eventTypes = new Set(flow.map((item) => item.eventType));
  const missing = expectedEventTypes.filter((eventType) => !eventTypes.has(eventType));
  throw new Error(`Transfer flow ${transferId} is missing ${missing.join(", ")}.`);
}

async function waitForTimelineEvent(transferId) {
  for (let attempt = 0; attempt < 45; attempt += 1) {
    const timeline = await api("/events/timeline");
    const transferEvents = timeline.filter((item) => item.transferId === transferId);
    if (transferEvents.length > 0) {
      assertUnique(transferEvents.map((item) => item.eventId), `timeline events for ${transferId}`);
      return transferEvents;
    }

    await delay(2000);
  }

  throw new Error(`Public timeline did not include transfer ${transferId}.`);
}

async function createUser(role) {
  const clientId = `smoke-${role}-${runId}-${Math.random().toString(16).slice(2)}`;
  const session = await api("/sessions/anonymous", {
    method: "POST",
    body: JSON.stringify({ clientId })
  });
  await api(`/wallet/users/${encodeURIComponent(session.userId)}/bootstrap`, { method: "POST" });
  return session;
}

async function getAccounts(userId) {
  return api(`/wallet/accounts?userId=${encodeURIComponent(userId)}`);
}

function findAccount(accounts, userId, bankId) {
  const account = accounts.find((item) => item.userId === userId && item.bankId === bankId);
  if (!account) {
    throw new Error(`${bankId} account was not returned for ${userId}.`);
  }

  return account;
}

async function getHistory(account) {
  return api(
    `/wallet/accounts/${encodeURIComponent(account.accountId)}/transactions` +
      `?userId=${encodeURIComponent(account.userId)}&bankId=${encodeURIComponent(account.bankId)}`
  );
}

async function totalBalances(userIds) {
  const accountSets = await Promise.all(userIds.map(getAccounts));
  return accountSets.flat().reduce((total, account) => total + Number(account.balance), 0);
}

async function verifyTransitions(transferId, expectedStates) {
  const transitions = await api(`/pix/transfers/${encodeURIComponent(transferId)}/transitions`);
  assertUnique(transitions.map((item) => item.transitionId), `Saga transitions for ${transferId}`);
  const states = transitions.map((item) => item.nextState);
  if (JSON.stringify(states) !== JSON.stringify(expectedStates)) {
    throw new Error(`Unexpected Saga transitions for ${transferId}: ${states.join(" -> ")}.`);
  }

  transitions.forEach((transition, index) => {
    if (transition.nextVersion !== index + 1) {
      throw new Error(`Non-contiguous Saga version for ${transferId}: ${transition.nextVersion}.`);
    }
  });
  return transitions;
}

async function runScenario({
  name,
  sender,
  recipient,
  amount,
  simulationMode,
  expectedState,
  expectedFailureCode,
  expectedTransitions,
  expectedFlowEvents,
  replay = false
}) {
  const senderAccount = findAccount(await getAccounts(sender.userId), sender.userId, "bank-a");
  const recipientAccount = findAccount(await getAccounts(recipient.userId), recipient.userId, "bank-b");
  const beforeTotal = await totalBalances([sender.userId, recipient.userId]);
  const request = {
    idempotencyKey: `smoke-${name}-${runId}`,
    senderUserId: sender.userId,
    senderAccountId: senderAccount.accountId,
    senderBankId: senderAccount.bankId,
    recipientUserId: recipient.userId,
    recipientAccountId: recipientAccount.accountId,
    recipientBankId: recipientAccount.bankId,
    amount,
    simulationMode
  };
  const first = await api("/pix/transfers", { method: "POST", body: JSON.stringify(request) });
  if (replay) {
    const duplicate = await api("/pix/transfers", { method: "POST", body: JSON.stringify(request) });
    if (duplicate.transferId !== first.transferId) {
      throw new Error(`${name}: idempotent replay returned a different transfer id.`);
    }
  }

  const final = await waitForTransfer(first.transferId);
  if (final.sagaState !== expectedState || (expectedFailureCode && final.failureCode !== expectedFailureCode)) {
    throw new Error(
      `${name}: expected ${expectedState}/${expectedFailureCode ?? "no failure"}, got ` +
        `${final.sagaState}/${final.failureCode ?? "no failure"}.`
    );
  }

  await verifyTransitions(final.transferId, expectedTransitions);
  const flow = await waitForTransferFlow(final.transferId, expectedFlowEvents);
  const timeline = await waitForTimelineEvent(final.transferId);
  const afterTotal = await totalBalances([sender.userId, recipient.userId]);
  const unresolvedAmount = expectedState === "manual_intervention" ? amount : 0;
  assertMoneyEqual(afterTotal + unresolvedAmount, beforeTotal, `${name}: fictional money conservation`);

  const senderHistory = await getHistory(senderAccount);
  const recipientHistory = await getHistory(recipientAccount);
  const senderOperations = senderHistory.filter((item) => item.transferId === final.transferId);
  const recipientOperations = recipientHistory.filter((item) => item.transferId === final.transferId);
  assertUnique(senderOperations.map((item) => `${item.transferId}:${item.entryType}`), `${name} sender operations`);
  assertUnique(recipientOperations.map((item) => `${item.transferId}:${item.entryType}`), `${name} recipient operations`);
  const expectedOperationTypes = {
    completed: { sender: ["pix-debit"], recipient: ["pix-credit"] },
    compensated: { sender: ["pix-debit", "pix-refund"], recipient: [] },
    failed: { sender: [], recipient: [] },
    manual_intervention: { sender: ["pix-debit"], recipient: [] }
  }[expectedState];
  assertOperationTypes(senderOperations, expectedOperationTypes.sender, `${name} sender ledger`);
  assertOperationTypes(recipientOperations, expectedOperationTypes.recipient, `${name} recipient ledger`);

  return {
    name,
    transferId: final.transferId,
    sagaState: final.sagaState,
    transitions: expectedTransitions.length,
    flowSteps: flow.length,
    timelineEvents: timeline.length,
    unresolvedAmount
  };
}

function assertUnique(values, label) {
  const filtered = values.filter(Boolean);
  if (new Set(filtered).size !== filtered.length) {
    throw new Error(`Duplicate ${label} were returned.`);
  }
}

function assertMoneyEqual(actual, expected, label) {
  if (Math.abs(actual - expected) > 0.001) {
    throw new Error(`${label}: expected ${expected}, got ${actual}.`);
  }
}

function assertOperationTypes(operations, expectedTypes, label) {
  const actualTypes = operations.map((item) => item.entryType).sort();
  const sortedExpected = [...expectedTypes].sort();
  if (JSON.stringify(actualTypes) !== JSON.stringify(sortedExpected)) {
    throw new Error(`${label}: expected ${sortedExpected.join(", ")}, got ${actualTypes.join(", ")}.`);
  }
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

await verifyBrowserPreflight();
await api("/health");
await api("/health/ready");
await api("/presence/users");

const sender = await createUser("sender");
const recipient = await createUser("recipient");
const senderAccount = findAccount(await getAccounts(sender.userId), sender.userId, "bank-a");
await api(`/wallet/accounts/${encodeURIComponent(senderAccount.accountId)}/deposit?bankId=bank-a`, {
  method: "POST",
  body: JSON.stringify({ userId: sender.userId, amount: 125, reason: "Cloud Saga smoke deposit" })
});

const results = [];
results.push(await runScenario({
  name: "normal",
  sender,
  recipient,
  amount: 25,
  simulationMode: "normal",
  expectedState: "completed",
  expectedTransitions: ["debit_pending", "credit_pending", "completed"],
  expectedFlowEvents: ["FundsDebited.v1", "FundsCredited.v1", "PixTransferCompleted.v2"],
  replay: true
}));
results.push(await runScenario({
  name: "debit-rejected",
  sender,
  recipient,
  amount: 1000000,
  simulationMode: "normal",
  expectedState: "failed",
  expectedFailureCode: "debit_rejected",
  expectedTransitions: ["debit_pending", "failed"],
  expectedFlowEvents: ["FundsDebitRejected.v1", "PixTransferFailed.v2"]
}));
results.push(await runScenario({
  name: "credit-rejected",
  sender,
  recipient,
  amount: 7,
  simulationMode: "credit_rejected",
  expectedState: "compensated",
  expectedFailureCode: "credit_rejected",
  expectedTransitions: ["debit_pending", "credit_pending", "compensation_pending", "compensated"],
  expectedFlowEvents: ["FundsDebited.v1", "FundsCreditRejected.v1", "FundsRefunded.v1", "PixTransferCompensated.v1"]
}));
results.push(await runScenario({
  name: "credit-timeout",
  sender,
  recipient,
  amount: 6,
  simulationMode: "credit_timeout",
  expectedState: "compensated",
  expectedFailureCode: "credit_timeout",
  expectedTransitions: ["debit_pending", "credit_pending", "compensation_pending", "compensated"],
  expectedFlowEvents: ["FundsDebited.v1", "PixSagaTimedOut.v1", "FundsRefunded.v1", "PixTransferCompensated.v1"]
}));
results.push(await runScenario({
  name: "manual-intervention",
  sender,
  recipient,
  amount: 5,
  simulationMode: "refund_rejected_test",
  expectedState: "manual_intervention",
  expectedFailureCode: "refund_rejected",
  expectedTransitions: ["debit_pending", "credit_pending", "compensation_pending", "manual_intervention"],
  expectedFlowEvents: ["FundsCreditRejected.v1", "FundsRefundRejected.v1", "PixTransferFailed.v2"]
}));

console.log(JSON.stringify({ status: "ok", senderUserId: sender.userId, recipientUserId: recipient.userId, results }));
