import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getServiceScope, setServiceScopeAndNotify, type ServiceScope } from "../serviceScope";
import { fetchConfiguredScopes } from "../api/client";
import type { ServiceScopeOptionDto } from "../api/contracts";

type ServiceKey = "tmdb" | "plex" | "jellyfin" | "emby";

function scopeForService(key: ServiceKey): ServiceScope {
  if (key === "tmdb") {
    return { serviceType: "tmdb", serverId: "tmdb" };
  }

  // For now we keep a single default server scope per service.
  return { serviceType: key, serverId: "default" };
}

export default function ServiceSelectPage() {
  const navigate = useNavigate();
  const current = useMemo(() => getServiceScope(), []);
  const [availableScopes, setAvailableScopes] = useState<ServiceScopeOptionDto[]>([]);

  useEffect(() => {
    fetchConfiguredScopes()
      .then(setAvailableScopes)
      .catch(() => setAvailableScopes([]));
  }, []);

  const currentLabel = useMemo(() => {
    const match = availableScopes.find(
      (s) =>
        s.serviceType.toLowerCase() === current.serviceType.toLowerCase()
        && s.serverId.toLowerCase() === current.serverId.toLowerCase()
    );

    return match?.displayName ?? current.serviceType;
  }, [availableScopes, current.serverId, current.serviceType]);

  function selectService(key: ServiceKey) {
    setServiceScopeAndNotify(scopeForService(key));
    navigate("/swipe", { replace: true });
  }

  return (
    <section className="deck">
      <div className="deck__state" style={{ textAlign: "left" }}>
        <h2 style={{ marginTop: 0, marginBottom: "0.25rem" }}>What would you like to do?</h2>
        <div className="field__label">Current scope</div>
        <div style={{ marginTop: "0.25rem" }}>
          {currentLabel}
        </div>
      </div>


      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Swipe Items in Media Library</h3>
        <div style={{ display: "grid", gap: "0.75rem", gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))" }}>
          <button type="button" className="button" onClick={() => selectService("plex")}
          >
            Plex
          </button>
          <button type="button" className="button" onClick={() => selectService("jellyfin")}
          >
            Jellyfin
          </button>
          <button type="button" className="button" onClick={() => selectService("emby")}
          >
            Emby
          </button>
        </div>
      </div>

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Discover (TMDB)</h3>
        <button type="button" className="button button--like" onClick={() => selectService("tmdb")}
        >
          Discover
        </button>
      </div>
    </section>
  );
}
