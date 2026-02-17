using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Rooms;

public sealed class RoomLifetimeProvider(
	IServiceScopeFactory scopeFactory,
	IMemoryCache cache,
	IOptions<JwtOptions> jwtOptions) : IRoomLifetimeProvider
{
	private static readonly object CacheKey = new();
	private readonly JwtOptions jwt = jwtOptions.Value;

	public async Task<TimeSpan> GetRoomTtlAsync(CancellationToken cancellationToken)
		=> (await GetAsync(cancellationToken).ConfigureAwait(false)).RoomTtl;

	public async Task<TimeSpan> GetGuestSessionTtlAsync(CancellationToken cancellationToken)
		=> (await GetAsync(cancellationToken).ConfigureAwait(false)).GuestSessionTtl;

	private Task<ResolvedLifetimes> GetAsync(CancellationToken cancellationToken)
	{
		return cache.GetOrCreateAsync(CacheKey, async entry =>
		{
			entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

			using var scope = scopeFactory.CreateScope();
			var repo = scope.ServiceProvider.GetRequiredService<IJoinAddressSettingsRepository>();
			var settings = await repo.GetAsync(cancellationToken).ConfigureAwait(false);

			var roomMinutes = settings?.RoomLifetimeMinutes;
			var guestMinutes = settings?.GuestSessionLifetimeMinutes;

			var roomTtl = TimeSpan.FromMinutes(
				roomMinutes is > 0 ? roomMinutes.Value : 120);
			var guestTtl = TimeSpan.FromMinutes(
				guestMinutes is > 0 ? guestMinutes.Value : Math.Clamp(jwt.AccessTokenMinutes, 1, 24 * 60));

			return new ResolvedLifetimes(roomTtl, guestTtl);
		})!;
	}

	private sealed record ResolvedLifetimes(TimeSpan RoomTtl, TimeSpan GuestSessionTtl);
}
