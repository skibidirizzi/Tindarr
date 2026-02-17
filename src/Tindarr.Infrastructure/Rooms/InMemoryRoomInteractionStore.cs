using System.Collections.Concurrent;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Rooms;

public sealed class InMemoryRoomInteractionStore(IRoomLifetimeProvider lifetimes) : IRoomInteractionStore
{
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

		return ListOrExpireAsync(roomId, bucket, limit, cancellationToken);
	}

	private async Task<IReadOnlyList<Interaction>> ListOrExpireAsync(string roomId, RoomInteractionBucket bucket, int limit, CancellationToken cancellationToken)
	{
		if (await IsExpiredAsync(bucket, cancellationToken).ConfigureAwait(false))
		{
			_buckets.TryRemove(roomId, out _);
			return Array.Empty<Interaction>();
		}

		lock (bucket.Gate)
		{
			bucket.LastActivityAtUtc = DateTimeOffset.UtcNow;
			var result = bucket.Items
				.OrderByDescending(x => x.CreatedAtUtc)
				.Take(Math.Clamp(limit, 1, 50_000))
				.ToList();
			return result;
		}
	}

	public Task CleanupExpiredAsync(CancellationToken cancellationToken)
	{
		return CleanupExpiredInnerAsync(cancellationToken);
	}

	private async Task CleanupExpiredInnerAsync(CancellationToken cancellationToken)
	{
		foreach (var kvp in _buckets)
		{
			if (await IsExpiredAsync(kvp.Value, cancellationToken).ConfigureAwait(false))
			{
				_buckets.TryRemove(kvp.Key, out _);
			}
		}
	}

	private async Task<bool> IsExpiredAsync(RoomInteractionBucket bucket, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var ttl = await lifetimes.GetRoomTtlAsync(cancellationToken).ConfigureAwait(false);
		return now - bucket.LastActivityAtUtc > ttl;
	}

	private sealed class RoomInteractionBucket
	{
		public object Gate { get; } = new();
		public DateTimeOffset LastActivityAtUtc { get; set; } = DateTimeOffset.UtcNow;
		public List<Interaction> Items { get; } = new();
	}
}
