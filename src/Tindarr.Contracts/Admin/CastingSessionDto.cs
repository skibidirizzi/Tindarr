namespace Tindarr.Contracts.Admin;

/// <summary>
/// Represents an active or recently-active casting session.
/// </summary>
public sealed record CastingSessionDto(
	string SessionId,
	string DeviceId,
	string ContentTitle,
	string ContentSubtitle,
	string SessionState,
	string ContentType,
	DateTime StartedAtUtc,
	DateTime ExpiresAtUtc,
	int ContentRuntimeSeconds);

/// <summary>
/// A single casting event in the diagnostic log.
/// </summary>
public sealed record CastingEventDto(
	long EventId,
	DateTime OccurredAtUtc,
	string EventType,
	string Message,
	string? DeviceId,
	string? ErrorDetails);

/// <summary>
/// Casting diagnostics state and the recent event log.
/// </summary>
public sealed record CastingDiagnosticsDto(
	IReadOnlyList<CastingSessionDto> ActiveSessions,
	IReadOnlyList<CastingEventDto> RecentEvents);
