using System.Collections.Concurrent;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Rooms;

public sealed class InMemoryRoomInteractionStore : IRoomInteractionStore
{
	private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);
	private readonly ConcurrentDictionary<string, RoomInteractionBucket> _buckets = new(StringComparer.Ordinal);

	public Task AddAsync(string roomId, Interaction interaction, CancellationToken cancellationToken)
	{
		var bucket = _buckets.GetOrAdd(roomId, _ => new RoomInteractionBucket());
		lock (bucket.Gate)
		{
			bucket.LastActivityAtUtc = DateTimeOffset.UtcNow;
			bucket.Items.Add(interaction);
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Interaction>> ListAsync(string roomId, int limit, CancellationToken cancellationToken)
	{
		if (!_buckets.TryGetValue(roomId, out var bucket))
		{
			return Task.FromResult<IReadOnlyList<Interaction>>(Array.Empty<Interaction>());
		}

		if (IsExpired(bucket))
		{
			_buckets.TryRemove(roomId, out _);
			return Task.FromResult<IReadOnlyList<Interaction>>(Array.Empty<Interaction>());
		}

		lock (bucket.Gate)
		{
			bucket.LastActivityAtUtc = DateTimeOffset.UtcNow;
			var result = bucket.Items
				.OrderByDescending(x => x.CreatedAtUtc)
				.Take(Math.Clamp(limit, 1, 50_000))
				.ToList();
			return Task.FromResult<IReadOnlyList<Interaction>>(result);
		}
	}

	public Task CleanupExpiredAsync(CancellationToken cancellationToken)
	{
		foreach (var kvp in _buckets)
		{
			if (IsExpired(kvp.Value))
			{
				_buckets.TryRemove(kvp.Key, out _);
			}
		}

		return Task.CompletedTask;
	}

	private static bool IsExpired(RoomInteractionBucket bucket)
	{
		var now = DateTimeOffset.UtcNow;
		return now - bucket.LastActivityAtUtc > Ttl;
	}

	private sealed class RoomInteractionBucket
	{
		public object Gate { get; } = new();
		public DateTimeOffset LastActivityAtUtc { get; set; } = DateTimeOffset.UtcNow;
		public List<Interaction> Items { get; } = new();
	}
}
