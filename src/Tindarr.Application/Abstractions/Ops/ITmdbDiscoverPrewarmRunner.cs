namespace Tindarr.Application.Abstractions.Ops;

/// <summary>
/// Runs one pass of TMDB discover prewarm (metadata + optional image cache fill).
/// Used by the periodic worker and by setup complete to trigger the first pass during the countdown.
/// </summary>
public interface ITmdbDiscoverPrewarmRunner
{
	/// <summary>
	/// Runs one prewarm pass. Fetches discover for a batch of users, upserts into metadata store, optionally prefetches images.
	/// </summary>
	/// <param name="startOffset">User list offset (0 for first batch; worker passes its current offset for round-robin).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Next offset to use for the following pass (startOffset + number of users processed this pass).</returns>
	Task<int> RunOnceAsync(int startOffset, CancellationToken cancellationToken);
}
