using System.Threading.Channels;

namespace Tindarr.Infrastructure.Notifications;

public sealed record OutgoingWebhookMessage(
	string EventType,
	string BodyJson,
	string[] Urls);

public sealed class OutgoingWebhookQueue
{
	private readonly Channel<OutgoingWebhookMessage> _channel;

	public OutgoingWebhookQueue(int capacity = 1000)
	{
		_channel = Channel.CreateBounded<OutgoingWebhookMessage>(new BoundedChannelOptions(capacity)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropOldest
		});
	}

	public bool TryEnqueue(OutgoingWebhookMessage message) => _channel.Writer.TryWrite(message);

	public ChannelReader<OutgoingWebhookMessage> Reader => _channel.Reader;
}
