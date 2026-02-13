using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Contracts.Preferences;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/preferences")]
public sealed class PreferencesController(IUserPreferencesService preferencesService, ICurrentUser currentUser, ITmdbMetadataStore metadataStore) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<UserPreferencesDto>> Get(CancellationToken cancellationToken)
	{
		var prefs = await preferencesService.GetOrDefaultAsync(currentUser.UserId, cancellationToken);
		return Ok(Map(prefs));
	}

	[HttpPut]
	public async Task<ActionResult<UserPreferencesDto>> Put([FromBody] UpdateUserPreferencesRequest request, CancellationToken cancellationToken)
	{
		var upsert = new UserPreferencesUpsert(
			request.IncludeAdult,
			request.MinReleaseYear,
			request.MaxReleaseYear,
			request.MinRating,
			request.MaxRating,
			request.PreferredGenres ?? [],
			request.ExcludedGenres ?? [],
			request.PreferredOriginalLanguages ?? [],
			request.ExcludedOriginalLanguages ?? [],
			request.PreferredRegions ?? [],
			request.ExcludedRegions ?? [],
			string.IsNullOrWhiteSpace(request.SortBy) ? "popularity.desc" : request.SortBy.Trim());

		var updated = await preferencesService.UpdateAsync(currentUser.UserId, upsert, cancellationToken);

		// Preference changes should affect the next swipedeck immediately. The deck is served from a local per-user pool,
		// so clear it here to force repopulation with the new preference filters.
		await metadataStore.ClearUserPoolAsync(currentUser.UserId, cancellationToken);
		return Ok(Map(updated));
	}

	private static UserPreferencesDto Map(UserPreferencesRecord record)
	{
		return new UserPreferencesDto(
			record.IncludeAdult,
			record.MinReleaseYear,
			record.MaxReleaseYear,
			record.MinRating,
			record.MaxRating,
			record.PreferredGenres,
			record.ExcludedGenres,
			record.PreferredOriginalLanguages,
			record.ExcludedOriginalLanguages,
			record.PreferredRegions,
			record.ExcludedRegions,
			record.SortBy,
			record.UpdatedAtUtc);
	}
}

