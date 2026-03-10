using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Notifications;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Notifications;

public sealed class OutgoingWebhookNotifier(
	OutgoingWebhookQueue queue,
	IEffectiveAdvancedSettings advancedSettings,
	ILogger<OutgoingWebhookNotifier> logger) : IOutgoingWebhookNotifier
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter() }
	};

	private sealed record WebhookEnvelope(
		string Event,
		DateTimeOffset OccurredAtUtc,
		object Data);

	public void TryNotify(OutgoingWebhookEvents @event, string eventType, object data, DateTimeOffset? occurredAtUtc = null)
	{
		try
		{
			var settings = advancedSettings.GetOutgoingWebhookSettings();
			if (!settings.Enabled)
			{
				return;
			}

			if (!settings.Events.HasFlag(@event))
			{
				return;
			}

			if (settings.Urls.Count == 0)
			{
				return;
			}

			var occurred = occurredAtUtc ?? DateTimeOffset.UtcNow;
			var envelope = new WebhookEnvelope(eventType, occurred, data);
			var body = JsonSerializer.Serialize(envelope, JsonOptions);
			var urls = settings.Urls.ToArray();
			var ok = queue.TryEnqueue(new OutgoingWebhookMessage(eventType, body, urls));
			if (!ok)
			{
				logger.LogWarning("Outgoing webhook queue is full; dropping event {EventType}", eventType);
			}
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to enqueue outgoing webhook event {EventType}", eventType);
		}
	}
}
