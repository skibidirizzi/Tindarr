using Tindarr.Application.Options;

namespace Tindarr.Application.Abstractions.Notifications;

public interface IOutgoingWebhookNotifier
{
	void TryNotify(OutgoingWebhookEvents @event, string eventType, object data, DateTimeOffset? occurredAtUtc = null);
}
