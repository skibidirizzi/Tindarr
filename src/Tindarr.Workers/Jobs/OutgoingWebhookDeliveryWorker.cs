using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Common;
using Tindarr.Infrastructure.Notifications;

namespace Tindarr.Workers.Jobs;

public sealed class OutgoingWebhookDeliveryWorker(
	OutgoingWebhookQueue queue,
	IHttpClientFactory httpClientFactory,
	ILogger<OutgoingWebhookDeliveryWorker> logger) : BackgroundService
{
	public const string HttpClientName = "tindarr.outgoing-webhooks";

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
		{
			stoppingToken.ThrowIfCancellationRequested();

			if (message.Urls.Length == 0)
			{
				continue;
			}

			var client = httpClientFactory.CreateClient(HttpClientName);
			foreach (var url in message.Urls)
			{
				stoppingToken.ThrowIfCancellationRequested();

				try
				{
					using var req = new HttpRequestMessage(HttpMethod.Post, url);
					req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Tindarr", TindarrVersion.Current.ToString(3)));
					req.Headers.TryAddWithoutValidation("X-Tindarr-Event", message.EventType);
					req.Content = new StringContent(message.BodyJson, Encoding.UTF8, "application/json");

					using var resp = await client.SendAsync(req, stoppingToken).ConfigureAwait(false);
					if (!resp.IsSuccessStatusCode)
					{
						logger.LogWarning(
							"Outgoing webhook delivery failed: {StatusCode} {EventType} -> {Url}",
							(int)resp.StatusCode,
							message.EventType,
							url);
					}
				}
				catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
				{
					logger.LogWarning(ex, "Outgoing webhook delivery error: {EventType} -> {Url}", message.EventType, url);
				}
			}
		}
	}
}
