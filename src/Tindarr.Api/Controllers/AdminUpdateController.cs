using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Api.Services;
using Tindarr.Contracts.Admin;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/admin/update")]
public sealed class AdminUpdateController(IUpdateChecker updateChecker) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<AdminUpdateCheckResponse>> Check(CancellationToken cancellationToken)
	{
		var result = await updateChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
		return Ok(new AdminUpdateCheckResponse(
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
