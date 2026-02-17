
# Playback Gateway

This document describes how Tindarr proxies and secures media playback so that:

- Clients never talk directly to Plex/Jellyfin/Emby.
- Cast devices only ever fetch media from Tindarr URLs.
- Provider credentials never appear in URLs (only in upstream request headers).

See also: `docs/architecture/invariants.md`.

## Endpoints

### Prepare playback (Issue #62)

`POST /api/v1/playback/movie/prepare` (authorized)

Body: `PrepareMoviePlaybackRequest` (`serviceType`, `serverId`, `tmdbId`).

Returns: `PreparePlaybackResponse`

- `ContentUrl`: a fully-qualified URL to the gateway stream endpoint.
- `ContentType`: currently `video/mp4` (cast-friendly default).
- `ExpiresAtUnixSeconds`: token expiry timestamp.

URLs are generated via `IBaseUrlResolver` (server-owned LAN/WAN base URL selection), never from client-provided base URLs.

### Stream movie

`GET /api/v1/playback/movie/{serviceType}/{serverId}/{tmdbId}?token=...`

- Validates playback token.
- Asks the provider (`IPlaybackProvider`) for an upstream request.
- Proxies the upstream response.
- If the upstream response is an HLS playlist, the playlist is rewritten so all referenced resources point back to the gateway.

### Proxy HLS sub-resources (Issue #64)

`GET /api/v1/playback/proxy/movie/{serviceType}/{serverId}/{tmdbId}?token=...&p=...`

`p` is a Base64Url-encoded string containing the upstream *path + query* (must start with `/`).

Security properties:

- Prevents open proxying by forcing requests to the configured upstream origin for the selected service scope.
- Scrubs a small set of known secret-ish query keys when generating the proxy pointer.

## Token design (Issue #63)

Playback tokens are intentionally *not* JWTs. They are short-lived, URL-safe tokens suitable for cast devices.

Format:

`base64url(payloadJson).base64url(hmacSha256(payloadB64, keyMaterial))`

Payload fields:

- `kid`: signing key id
- `svc`: service type (plex/jellyfin/emby)
- `sid`: server id
- `tmdb`: TMDB id
- `exp`: expiry unix seconds

Validation:

- Checks scope + tmdb match.
- Checks expiry.
- Verifies signature using key(s) from `ITokenSigningKeyStore`.

### Rotation plan

Signing keys are persisted in the `jwt-signing-keys.json` file (same store used by auth JWT signing). Rotation is operationally simple:

1. Add a new key entry to the file and switch `ActiveKeyId` to the new key.
2. Keep the old key(s) in the file for a grace period (at least as long as `Playback:TokenMinutes`, and ideally the longest cache TTL in your playback path).
3. After the grace period, remove old key(s).

The playback token validator prefers the key referenced by `kid`, but will fall back to trying all available keys when `kid` is missing or unknown (supporting safe rotation and backwards compatibility).

## HLS rewrite behavior (Issue #64)

When an upstream response is an HLS playlist (`.m3u8`), Tindarr rewrites:

- URI lines (segments, nested playlists)
- `URI="..."` attributes inside tag lines (e.g., `#EXT-X-KEY`)

Only `http`/`https` URIs are rewritten; non-http schemes (e.g., `skd://`) are left unchanged.

Implementation lives in `Tindarr.Infrastructure.Playback.Hls.ManifestRewriter`.
