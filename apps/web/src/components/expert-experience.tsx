"use client";

import {
  Activity,
  ArrowRightLeft,
  Bot,
  CircleUserRound,
  Database,
  Filter,
  Landmark,
  Plus,
  RefreshCcw,
  Send,
  Server,
  Users
} from "lucide-react";
import { useMemo, useState } from "react";
import type { RealtimePixPlatform } from "@/hooks/use-realtime-pix-platform";
import { eventOutcome, money, time } from "@/lib/presentation";
import type { TimelineEvent } from "@/lib/types";
import { PersonalHistory } from "./activity-views";
import { TransactionMap } from "./transaction-map";

export function ExpertExperience({ platform }: { platform: RealtimePixPlatform }) {
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null);
  const [eventTypeFilter, setEventTypeFilter] = useState("operational");
  const [producerFilter, setProducerFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState("all");
  const [transferFilter, setTransferFilter] = useState("");
  const [timeFilter, setTimeFilter] = useState("all");

  const eventTypes = useMemo(
    () => [...new Set(platform.timeline.map((event) => event.eventType))].sort(),
    [platform.timeline]
  );
  const producers = useMemo(
    () => [...new Set(platform.timeline.map((event) => event.producer))].sort(),
    [platform.timeline]
  );

  const filteredEvents = useMemo(() => {
    const now = Date.now();
    const minutes = timeFilter === "all" ? null : Number(timeFilter);
    return platform.timeline.filter((event) => {
      if (
        eventTypeFilter === "operational" &&
        event.eventType === "ArchitectureFlowStepRecorded.v1"
      ) {
        return false;
      }
      if (
        eventTypeFilter !== "all" &&
        eventTypeFilter !== "operational" &&
        event.eventType !== eventTypeFilter
      ) {
        return false;
      }
      if (producerFilter !== "all" && event.producer !== producerFilter) return false;
      if (statusFilter !== "all" && eventOutcome(event.eventType) !== statusFilter) return false;
      if (
        transferFilter &&
        !event.transferId?.toLowerCase().includes(transferFilter.trim().toLowerCase())
      ) {
        return false;
      }
      if (minutes && now - new Date(event.occurredAt).getTime() > minutes * 60_000) return false;
      return true;
    });
  }, [
    eventTypeFilter,
    platform.timeline,
    producerFilter,
    statusFilter,
    timeFilter,
    transferFilter
  ]);

  const selectedEvent =
    filteredEvents.find((event) => event.eventId === selectedEventId) ?? filteredEvents[0] ?? null;

  return (
    <main className="expertMain">
      <section className="operationsBar">
        <div>
          <span>Session</span>
          <strong>{platform.session?.userId ?? "joining"}</strong>
        </div>
        <div>
          <span>Presence transport</span>
          <strong>{platform.connectionState}</strong>
        </div>
        <div>
          <span>Active users</span>
          <strong>{platform.users.length}</strong>
        </div>
        <div>
          <span>Current transfer</span>
          <strong>{platform.transfer?.status ?? "none"}</strong>
        </div>
        <button onClick={() => void platform.refresh()} title="Refresh projections" type="button">
          <RefreshCcw size={17} />
        </button>
      </section>

      <section className="expertWorkspace">
        <div className="workspaceSection accountsWorkspace">
          <div className="workspaceHeading">
            <Landmark size={18} />
            <div>
              <h2>Service-owned accounts</h2>
              <p>Select the sender account and issue manual demo deposits.</p>
            </div>
          </div>
          <div className="expertAccountList">
            {platform.accounts.map((account) => (
              <article
                className={platform.selectedAccountId === account.accountId ? "selected" : ""}
                key={account.accountId}
              >
                <button onClick={() => void platform.selectAccount(account.accountId)} type="button">
                  <span>
                    <Database size={17} />
                    {account.bankName}
                  </span>
                  <strong>{money(account.balance)}</strong>
                  <small>{account.accountId}</small>
                </button>
                <div className="depositRow">
                  <input
                    aria-label={`Deposit into ${account.bankName}`}
                    inputMode="decimal"
                    onChange={(event) =>
                      platform.setDepositInputs((current) => ({
                        ...current,
                        [account.accountId]: event.target.value
                      }))
                    }
                    placeholder="Deposit amount"
                    value={platform.depositInputs[account.accountId] ?? ""}
                  />
                  <button
                    onClick={() => void platform.deposit(account.accountId)}
                    title={`Deposit into ${account.bankName}`}
                    type="button"
                  >
                    <Plus size={17} />
                  </button>
                </div>
              </article>
            ))}
          </div>
        </div>

        <form
          className="workspaceSection expertTransferForm"
          onSubmit={(event) => {
            event.preventDefault();
            void platform.sendTransfer();
          }}
        >
          <div className="workspaceHeading">
            <ArrowRightLeft size={18} />
            <div>
              <h2>Emit PIX transfer</h2>
              <p>Configure the full command payload.</p>
            </div>
          </div>
          <div className="fieldGrid">
            <label>
              Sender account
              <select
                onChange={(event) => void platform.selectAccount(event.target.value)}
                value={platform.selectedAccountId}
              >
                {platform.accounts.map((account) => (
                  <option key={account.accountId} value={account.accountId}>
                    {account.bankName} - {money(account.balance)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Recipient
              <select
                onChange={(event) => void platform.selectRecipient(event.target.value)}
                value={platform.selectedRecipientId}
              >
                <option value="">Select active user</option>
                {platform.recipients.map((user) => (
                  <option key={user.userId} value={user.userId}>
                    {user.displayName} {user.isBot ? "(bot)" : ""}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Recipient account ID
              <input
                onChange={(event) => platform.setSelectedRecipientAccountId(event.target.value)}
                value={platform.selectedRecipientAccountId}
              />
            </label>
            <label>
              Amount
              <input
                inputMode="decimal"
                onChange={(event) => platform.setAmount(event.target.value)}
                value={platform.amount}
              />
            </label>
          </div>
          <button
            className="expertSendButton"
            disabled={platform.isSending || !platform.selectedRecipientId}
            type="submit"
          >
            <Send size={17} />
            {platform.isSending ? "Publishing command..." : "Publish transfer command"}
          </button>
        </form>

        <div className="workspaceSection presenceWorkspace">
          <div className="workspaceHeading">
            <Users size={18} />
            <div>
              <h2>Presence projection</h2>
              <p>SignalR-connected identities and persistent demo bots.</p>
            </div>
          </div>
          <div className="expertPresenceList">
            {platform.recipients.map((user) => (
              <button
                className={platform.selectedRecipientId === user.userId ? "selected" : ""}
                key={user.userId}
                onClick={() => void platform.selectRecipient(user.userId)}
                type="button"
              >
                {user.isBot ? <Bot size={16} /> : <CircleUserRound size={16} />}
                <span>
                  <strong>{user.displayName}</strong>
                  <small>{user.userId}</small>
                </span>
                <em>{user.isBot ? "bot" : "live"}</em>
              </button>
            ))}
          </div>
        </div>
      </section>

      <TransactionMap
        expertMode
        flow={platform.flow}
        onReplay={platform.replay}
        replayKey={platform.replayKey}
        transfer={platform.transfer}
      />

      <section className="expertDataGrid">
        <div className="eventExplorer">
          <div className="dataSectionHeading">
            <div>
              <span className="sectionKicker">Integration stream</span>
              <h2>Event inspector</h2>
            </div>
            <Filter size={18} />
          </div>
          <div className="eventFilters">
            <select onChange={(event) => setEventTypeFilter(event.target.value)} value={eventTypeFilter}>
              <option value="operational">Operational events</option>
              <option value="all">All raw events</option>
              {eventTypes.map((eventType) => (
                <option key={eventType} value={eventType}>{eventType}</option>
              ))}
            </select>
            <select onChange={(event) => setProducerFilter(event.target.value)} value={producerFilter}>
              <option value="all">All producers</option>
              {producers.map((producer) => (
                <option key={producer} value={producer}>{producer}</option>
              ))}
            </select>
            <select onChange={(event) => setStatusFilter(event.target.value)} value={statusFilter}>
              <option value="all">All outcomes</option>
              <option value="pending">Pending</option>
              <option value="success">Success</option>
              <option value="failure">Failure</option>
              <option value="info">Informational</option>
            </select>
            <select onChange={(event) => setTimeFilter(event.target.value)} value={timeFilter}>
              <option value="all">All captured time</option>
              <option value="5">Last 5 minutes</option>
              <option value="15">Last 15 minutes</option>
              <option value="60">Last hour</option>
            </select>
            <input
              onChange={(event) => setTransferFilter(event.target.value)}
              placeholder="Filter transfer ID"
              value={transferFilter}
            />
          </div>
          <div className="eventInspectorBody">
            <div className="eventList" role="list">
              {filteredEvents.slice(0, 80).map((event) => (
                <EventRow
                  event={event}
                  key={event.eventId}
                  onSelect={() => setSelectedEventId(event.eventId)}
                  selected={selectedEvent?.eventId === event.eventId}
                />
              ))}
            </div>
            <EventDetail event={selectedEvent} />
          </div>
        </div>

        <div className="ledgerExplorer">
          <div className="dataSectionHeading">
            <div>
              <span className="sectionKicker">Wallet read model</span>
              <h2>Structured ledger</h2>
            </div>
            <Activity size={18} />
          </div>
          <PersonalHistory
            entries={platform.entries}
            expertMode
            onSelectTransfer={(transferId) => void platform.loadTransferJourney(transferId)}
            users={platform.users}
          />
        </div>
      </section>
    </main>
  );
}

function EventRow({
  event,
  selected,
  onSelect
}: {
  event: TimelineEvent;
  selected: boolean;
  onSelect: () => void;
}) {
  const outcome = eventOutcome(event.eventType);
  return (
    <button className={selected ? "selected" : ""} onClick={onSelect} role="listitem" type="button">
      <span className={`eventOutcome ${outcome}`} />
      <span>
        <strong>{event.eventType}</strong>
        <small>{event.producer}</small>
      </span>
      <time>{time(event.occurredAt)}</time>
    </button>
  );
}

function EventDetail({ event }: { event: TimelineEvent | null }) {
  if (!event) {
    return <div className="emptyState">No event matches the current filters.</div>;
  }

  return (
    <aside className="eventDetail">
      <div className="eventDetailTitle">
        <Server size={17} />
        <strong>{event.eventType}</strong>
      </div>
      <dl>
        <div><dt>Event ID</dt><dd>{event.eventId}</dd></div>
        <div><dt>Producer</dt><dd>{event.producer}</dd></div>
        <div><dt>Correlation ID</dt><dd>{event.correlationId}</dd></div>
        <div><dt>Transfer ID</dt><dd>{event.transferId ?? "n/a"}</dd></div>
        <div><dt>Occurred</dt><dd>{new Date(event.occurredAt).toISOString()}</dd></div>
      </dl>
      <pre>{JSON.stringify(event.payload, null, 2)}</pre>
    </aside>
  );
}
