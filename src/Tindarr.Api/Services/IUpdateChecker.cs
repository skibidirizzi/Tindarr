namespace Tindarr.Api.Services;

public interface IUpdateChecker
{
	Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
}
