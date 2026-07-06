"use client";

import { Activity, CircleUserRound, RadioTower } from "lucide-react";
import type { RealtimePixPlatform } from "@/hooks/use-realtime-pix-platform";
import { money } from "@/lib/presentation";

type AppHeaderProps = {
  expertMode: boolean;
  onExpertModeChange: (nextValue: boolean) => void;
  platform: RealtimePixPlatform;
};

export function AppHeader({ expertMode, onExpertModeChange, platform }: AppHeaderProps) {
  const connected = platform.connectionState === "connected";

  return (
    <header className="appHeader">
      <div className="brandLockup">
        <span className="brandMark" aria-hidden="true">
          <Activity size={20} />
        </span>
        <div>
          <strong>PIX Journey</strong>
          <span>{expertMode ? "Architecture workspace" : "Send it. See how it moves."}</span>
        </div>
      </div>

      <div className="headerIdentity">
        <div className="identityDetail">
          <CircleUserRound size={18} />
          <span>
            <small>You are</small>
            <strong>{platform.loading ? "Joining..." : platform.session?.displayName ?? "Visitor"}</strong>
          </span>
        </div>
        <div className="identityDetail balanceIdentity">
          <span>
            <small>Available</small>
            <strong>{money(platform.primaryAccount?.balance ?? 0)}</strong>
          </span>
        </div>
        <span className={`connectionSignal ${connected ? "online" : ""}`}>
          <RadioTower size={15} />
          {connected ? "Live" : platform.connectionState}
        </span>
        <label className="modeSwitch">
          <span>Expert mode</span>
          <button
            aria-checked={expertMode}
            aria-label="Expert mode"
            className="switchTrack"
            onClick={() => onExpertModeChange(!expertMode)}
            role="switch"
            type="button"
          >
            <span className="switchThumb" />
          </button>
        </label>
      </div>
    </header>
  );
}
