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
			return Redirect(RedirectToTmdbUrl(size, path));
		}

		var result = await imageCache.GetOrFetchAsync(size, path, cancellationToken);
		if (result is null)
		{
			// Cache miss and on-demand fetch failed (e.g. no credentials, TMDB error).
			// Redirect to TMDB so the client still gets the image; avoids blank/fallback in UI.
			return Redirect(RedirectToTmdbUrl(size, path));
		}

		// NOTE: physical path is in a private cache dir, not user-provided.
		return PhysicalFile(result.FilePath, result.ContentType);
	}

	private string RedirectToTmdbUrl(string size, string path)
	{
		var normalizedBase = tmdbOptions.Value.ImageBaseUrl.TrimEnd('/');
		var normalizedSize = (size ?? string.Empty).Trim().Trim('/');
		var normalizedPath = (path ?? string.Empty).Trim();
		if (!normalizedPath.StartsWith('/'))
		{
			normalizedPath = "/" + normalizedPath;
		}
		return $"{normalizedBase}/{normalizedSize}{normalizedPath}";
	}
}
