using Microsoft.Extensions.Caching.Memory;
using Tindarr.Contracts.Admin;

namespace Tindarr.Infrastructure.Casting;

/// <summary>
/// Stores active casting sessions with TTL = content runtime + 120 seconds.
/// Broadcasts events for admin diagnostics.
/// </summary>
public sealed class CastingSessionStore(IMemoryCache cache)
{
	private const string SessionKeyPrefix = "casting:session:";
	private const string SessionIndexKey = "casting:sessions:index";
	private const string EventLogKey = "casting:events";
	private long _eventIdCounter = 0;
	private readonly object _lock = new object();

	public event EventHandler<CastingEventArgs>? SessionStarted;
	public event EventHandler<CastingEventArgs>? SessionEnded;
	public event EventHandler<CastingEventArgs>? SessionError;

	/// <summary>
	/// Creates or updates a casting session and sets expiration to content runtime + 120 seconds.
	/// </summary>
	public void RegisterSession(
		string sessionId,
		string deviceId,
		string contentTitle,
		string contentSubtitle,
		string contentType,
		int contentRuntimeSeconds)
	{
		// If a device starts a new cast, consider the previous session ended.
		// This keeps diagnostics accurate even when the receiver never calls back (e.g. user stops early).
		EndOtherSessionsForDevice(deviceId, exceptSessionId: sessionId);

		var expiresAt = DateTime.UtcNow.AddSeconds(contentRuntimeSeconds + 120);

		var session = new CastingSessionDto(
			sessionId,
			deviceId,
			contentTitle,
			contentSubtitle,
			"active",
			contentType,
			DateTime.UtcNow,
			expiresAt,
			contentRuntimeSeconds);

		var ttl = expiresAt - DateTime.UtcNow;
		var key = $"{SessionKeyPrefix}{sessionId}";
		cache.Set(key, session, new MemoryCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = ttl
		});

		AddToSessionIndex(sessionId);

		LogEvent(new CastingEventDto(
			EventId: GetNextEventId(),
			OccurredAtUtc: DateTime.UtcNow,
			EventType: "session_started",
			Message: $"Casting session started on device {deviceId}",
			DeviceId: deviceId,
			ErrorDetails: null));

