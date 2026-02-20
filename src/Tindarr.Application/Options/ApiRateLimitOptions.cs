namespace Tindarr.Application.Options;

/// <summary>
/// Options for API-level rate limiting and abuse protection.
/// </summary>
public sealed class ApiRateLimitOptions
{
	public const string SectionName = "ApiRateLimit";

	/// <summary>
	/// When true, rate limiting is applied to API requests (per partition: IP or user).
	/// </summary>
	public bool Enabled { get; init; } = true;

	/// <summary>
	/// Maximum number of requests allowed per partition per window.
	/// </summary>
	public int PermitLimit { get; init; } = 200;

	/// <summary>
	/// Time window for the limit (e.g. 1 minute). Supported format: "00:01:00" or seconds as integer in config.
	/// </summary>
	public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

	public bool IsValid()
	{
		if (!Enabled)
		{
			return true;
		}

		if (PermitLimit < 1)
		{
			return false;
		}

		if (Window <= TimeSpan.Zero)
		{
			return false;
		}

		return true;
	}
}
