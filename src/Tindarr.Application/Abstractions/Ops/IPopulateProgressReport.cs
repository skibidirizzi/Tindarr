using Tindarr.Contracts.Admin;

namespace Tindarr.Application.Abstractions.Ops;

/// <summary>
/// Reports progress of the Admin DB Populate job (fetch-all-details and fetch-all-images).
/// </summary>
public interface IPopulateProgressReport
{
	PopulateStatusDto GetStatus();

	/// <summary>Mark running and set totals (call when starting).</summary>
	void SetRunning(int detailsTotal, int imagesTotal);

	/// <summary>Update details progress.</summary>
	void SetDetailsDone(int done);

	/// <summary>Update images progress.</summary>
	void SetImagesDone(int done);

	/// <summary>Optional message.</summary>
	void SetMessage(string? message);

	/// <summary>Mark idle (call when finished or not started).</summary>
	void SetIdle();
}