		SessionStarted?.Invoke(this, new CastingEventArgs(sessionId, deviceId));
	}

	private void EndOtherSessionsForDevice(string deviceId, string exceptSessionId)
	{
		if (string.IsNullOrWhiteSpace(deviceId))
		{
			return;
		}

		HashSet<string> sessionIds;
		lock (_lock)
		{
			sessionIds = LoadSessionIndexNoLock();
		}

		foreach (var id in sessionIds)
		{
			if (string.Equals(id, exceptSessionId, StringComparison.Ordinal))
			{
				continue;
			}

			var key = $"{SessionKeyPrefix}{id}";
			if (cache.TryGetValue(key, out CastingSessionDto? session)
				&& session is not null
				&& string.Equals(session.DeviceId, deviceId, StringComparison.Ordinal))
			{
				EndSession(id);
			}
		}
	}

	/// <summary>
	/// Ends a casting session.
	/// </summary>
	public void EndSession(string sessionId)
	{
		var key = $"{SessionKeyPrefix}{sessionId}";
		if (cache.TryGetValue(key, out CastingSessionDto? session) && session is not null)
		{
			cache.Remove(key);
			RemoveFromSessionIndex(sessionId);

			LogEvent(new CastingEventDto(
				EventId: GetNextEventId(),
				OccurredAtUtc: DateTime.UtcNow,
				EventType: "session_ended",
				Message: $"Casting session ended",
				DeviceId: session.DeviceId,
				ErrorDetails: null));

			SessionEnded?.Invoke(this, new CastingEventArgs(sessionId, session.DeviceId));
		}
		else
		{
			// Best-effort cleanup when the session already expired.
			RemoveFromSessionIndex(sessionId);
		}
	}

	/// <summary>
	/// Logs an error event for a casting session.
	/// </summary>
	public void LogError(string sessionId, string deviceId, string message, Exception? ex = null)
	{
		LogEvent(new CastingEventDto(
			EventId: GetNextEventId(),
			OccurredAtUtc: DateTime.UtcNow,
			EventType: "session_error",
			Message: message,
			DeviceId: deviceId,
			ErrorDetails: ex?.ToString()));

		SessionError?.Invoke(this, new CastingEventArgs(sessionId, deviceId));
	}

	/// <summary>
	/// Gets all active casting sessions.
	/// </summary>
	public List<CastingSessionDto> GetActiveSessions()
	{
		HashSet<string> sessionIds;
		lock (_lock)
		{
			sessionIds = LoadSessionIndexNoLock();
		}

		var sessions = new List<CastingSessionDto>(sessionIds.Count);
		var stale = new List<string>();
		foreach (var id in sessionIds)
		{
			var key = $"{SessionKeyPrefix}{id}";
			if (cache.TryGetValue(key, out CastingSessionDto? s) && s is not null)
			{
				sessions.Add(s);
			}
			else
			{
				stale.Add(id);
			}
		}

		if (stale.Count > 0)
		{
			lock (_lock)
			{
				var updated = LoadSessionIndexNoLock();
				foreach (var id in stale)
				{
					updated.Remove(id);
				}
				SaveSessionIndexNoLock(updated);
			}
		}

		return sessions;
	}

	/// <summary>
	/// Gets the recent casting event log (latest 100 events).
	/// </summary>
	public List<CastingEventDto> GetRecentEvents(int count = 100)
	{
		if (!cache.TryGetValue(EventLogKey, out List<CastingEventDto>? events))
		{
			return [];
		}

		return events?.TakeLast(count).ToList() ?? [];
	}

	/// <summary>
	/// Gets the complete diagnostics snapshot.
	/// </summary>
	public CastingDiagnosticsDto GetDiagnostics()
	{
		var sessions = GetActiveSessions();
		var events = GetRecentEvents(100);
		return new CastingDiagnosticsDto(sessions, events);
	}

	private void LogEvent(CastingEventDto evt)
	{
		lock (_lock)
		{
			if (!cache.TryGetValue(EventLogKey, out List<CastingEventDto>? events))
			{
				events = [];
			}
			else
			{
				events = new List<CastingEventDto>(events ?? []);
			}

			events.Add(evt);

			// Keep only the last 200 events in memory.
			if (events.Count > 200)
			{
				events = events.TakeLast(200).ToList();
			}

			cache.Set(EventLogKey, events, new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
			});
		}
	}

	private void AddToSessionIndex(string sessionId)
	{
		lock (_lock)
		{
			var ids = LoadSessionIndexNoLock();
			if (ids.Add(sessionId))
			{
				SaveSessionIndexNoLock(ids);
			}
			else
			{
				// Refresh TTL even when it's already present.
				SaveSessionIndexNoLock(ids);
			}
		}
	}

	private void RemoveFromSessionIndex(string sessionId)
	{
		lock (_lock)
		{
			var ids = LoadSessionIndexNoLock();
			if (ids.Remove(sessionId))
			{
				SaveSessionIndexNoLock(ids);
			}
		}
	}

	private HashSet<string> LoadSessionIndexNoLock()
	{
		if (!cache.TryGetValue(SessionIndexKey, out HashSet<string>? ids) || ids is null)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		// Copy so we never mutate the cached instance outside the lock.
		return new HashSet<string>(ids, StringComparer.Ordinal);
	}

	private void SaveSessionIndexNoLock(HashSet<string> ids)
	{
		cache.Set(SessionIndexKey, ids, new MemoryCacheEntryOptions
		{
			SlidingExpiration = TimeSpan.FromDays(7)
		});
	}

	private long GetNextEventId()
	{
		lock (_lock)
		{
			return ++_eventIdCounter;
		}
	}
}

public sealed class CastingEventArgs(string sessionId, string deviceId) : EventArgs
{
	public string SessionId { get; } = sessionId;
	public string DeviceId { get; } = deviceId;
}
