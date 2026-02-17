using Tindarr.Domain.Common;

namespace Tindarr.Application.Abstractions.Persistence;

public interface IRadarrPendingAddRepository
{
	/// <summary>
	/// Returns the next scheduled ReadyAtUtc timestamp for any pending (not canceled, not processed)
	/// record, or null if no pending records exist.
	/// </summary>
	Task<DateTimeOffset?> GetNextReadyAtUtcAsync(CancellationToken cancellationToken);

	Task<bool> TryEnqueueAsync(
		ServiceScope scope,
		string userId,
		int tmdbId,
		DateTimeOffset readyAtUtc,
		CancellationToken cancellationToken);

	Task<bool> TryCancelAsync(
		ServiceScope scope,
		string userId,
		int tmdbId,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<RadarrPendingAddRecord>> ListDueAsync(
		DateTimeOffset nowUtc,
		int limit,
		CancellationToken cancellationToken);

	Task MarkProcessedAsync(long id, DateTimeOffset processedAtUtc, CancellationToken cancellationToken);

	Task RescheduleAsync(long id, DateTimeOffset nextReadyAtUtc, string? lastError, CancellationToken cancellationToken);
}

public sealed record RadarrPendingAddRecord(
	long Id,
	ServiceType ServiceType,
	string ServerId,
	string UserId,
	int TmdbId,
	DateTimeOffset ReadyAtUtc,
	int AttemptCount,
	string? LastError);
