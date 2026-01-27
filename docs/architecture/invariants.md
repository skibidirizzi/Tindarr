## INVARIANT PRECEDENCE RULE

- Invariant IDs are authoritative.
- If multiple invariants define the same rule or constraint:
  - The invariant with the LOWEST numeric ID takes precedence.
  - Higher-numbered duplicates MUST be ignored.
- No invariant may weaken or contradict a lower-numbered invariant.
- Duplicate invariants MUST NOT be merged at runtime or by tooling.


# docs/architecture/invariants.md

# Tindarr Invariants

## INV-0001 — API surface
- All HTTP endpoints MUST be served under `/api/v1`.
- No endpoint MAY be introduced outside `/api/v1` (except static assets + SPA fallback routing).

## INV-0002 — Service scoping (global)
- All persisted and queried data related to movies, interactions, matches, accepted movies, and library caches MUST be scoped by:
  - `ServiceType` AND
  - `ServerId` (or equivalent stable server identity per provider).
- Cross-scope reads/writes are forbidden unless explicitly requested by an Admin-only endpoint designed for aggregation.

## INV-0003 — Interaction scoping
- Every Like/Nope/Skip/Superlike/Undo MUST include and enforce `(UserId, ServiceType, ServerId, TmdbId)` scoping.
- Interaction history MUST NOT bleed across ServiceType or ServerId.
- “Seen”/“interacted” semantics MUST be computed per scope.

## INV-0004 — Matching scoping
- Consensus matching MUST be computed per `(ServiceType, ServerId)` scope.
- Accepted movies MUST be stored per `(ServiceType, ServerId)` scope.
- Curator force-match MUST apply only within a single scope.

## INV-0005 — Authentication and authorization
- All non-public endpoints MUST require authentication.
- RBAC MUST be enforced via policies:
  - `AdminOnly` for admin endpoints
  - `CuratorOnly` for curator actions (e.g., force-match)
- Passwords MUST be stored as PBKDF2-SHA256 hashes (with per-user salt + configured iterations).
- Raw passwords MUST never be logged or persisted.

## INV-0006 — Token secrecy
- External media server credentials/tokens (Plex/Jellyfin/Emby/Radarr) MUST never be returned to clients.
- Request logging MUST redact:
  - Authorization headers
  - Plex tokens
  - signed playback tokens
  - cookies/session identifiers

## INV-0007 — Playback gateway only
- All playback MUST flow through the Tindarr Playback Gateway.
- Clients MUST never receive a direct Plex/Jellyfin/Emby stream URL, manifest URL, or segment URL.
- Any URL used for casting MUST be a Tindarr URL (gateway endpoint) protected by a signed token.

## INV-0008 — Signed playback tokens
- All playback gateway endpoints MUST require a signed token with TTL.
- Signed playback tokens MUST be:
  - scope-bound (ServiceType + ServerId + MediaId/TmdbId as applicable)
  - time-bound (expiry enforced server-side)
- Token signing keys MUST be centrally managed and consistent across hosts (API + Workers if both issue/validate tokens).
- Token rotation MUST be supported without breaking active short-lived sessions.

## INV-0009 — Streaming proxy behavior
- Proxy MUST be streaming pass-through (no full buffering of large bodies).
- Proxy MUST forward only a safe allowlist of headers.
- Proxy MUST not act as an open proxy:
  - Only provider endpoints derived from configured servers are allowed.
  - No arbitrary upstream host forwarding.

## INV-0010 — HLS rewrite requirements
- HLS playlist rewrite MUST preserve:
  - query strings needed by upstream provider
  - byte-range and `EXT-X-MAP` semantics when present
- All segment and sub-resource URLs in rewritten playlists MUST point back to Tindarr gateway endpoints.

## INV-0011 — Chromecast / receiver constraints
- Cast devices MUST be treated as capability-opaque.
- Sender SDK MUST NOT attempt codec/capability probing beyond:
  - friendlyName, receiverType, modelName (if available).
- Any “device profile” inference MUST be conservative and server-driven.

## INV-0012 — Rooms lifecycle
- Room state MUST be ephemeral and TTL-governed.
- Room cleanup MUST be automated (worker or hosted service).
- Room membership and guest identity MUST not require permanent DB persistence unless explicitly scoped and justified by an ADR.

## INV-0013 — Rooms scoping
- Room interactions and scoring MUST be scoped by:
  - `RoomId`
  - `ServiceType`
  - `ServerId`
- Room mode MUST disable features not compatible with shared selection rules (e.g., superlikes/force-match) unless explicitly permitted by room policy.

## INV-0014 — Provider-agnostic application logic
- Domain and Application layers MUST NOT depend on provider-specific SDKs or models (Plex/Jellyfin/Emby/Radarr).
- Provider-specific concerns MUST live in Infrastructure and map into Contracts/DTOs.

## INV-0015 — Integration boundaries
- Tindarr is the only component that talks to Plex/Jellyfin/Emby/Radarr.
- Browser/mobile clients MUST only talk to Tindarr API.
- Cast devices MUST only load media from Tindarr playback gateway URLs.

## INV-0016 — Configuration ownership
- Base URL (LAN/WAN) resolution MUST be server-configured and validated.
- Client-provided base URLs MUST be ignored or treated as untrusted input.
- Any externally visible URL generation MUST route through `BaseUrlResolver`.

## INV-0017 — Observability
- Every request MUST have a correlation ID.
- Logs MUST be structured and safe (no secret leakage).
- Health/info endpoints MUST NOT expose secrets.

## INV-0018 — Jellyfin/Emby forward-compat constraints
- Playback gateway contracts MUST remain stable when adding Jellyfin/Emby streaming.
- Jellyfin/Emby playback providers MAY be stubs initially, but:
  - their interfaces/DTO shapes MUST match final intended gateway behavior
  - they MUST NOT leak provider URLs or tokens to clients
