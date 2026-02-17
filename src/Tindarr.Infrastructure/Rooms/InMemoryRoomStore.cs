using System.Collections.Concurrent;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Domain.Rooms;

namespace Tindarr.Infrastructure.Rooms;

public sealed class InMemoryRoomStore(IRoomLifetimeProvider lifetimes) : IRoomStore
{
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

		return GetOrExpireAsync(roomId, state, cancellationToken);
	}

	private async Task<RoomState?> GetOrExpireAsync(string roomId, RoomState state, CancellationToken cancellationToken)
	{
		if (await IsExpiredAsync(state, cancellationToken).ConfigureAwait(false))
		{
			_rooms.TryRemove(roomId, out _);
			return null;
		}

		return state;
	}

	public Task UpdateAsync(RoomState state, CancellationToken cancellationToken)
	{
		_rooms[state.RoomId] = state;
		return Task.CompletedTask;
	}

	public Task CleanupExpiredAsync(CancellationToken cancellationToken)
	{
		return CleanupExpiredInnerAsync(cancellationToken);
	}

	private async Task CleanupExpiredInnerAsync(CancellationToken cancellationToken)
	{
		foreach (var kvp in _rooms)
		{
			if (await IsExpiredAsync(kvp.Value, cancellationToken).ConfigureAwait(false))
			{
				_rooms.TryRemove(kvp.Key, out _);
			}
		}
	}

	private async Task<bool> IsExpiredAsync(RoomState state, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var ttl = await lifetimes.GetRoomTtlAsync(cancellationToken).ConfigureAwait(false);
		return now - state.LastActivityAtUtc > ttl;
	}
}
