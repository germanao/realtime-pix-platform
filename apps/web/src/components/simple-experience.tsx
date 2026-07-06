"use client";

import { ArrowRight, Bot, CircleUserRound, Send, Sparkles, Users } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import type { RealtimePixPlatform } from "@/hooks/use-realtime-pix-platform";
import { money, moneyParts } from "@/lib/presentation";
import { CommunityActivity, PersonalHistory } from "./activity-views";
import { TransactionMap } from "./transaction-map";

type AmountPickerProps = {
  amount: string;
  choice: "25" | "50" | "custom";
  disabled?: boolean;
  onAmountChange: (value: string) => void;
  onChoiceChange: (choice: "25" | "50" | "custom") => void;
};

export function AmountPicker({
  amount,
  choice,
  disabled,
  onAmountChange,
  onChoiceChange
}: AmountPickerProps) {
  return (
    <div className="amountPicker">
      <div className="amountOptions" aria-label="Choose an amount">
        {(["25", "50", "custom"] as const).map((option) => (
          <button
            aria-pressed={choice === option}
            disabled={disabled}
            key={option}
            onClick={() => onChoiceChange(option)}
            type="button"
          >
            {option === "custom" ? "Custom" : money(Number(option))}
          </button>
        ))}
      </div>
      <AnimatePresence initial={false}>
        {choice === "custom" && (
          <motion.label
            animate={{ height: "auto", opacity: 1 }}
            className="customAmount"
            exit={{ height: 0, opacity: 0 }}
            initial={{ height: 0, opacity: 0 }}
          >
            <span>How much?</span>
            <span className="currencyInput">
              <b>$</b>
              <input
                aria-label="Custom amount"
                disabled={disabled}
                inputMode="decimal"
                onChange={(event) => onAmountChange(event.target.value)}
                placeholder="0,00"
                value={amount}
              />
            </span>
          </motion.label>
        )}
      </AnimatePresence>
    </div>
  );
}

export function SimpleExperience({ platform }: { platform: RealtimePixPlatform }) {
  const balance = moneyParts(platform.primaryAccount?.balance ?? 0);
  const canSend =
    Boolean(platform.selectedRecipientId) &&
    Boolean(platform.selectedRecipientAccountId) &&
    !platform.isSending;

  return (
    <>
      <main className="simpleMain">
        <section className="sendStudio">
          <div className="studioIntro">
            <span className="sectionKicker">Your fictional balance</span>
            <h1 aria-label={money(platform.primaryAccount?.balance ?? 0)} className="heroBalance">
              <span className="heroCurrency">{balance.symbol}</span>
              <span className="heroAmount">{balance.amount}</span>
            </h1>
            <p>Pick someone, choose a value, and watch every part of the transfer come alive.</p>
            <span className="welcomeNote">
              <Sparkles size={15} />
              $10,000.00 is added once when your identity is created.
            </span>
          </div>

          <form
            className="sendComposer"
            onSubmit={(event) => {
              event.preventDefault();
              void platform.sendTransfer();
            }}
          >
            <div className="composerSection">
              <div className="composerHeading">
                <span>1</span>
                <div>
                  <strong>Who receives it?</strong>
                  <small>People online now</small>
                </div>
              </div>
              <div className="recipientRail">
                {platform.recipients.map((user) => (
                  <button
                    aria-pressed={platform.selectedRecipientId === user.userId}
                    className="recipientChoice"
                    key={user.userId}
                    onClick={() => void platform.selectRecipient(user.userId)}
                    type="button"
                  >
                    <span className="recipientAvatar">
                      {user.isBot ? <Bot size={20} /> : <CircleUserRound size={20} />}
                    </span>
                    <strong>{user.displayName}</strong>
                    <small>{user.isBot ? "Always available" : "Online now"}</small>
                  </button>
                ))}
                {!platform.recipients.length && (
                  <div className="inlineEmpty">
                    <Users size={20} />
                    Waiting for someone to come online
                  </div>
                )}
              </div>
            </div>

            <div className="composerSection">
              <div className="composerHeading">
                <span>2</span>
                <div>
                  <strong>How much?</strong>
                  <small>Choose a quick value or enter your own</small>
                </div>
              </div>
              <AmountPicker
                amount={platform.amount}
                choice={platform.amountChoice}
                disabled={platform.isSending}
                onAmountChange={platform.setAmount}
                onChoiceChange={platform.chooseAmount}
              />
            </div>

            <button className="sendButton" disabled={!canSend} type="submit">
              <Send size={19} />
              {platform.isSending ? "Sending..." : `Send ${money(Number(platform.amount.replace(",", ".")) || 0)}`}
              <ArrowRight size={18} />
            </button>
            <p className="sendSummary">
              {platform.selectedRecipient
                ? `Ready for ${platform.selectedRecipient.displayName}`
                : "Choose a person to continue"}
            </p>
          </form>
        </section>

        <TransactionMap
          expertMode={false}
          flow={platform.flow}
          onReplay={platform.replay}
          replayKey={platform.replayKey}
          transfer={platform.transfer}
        />

        <section className="activityBand">
          <div className="activityColumn">
            <div className="sectionHeading">
              <span className="sectionKicker">Your money</span>
              <h2>Activity journal</h2>
              <p>Choose a PIX entry to revisit its journey.</p>
            </div>
            <PersonalHistory
              entries={platform.entries}
              onSelectTransfer={(transferId) => void platform.loadTransferJourney(transferId)}
              users={platform.users}
            />
          </div>
          <div className="activityColumn communityColumn">
            <div className="sectionHeading">
              <span className="sectionKicker">Around you</span>
              <h2>Live room activity</h2>
              <p>A friendly view of what is happening across the simulation.</p>
            </div>
            <CommunityActivity timeline={platform.timeline} users={platform.users} />
          </div>
        </section>
      </main>
    </>
  );
}
