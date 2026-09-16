"use client";

import { useState } from "react";
import { AppHeader } from "@/components/app-header";
import { ExpertExperience } from "@/components/expert-experience";
import { SimpleExperience } from "@/components/simple-experience";
import { useRealtimePixPlatform } from "@/hooks/use-realtime-pix-platform";

export default function Home() {
  const [expertMode, setExpertMode] = useState(false);
  const platform = useRealtimePixPlatform();

  return (
    <div className={expertMode ? "appShell expertShell" : "appShell"}>
      <AppHeader
        expertMode={expertMode}
        onExpertModeChange={setExpertMode}
        platform={platform}
      />
      {platform.startupStatus && (
        <div className="globalNotice" role="status" aria-live="polite">
          <span>{platform.startupStatus} {platform.loading && "Sleeping services may take a few minutes. This page will continue automatically."}</span>
          {!platform.loading && <button onClick={platform.retryJoin} type="button">Retry startup</button>}
        </div>
      )}
      {platform.error && (
        <div className="globalNotice" role="alert">
          <span>{platform.error}</span>
          <button onClick={() => platform.setError(null)} type="button">Dismiss</button>
        </div>
      )}
      {expertMode ? (
        <ExpertExperience platform={platform} />
      ) : (
        <SimpleExperience platform={platform} />
      )}
    </div>
  );
}
