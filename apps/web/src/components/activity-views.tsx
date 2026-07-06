"use client";

import { ArrowDownLeft, ArrowUpRight, Clock3, Gift, Radio } from "lucide-react";
import { motion } from "motion/react";
import { describeCommunityEvent, describeLedgerEntry, money, time } from "@/lib/presentation";
import type { LedgerEntry, PresenceUser, TimelineEvent } from "@/lib/types";

type PersonalHistoryProps = {
  entries: LedgerEntry[];
  users: PresenceUser[];
  onSelectTransfer: (transferId: string) => void;
  expertMode?: boolean;
};

function dayLabel(value: string) {
  const date = new Date(value);
  const today = new Date();
  if (date.toDateString() === today.toDateString()) {
    return "Today";
  }
  return new Intl.DateTimeFormat("en", { month: "long", day: "numeric" }).format(date);
}

export function PersonalHistory({
  entries,
  users,
  onSelectTransfer,
  expertMode = false
}: PersonalHistoryProps) {
  const grouped = entries.reduce<Record<string, LedgerEntry[]>>((result, entry) => {
    const key = dayLabel(entry.occurredAt);
    result[key] ??= [];
    result[key].push(entry);
    return result;
  }, {});

  if (!entries.length) {
    return <div className="emptyState">Your money activity will appear here.</div>;
  }

  return (
    <div className="journal">
      {Object.entries(grouped).map(([label, dayEntries]) => (
        <section className="journalDay" key={label}>
          <h3>{label}</h3>
          {dayEntries.map((entry) => {
            const description = describeLedgerEntry(entry, users);
            const Icon =
              entry.entryType === "welcome"
                ? Gift
                : entry.direction === "debit"
                  ? ArrowUpRight
                  : ArrowDownLeft;
            const interactive = Boolean(entry.transferId);

            return (
              <button
                className="journalEntry"
                disabled={!interactive}
                key={entry.ledgerEntryId}
                onClick={() => entry.transferId && onSelectTransfer(entry.transferId)}
                type="button"
              >
                <span className={`journalIcon ${description.tone}`}>
                  <Icon size={18} />
                </span>
                <span className="journalCopy">
                  <strong>{description.title}</strong>
                  <small>
                    {expertMode && entry.transferId ? entry.transferId : description.detail}
                  </small>
                </span>
                <span className={`journalAmount ${description.tone}`}>
                  <strong>{entry.amount > 0 ? "+" : ""}{money(entry.amount)}</strong>
                  <small>{time(entry.occurredAt)}</small>
                </span>
              </button>
            );
          })}
        </section>
      ))}
    </div>
  );
}

type CommunityActivityProps = {
  timeline: TimelineEvent[];
  users: PresenceUser[];
};

export function CommunityActivity({ timeline, users }: CommunityActivityProps) {
  const activity = timeline
    .map((event) => describeCommunityEvent(event, users))
    .filter((event) => event !== null)
    .slice(0, 12);

  if (!activity.length) {
    return <div className="emptyState">The room is quiet for the moment.</div>;
  }

  return (
    <div className="communityStream">
      {activity.map((item) => (
        <motion.div
          animate={{ opacity: 1, y: 0 }}
          className="communityItem"
          initial={{ opacity: 0, y: 8 }}
          key={item.id}
        >
          <span className={`communityPulse ${item.tone}`}>
            <Radio size={14} />
          </span>
          <div>
            <strong>{item.title}</strong>
            <small>{item.detail}</small>
          </div>
          <span className="communityTime">
            <Clock3 size={13} />
            {time(item.occurredAt)}
          </span>
        </motion.div>
      ))}
    </div>
  );
}
