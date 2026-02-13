using System.Collections.Concurrent;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Domain.Rooms;

namespace Tindarr.Infrastructure.Rooms;

public sealed class InMemoryRoomStore : IRoomStore
{
	private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);
	private readonly ConcurrentDictionary<string, RoomState> _rooms = new(StringComparer.Ordinal);

	public Task CreateAsync(RoomState state, CancellationToken cancellationToken)
	{
		_rooms[state.RoomId] = state;
		return Task.CompletedTask;
	}

	public Task<RoomState?> GetAsync(string roomId, CancellationToken cancellationToken)
	{
		if (!_rooms.TryGetValue(roomId, out var state))
		{
			return Task.FromResult<RoomState?>(null);
		}

		if (IsExpired(state))
		{
			_rooms.TryRemove(roomId, out _);
			return Task.FromResult<RoomState?>(null);
		}

		return Task.FromResult<RoomState?>(state);
	}

	public Task UpdateAsync(RoomState state, CancellationToken cancellationToken)
	{
		_rooms[state.RoomId] = state;
		return Task.CompletedTask;
	}

	public Task CleanupExpiredAsync(CancellationToken cancellationToken)
	{
		foreach (var kvp in _rooms)
		{
			if (IsExpired(kvp.Value))
			{
				_rooms.TryRemove(kvp.Key, out _);
			}
		}

		return Task.CompletedTask;
	}

	private static bool IsExpired(RoomState state)
	{
		var now = DateTimeOffset.UtcNow;
		return now - state.LastActivityAtUtc > Ttl;
	}
}
