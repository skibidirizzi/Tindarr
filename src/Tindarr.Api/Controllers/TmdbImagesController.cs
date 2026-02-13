using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Options;

namespace Tindarr.Api.Controllers;

[ApiController]
[Route("api/v1/tmdb")]
public sealed class TmdbImagesController(
	ITmdbImageCache imageCache,
	ITmdbMetadataStore metadataStore,
	IOptions<TmdbOptions> tmdbOptions) : ControllerBase
{
	[HttpGet("images/{size}/{*path}")]
	[AllowAnonymous]
	public async Task<IActionResult> GetImage(string size, string path, CancellationToken cancellationToken = default)
	{
		var settings = await metadataStore.GetSettingsAsync(cancellationToken);
		if (settings.PosterMode != TmdbPosterMode.LocalProxy || settings.ImageCacheMaxMb <= 0)
		{
			// Redirect to TMDB directly.
			var normalizedBase = tmdbOptions.Value.ImageBaseUrl.TrimEnd('/');
			var normalizedSize = (size ?? string.Empty).Trim().Trim('/');
			var normalizedPath = (path ?? string.Empty).Trim();
			if (!normalizedPath.StartsWith('/'))
			{
				normalizedPath = "/" + normalizedPath;
			}
			return Redirect($"{normalizedBase}/{normalizedSize}{normalizedPath}");
		}

		var result = await imageCache.GetOrFetchAsync(size, path, cancellationToken);
		if (result is null)
		{
			return NotFound();
		}

		// NOTE: physical path is in a private cache dir, not user-provided.
		return PhysicalFile(result.FilePath, result.ContentType);
	}
}
