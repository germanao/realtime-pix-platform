"use client";

import * as signalR from "@microsoft/signalr";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, eventsHubUrl, presenceHubUrl, sendPresenceLeave } from "@/lib/api";
import { sortRecipients, uniqueBy, validateTransferAmount } from "@/lib/presentation";
import type {
  Account,
  DepositInputs,
  FlowStep,
  LedgerEntry,
  PresenceUser,
  Session,
  TimelineEvent,
  Transfer,
  WalletBootstrap
} from "@/lib/types";

export function useRealtimePixPlatform() {
  const [session, setSession] = useState<Session | null>(null);
  const [users, setUsers] = useState<PresenceUser[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [entries, setEntries] = useState<LedgerEntry[]>([]);
  const [selectedAccountId, setSelectedAccountId] = useState("");
  const [selectedRecipientId, setSelectedRecipientId] = useState("");
  const [selectedRecipientAccountId, setSelectedRecipientAccountId] = useState("");
  const [amount, setAmount] = useState("25");
  const [amountChoice, setAmountChoice] = useState<"25" | "50" | "custom">("25");
  const [depositInputs, setDepositInputs] = useState<DepositInputs>({});
  const [timeline, setTimeline] = useState<TimelineEvent[]>([]);
  const [transfer, setTransfer] = useState<Transfer | null>(null);
  const [flow, setFlow] = useState<FlowStep[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [isSending, setIsSending] = useState(false);
  const [connectionState, setConnectionState] = useState("connecting");
  const [replayKey, setReplayKey] = useState(0);

  const presenceConnectionRef = useRef<signalR.HubConnection | null>(null);
  const eventsConnectionRef = useRef<signalR.HubConnection | null>(null);
  const sessionRef = useRef<Session | null>(null);
  const selectedAccountIdRef = useRef("");
  const transferIdRef = useRef<string | null>(null);
  const isSendingRef = useRef(false);
  const pendingTransferKeyRef = useRef<string | null>(null);

  const recipients = useMemo(
    () => sortRecipients(users, session?.userId),
    [session?.userId, users]
  );
  const primaryAccount = accounts.find((account) => account.bankName === "Bank A") ?? accounts[0] ?? null;
  const selectedAccount =
    accounts.find((account) => account.accountId === selectedAccountId) ?? primaryAccount;
  const selectedRecipient = users.find((user) => user.userId === selectedRecipientId) ?? null;

  useEffect(() => {
    sessionRef.current = session;
  }, [session]);

  useEffect(() => {
    selectedAccountIdRef.current = selectedAccountId;
  }, [selectedAccountId]);

  useEffect(() => {
    transferIdRef.current = transfer?.transferId ?? null;
  }, [transfer?.transferId]);

  const loadEntries = useCallback(async (userId: string, accountId: string) => {
    if (!accountId) {
      setEntries([]);
      return;
    }
    const nextEntries = await api<LedgerEntry[]>(
      `/wallet/accounts/${encodeURIComponent(accountId)}/transactions?userId=${encodeURIComponent(userId)}`
    );
    setEntries(nextEntries);
  }, []);

  const loadAccounts = useCallback(
    async (userId: string, preferredAccountId?: string) => {
      const loaded = await api<Account[]>(`/wallet/accounts?userId=${encodeURIComponent(userId)}`);
      setAccounts(loaded);
      localStorage.setItem("realtime-pix:lastAccounts", JSON.stringify(loaded));
      const accountId =
        preferredAccountId && loaded.some((account) => account.accountId === preferredAccountId)
          ? preferredAccountId
          : loaded.find((account) => account.bankName === "Bank A")?.accountId ?? loaded[0]?.accountId ?? "";
      setSelectedAccountId(accountId);
      selectedAccountIdRef.current = accountId;
      await loadEntries(userId, accountId);
      return loaded;
    },
    [loadEntries]
  );

  const bootstrapWallet = useCallback(
    async (nextSession: Session) => {
      await api<WalletBootstrap>(`/wallet/users/${encodeURIComponent(nextSession.userId)}/bootstrap`, {
        method: "POST"
      });
      await loadAccounts(nextSession.userId, selectedAccountIdRef.current);
    },
    [loadAccounts]
  );

  const refresh = useCallback(async () => {
    const currentSession = sessionRef.current;
    if (!currentSession) {
      return;
    }

    const [nextUsers, nextTimeline] = await Promise.all([
      api<PresenceUser[]>("/presence/users"),
      api<TimelineEvent[]>("/events/timeline")
    ]);
    setUsers(uniqueBy(nextUsers, (user) => user.userId));
    setTimeline(uniqueBy(nextTimeline, (item) => item.eventId));
    await loadAccounts(currentSession.userId, selectedAccountIdRef.current);

    if (transferIdRef.current) {
      const [nextTransfer, nextFlow] = await Promise.all([
        api<Transfer>(`/pix/transfers/${transferIdRef.current}`),
        api<FlowStep[]>(`/events/transfers/${transferIdRef.current}/flow`)
      ]);
      setTransfer(nextTransfer);
      setFlow(uniqueBy(nextFlow, (step) => step.sourceEventId || step.stepId));
    }
  }, [loadAccounts]);

  useEffect(() => {
    let cancelled = false;
    let pageHideHandler: (() => void) | null = null;

    const acceptSession = async (nextSession: Session) => {
      if (cancelled) {
        return;
      }
      localStorage.setItem("realtime-pix:clientId", nextSession.clientId);
      sessionRef.current = nextSession;
      setSession(nextSession);
      await bootstrapWallet(nextSession);
    };

    async function join() {
      const clientId = localStorage.getItem("realtime-pix:clientId") ?? crypto.randomUUID();
      const cachedAccounts = localStorage.getItem("realtime-pix:lastAccounts");
      if (cachedAccounts) {
        try {
          setAccounts(JSON.parse(cachedAccounts) as Account[]);
        } catch {
          localStorage.removeItem("realtime-pix:lastAccounts");
        }
      }

      try {
        const connection = new signalR.HubConnectionBuilder()
          .withUrl(presenceHubUrl)
          .withAutomaticReconnect()
          .build();

        presenceConnectionRef.current = connection;
        connection.on("presence.snapshot", (snapshot: PresenceUser[]) => {
          setUsers(uniqueBy(snapshot, (user) => user.userId));
        });
        connection.onreconnecting(() => setConnectionState("reconnecting"));
        connection.onreconnected(async () => {
          setConnectionState("connected");
          const nextSession = await connection.invoke<Session>("Join", { clientId });
          await acceptSession(nextSession);
        });
        connection.onclose(() => setConnectionState("disconnected"));

        await connection.start();
        setConnectionState("connected");
        const nextSession = await connection.invoke<Session>("Join", { clientId });
        await acceptSession(nextSession);

        pageHideHandler = () => {
          const current = sessionRef.current;
          if (current) {
            sendPresenceLeave(current.userId, connection.connectionId);
          }
        };
        window.addEventListener("pagehide", pageHideHandler);
      } catch (joinError) {
        try {
          const nextSession = await api<Session>("/sessions/anonymous", {
            method: "POST",
            body: JSON.stringify({ clientId })
          });
          await acceptSession(nextSession);
          setConnectionState("http fallback");
        } catch {
          setError(joinError instanceof Error ? joinError.message : "Could not join the room.");
          setConnectionState("disconnected");
        }
      } finally {
        setLoading(false);
      }
    }

    void join();
    return () => {
      cancelled = true;
      if (pageHideHandler) {
        window.removeEventListener("pagehide", pageHideHandler);
      }
      const current = sessionRef.current;
      const connection = presenceConnectionRef.current;
      if (current && connection) {
        void connection
          .invoke("Leave", { userId: current.userId, connectionId: connection.connectionId })
          .catch(() => undefined);
      }
      void connection?.stop().catch(() => undefined);
    };
  }, [bootstrapWallet]);

  useEffect(() => {
    let cancelled = false;

    async function connectEvents() {
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(eventsHubUrl)
        .withAutomaticReconnect()
        .build();

      eventsConnectionRef.current = connection;
      connection.on("events.timelineSnapshot", (snapshot: TimelineEvent[]) => {
        setTimeline(uniqueBy(snapshot, (item) => item.eventId));
      });
      connection.on("events.timelineItem", (item: TimelineEvent) => {
        setTimeline((current) =>
          uniqueBy([item, ...current], (event) => event.eventId).slice(0, 250)
        );

        if (item.transferId === transferIdRef.current) {
          if (item.eventType === "PixTransferCompleted.v1" || item.eventType === "PixTransferFailed.v1") {
            void api<Transfer>(`/pix/transfers/${item.transferId}`)
              .then(setTransfer)
              .catch((syncError: unknown) =>
                setError(syncError instanceof Error ? syncError.message : "Transfer sync failed.")
              );
          }
        }

        const currentUserId = sessionRef.current?.userId;
        const payloadUserIds = [
          item.payload?.senderUserId,
          item.payload?.recipientUserId,
          item.payload?.userId
        ];
        if (currentUserId && payloadUserIds.includes(currentUserId)) {
          void loadAccounts(currentUserId, selectedAccountIdRef.current).catch(() => undefined);
        }
      });
      connection.on("events.transferFlowStep", (step: FlowStep) => {
        if (step.transferId === transferIdRef.current) {
          setFlow((current) =>
            uniqueBy([...current, step], (item) => item.sourceEventId || item.stepId)
          );
        }
      });
      connection.on("events.transferFlowSnapshot", (snapshot: FlowStep[]) => {
        setFlow(uniqueBy(snapshot, (step) => step.sourceEventId || step.stepId));
      });

      try {
        await connection.start();
        if (cancelled) {
          await connection.stop();
        }
      } catch (eventError) {
        setError(eventError instanceof Error ? eventError.message : "Live updates are unavailable.");
      }
    }

    void connectEvents();
    return () => {
      cancelled = true;
      void eventsConnectionRef.current?.stop().catch(() => undefined);
    };
  }, [loadAccounts]);

  useEffect(() => {
    const interval = window.setInterval(() => {
      void refresh().catch((refreshError: unknown) => {
        setError(refreshError instanceof Error ? refreshError.message : "Refresh failed.");
      });
    }, 15000);
    return () => window.clearInterval(interval);
  }, [refresh]);

  const selectAccount = useCallback(
    async (accountId: string) => {
      setSelectedAccountId(accountId);
      selectedAccountIdRef.current = accountId;
      if (sessionRef.current) {
        await loadEntries(sessionRef.current.userId, accountId);
      }
    },
    [loadEntries]
  );

  const selectRecipient = useCallback(async (userId: string) => {
    setSelectedRecipientId(userId);
    if (!userId) {
      setSelectedRecipientAccountId("");
      return;
    }
    const recipientAccounts = await api<Account[]>(
      `/wallet/accounts?userId=${encodeURIComponent(userId)}`
    );
    const primary =
      recipientAccounts.find((account) => account.bankName === "Bank A") ?? recipientAccounts[0];
    setSelectedRecipientAccountId(primary?.accountId ?? "");
  }, []);

  const chooseAmount = useCallback((choice: "25" | "50" | "custom") => {
    setAmountChoice(choice);
    if (choice !== "custom") {
      setAmount(choice);
    }
  }, []);

  const sendTransfer = useCallback(async () => {
    if (isSendingRef.current) {
      return;
    }

    const currentSession = sessionRef.current;
    const senderAccount =
      accounts.find((account) => account.accountId === selectedAccountIdRef.current) ??
      accounts.find((account) => account.bankName === "Bank A");
    if (!currentSession || !senderAccount || !selectedRecipientId || !selectedRecipientAccountId) {
      setError("Choose someone to receive your PIX.");
      return;
    }

    const validationError = validateTransferAmount(amount, senderAccount.balance);
    if (validationError) {
      setError(validationError);
      return;
    }

    isSendingRef.current = true;
    setIsSending(true);
    setError(null);
    pendingTransferKeyRef.current ??= crypto.randomUUID();

    try {
      const nextTransfer = await api<Transfer>("/pix/transfers", {
        method: "POST",
        body: JSON.stringify({
          idempotencyKey: pendingTransferKeyRef.current,
          senderUserId: currentSession.userId,
          senderAccountId: senderAccount.accountId,
          recipientUserId: selectedRecipientId,
          recipientAccountId: selectedRecipientAccountId,
          amount: Number(amount.replace(",", "."))
        })
      });

      transferIdRef.current = nextTransfer.transferId;
      setTransfer(nextTransfer);
      setFlow([]);
      setReplayKey((current) => current + 1);
      await eventsConnectionRef.current
        ?.invoke("SubscribeTransfer", nextTransfer.transferId)
        .catch(() => undefined);
      await refresh();
    } finally {
      pendingTransferKeyRef.current = null;
      isSendingRef.current = false;
      setIsSending(false);
    }
  }, [accounts, amount, refresh, selectedRecipientAccountId, selectedRecipientId]);

  const deposit = useCallback(
    async (accountId: string) => {
      const currentSession = sessionRef.current;
      if (!currentSession) {
        return;
      }
      const depositAmount = Number((depositInputs[accountId] || "0").replace(",", "."));
      if (!Number.isFinite(depositAmount) || depositAmount <= 0) {
        setError("Deposit amount must be positive.");
        return;
      }

      await api(`/wallet/accounts/${encodeURIComponent(accountId)}/deposit`, {
        method: "POST",
        body: JSON.stringify({
          userId: currentSession.userId,
          amount: depositAmount,
          reason: "Manual demo deposit"
        })
      });
      setDepositInputs((current) => ({ ...current, [accountId]: "" }));
      await loadAccounts(currentSession.userId, accountId);
    },
    [depositInputs, loadAccounts]
  );

  const loadTransferJourney = useCallback(async (transferId: string) => {
    const [nextTransfer, nextFlow] = await Promise.all([
      api<Transfer>(`/pix/transfers/${transferId}`),
      api<FlowStep[]>(`/events/transfers/${transferId}/flow`)
    ]);
    transferIdRef.current = transferId;
    setTransfer(nextTransfer);
    setFlow(uniqueBy(nextFlow, (step) => step.sourceEventId || step.stepId));
    setReplayKey((current) => current + 1);
    await eventsConnectionRef.current?.invoke("SubscribeTransfer", transferId).catch(() => undefined);
  }, []);

  return {
    session,
    users,
    recipients,
    accounts,
    entries,
    primaryAccount,
    selectedAccount,
    selectedAccountId,
    selectedRecipient,
    selectedRecipientId,
    selectedRecipientAccountId,
    amount,
    amountChoice,
    depositInputs,
    timeline,
    transfer,
    flow,
    error,
    loading,
    isSending,
    connectionState,
    replayKey,
    setAmount,
    setSelectedRecipientAccountId,
    setDepositInputs,
    setError,
    selectAccount,
    selectRecipient,
    chooseAmount,
    sendTransfer,
    deposit,
    refresh,
    loadTransferJourney,
    replay: () => setReplayKey((current) => current + 1)
  };
}

export type RealtimePixPlatform = ReturnType<typeof useRealtimePixPlatform>;
