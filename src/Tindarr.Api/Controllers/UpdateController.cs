using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Services;
using Tindarr.Contracts.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/update")]
public sealed class UpdateController(IUpdateChecker updateChecker) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<UpdateCheckResponse>> Check(CancellationToken cancellationToken)
	{
		var result = await updateChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
		return Ok(new UpdateCheckResponse(
			CurrentVersion: result.CurrentVersion,
			LatestVersion: result.LatestVersion,
			UpdateAvailable: result.UpdateAvailable,
			CheckedAtUtc: result.CheckedAtUtc,
			LatestReleaseUrl: result.LatestReleaseUrl,
			LatestReleaseName: result.LatestReleaseName,
			PublishedAtUtc: result.PublishedAtUtc,
			IsPreRelease: result.IsPreRelease,
			ReleaseNotes: result.ReleaseNotes,
			Error: result.Error));
	}
}
