using Microsoft.Extensions.Logging;
using Sharpcaster;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;
using Tindarr.Application.Interfaces.Casting;

namespace Tindarr.Infrastructure.Casting;

public sealed class SharpCasterCastClient(ILoggerFactory loggerFactory, ILogger<SharpCasterCastClient> logger) : ICastClient
{
	private const string DefaultMediaReceiverAppId = "CC1AD845";

	public async Task<IReadOnlyList<CastDevice>> DiscoverAsync(CancellationToken cancellationToken)
	{
		var locator = new ChromecastLocator();
		var receivers = await locator.FindReceiversAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

		return receivers
			.OrderBy(r => r.Name)
			.Select(r => new CastDevice(
				Id: ToDeviceId(r),
				Name: (r.Name ?? string.Empty).Trim(),
				Address: r.DeviceUri?.Host,
				Port: r.Port))
			.Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name))
			.ToList();
	}

	public async Task CastAsync(string deviceId, CastMedia media, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(deviceId))
		{
			throw new ArgumentException("deviceId is required", nameof(deviceId));
		}
		if (string.IsNullOrWhiteSpace(media.ContentUrl))
		{
			throw new ArgumentException("ContentUrl is required", nameof(media));
		}
		if (string.IsNullOrWhiteSpace(media.ContentType))
		{
			throw new ArgumentException("ContentType is required", nameof(media));
		}

		var locator = new ChromecastLocator();
		var receivers = await locator.FindReceiversAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
		var receiver = receivers.FirstOrDefault(r => string.Equals(ToDeviceId(r), deviceId, StringComparison.Ordinal));
		if (receiver is null)
		{
			throw new InvalidOperationException("Cast device not found.");
		}

		var clientLogger = loggerFactory.CreateLogger<ChromecastClient>();
		var client = new ChromecastClient(clientLogger);
		try
		{
			await client.ConnectChromecast(receiver).ConfigureAwait(false);
			await client.LaunchApplicationAsync(DefaultMediaReceiverAppId).ConfigureAwait(false);

			var m = new Media
			{
				ContentUrl = media.ContentUrl,
				ContentType = media.ContentType,
				Metadata = new MediaMetadata
				{
					Title = media.Title,
					SubTitle = media.SubTitle
				}
			};

			await client.MediaChannel.LoadAsync(m).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "cast failed. DeviceId={DeviceId} Url={Url}", deviceId, media.ContentUrl);
			throw;
		}
		finally
		{
			try
			{
				await client.Dispose().ConfigureAwait(false);
			}
			catch
			{
				// ignore
			}
		}
	}

	private static string ToDeviceId(ChromecastReceiver receiver)
	{
		var host = receiver.DeviceUri?.Host;
		if (string.IsNullOrWhiteSpace(host))
		{
			return string.Empty;
		}
		return $"{host}:{receiver.Port}";
	}
}
