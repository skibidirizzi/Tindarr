using System.Net.Http.Headers;
using Tindarr.Application.Abstractions.Ops;

namespace Tindarr.Infrastructure.Integrations.Tmdb.Http;

/// <summary>
/// Sets Authorization: Bearer from effective TMDB Read Access Token (DB or config) so credentials can be updated without restart.
/// </summary>
public sealed class TmdbAuthHandler(IEffectiveAdvancedSettings effectiveSettings) : DelegatingHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var token = effectiveSettings.GetEffectiveTmdbReadAccessToken();
		if (!string.IsNullOrWhiteSpace(token))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}
		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}
