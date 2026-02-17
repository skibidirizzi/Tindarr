using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Tindarr.Api.Hosting;

public static class StreamingProxy
{
	private static readonly string[] RequestHeaderAllowList =
	[
		HeaderNames.Range,
		HeaderNames.IfRange,
		HeaderNames.IfModifiedSince,
		HeaderNames.IfNoneMatch,
		HeaderNames.Accept,
		HeaderNames.AcceptEncoding
	];

	private static readonly string[] ResponseHeaderAllowList =
	[
		HeaderNames.ContentType,
		HeaderNames.ContentLength,
		HeaderNames.AcceptRanges,
		HeaderNames.ContentRange,
		HeaderNames.ETag,
		HeaderNames.LastModified,
		HeaderNames.ContentDisposition,
		HeaderNames.CacheControl
	];

	public static void CopyAllowedRequestHeaders(HttpRequest source, HttpRequestMessage dest)
	{
		foreach (var headerName in RequestHeaderAllowList)
		{
			if (source.Headers.TryGetValue(headerName, out var values))
			{
				dest.Headers.TryAddWithoutValidation(headerName, (IEnumerable<string>)values);
			}
		}
	}

	public static void CopyAllowedResponseHeaders(HttpResponseMessage upstreamResponse, HttpResponse downstreamResponse)
	{
		foreach (var headerName in ResponseHeaderAllowList)
		{
			if (upstreamResponse.Headers.TryGetValues(headerName, out var values)
				|| upstreamResponse.Content.Headers.TryGetValues(headerName, out values))
			{
				downstreamResponse.Headers[headerName] = values.ToArray();
			}
		}
	}

	public static async Task StreamBodyAsync(HttpResponseMessage upstreamResponse, HttpResponse downstreamResponse, CancellationToken cancellationToken)
	{
		downstreamResponse.StatusCode = (int)upstreamResponse.StatusCode;
		CopyAllowedResponseHeaders(upstreamResponse, downstreamResponse);
		await upstreamResponse.Content.CopyToAsync(downstreamResponse.Body, cancellationToken).ConfigureAwait(false);
	}
}
