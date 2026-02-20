using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Contracts.Admin;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/settings")]
public sealed class SettingsController(IEffectiveAdvancedSettings effectiveAdvancedSettings) : ControllerBase
{
	/// <summary>
	/// Returns UI display settings (e.g. date/time format, time zone, date order). Available to any authenticated user.
	/// </summary>
	[HttpGet("display")]
	public ActionResult<AdvancedSettingsDisplayDto> GetDisplay()
	{
		var mode = effectiveAdvancedSettings.GetDateTimeDisplayMode();
		var timeZoneId = effectiveAdvancedSettings.GetTimeZoneId();
		var dateOrder = effectiveAdvancedSettings.GetDateOrder();
		return Ok(new AdvancedSettingsDisplayDto(mode, timeZoneId, dateOrder));
	}
}
