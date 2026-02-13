using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;
using Tindarr.Infrastructure.Persistence.Repositories;

namespace Tindarr.UnitTests.Infrastructure.Persistence;

public sealed class UserRepositoryCleanupTests
{
	[Fact]
	public async Task PurgeGuestUsersAsync_deletes_old_guests_and_related_state()
	{
		await using var connection = new SqliteConnection("DataSource=:memory:");
		await connection.OpenAsync();

		var options = new DbContextOptionsBuilder<TindarrDbContext>()
			.UseSqlite(connection)
			.Options;

		var now = new DateTimeOffset(2026, 02, 13, 12, 0, 0, TimeSpan.Zero);
		var cutoff = now.AddDays(-1);

		await using (var setup = new TindarrDbContext(options))
		{
			await setup.Database.EnsureCreatedAsync();

			setup.Users.AddRange(
				new UserEntity { Id = "guest-old", DisplayName = "Guest", CreatedAtUtc = now.AddDays(-2) },
				new UserEntity { Id = "guest-new", DisplayName = "Guest", CreatedAtUtc = now.AddHours(-2) },
				new UserEntity { Id = "real-user", DisplayName = "Real", CreatedAtUtc = now.AddDays(-10), PasswordHash = new byte[] { 1 }, PasswordSalt = new byte[] { 2 }, PasswordIterations = 1000 },
				new UserEntity { Id = "guest-with-password", DisplayName = "Guest", CreatedAtUtc = now.AddDays(-10), PasswordHash = new byte[] { 1 }, PasswordSalt = new byte[] { 2 }, PasswordIterations = 1000 }
			);

			setup.UserPreferences.Add(new UserPreferencesEntity
			{
				UserId = "guest-old",
				IncludeAdult = false,
				UpdatedAtUtc = now
			});

			setup.UserRoles.Add(new UserRoleEntity { UserId = "guest-old", RoleName = "Contributor" });
			setup.UserRoles.Add(new UserRoleEntity { UserId = "guest-new", RoleName = "Contributor" });

			setup.Interactions.AddRange(
				new InteractionEntity
				{
					UserId = "guest-old",
					ServiceType = ServiceType.Plex,
					ServerId = "srv",
					TmdbId = 1,
					Action = InteractionAction.Like,
					CreatedAtUtc = now.AddDays(-2)
				},
				new InteractionEntity
				{
					UserId = "guest-new",
					ServiceType = ServiceType.Plex,
					ServerId = "srv",
					TmdbId = 2,
					Action = InteractionAction.Nope,
					CreatedAtUtc = now.AddHours(-1)
				}
			);

			setup.AcceptedMovies.Add(new AcceptedMovieEntity
			{
				ServiceType = ServiceType.Plex,
				ServerId = "srv",
				TmdbId = 42,
				AcceptedAtUtc = now.AddDays(-2),
				AcceptedByUserId = "guest-old"
			});

			await setup.SaveChangesAsync();
		}

		await using (var db = new TindarrDbContext(options))
		{
			var repo = new UserRepository(db);
			var deleted = await repo.PurgeGuestUsersAsync(cutoff, CancellationToken.None);
			Assert.Equal(1, deleted);
		}

		await using (var verify = new TindarrDbContext(options))
		{
			var remainingUserIds = await verify.Users.AsNoTracking().Select(u => u.Id).ToListAsync();
			Assert.DoesNotContain("guest-old", remainingUserIds);
			Assert.Contains("guest-new", remainingUserIds);
			Assert.Contains("real-user", remainingUserIds);
			Assert.Contains("guest-with-password", remainingUserIds);

			var remainingInteractionUserIds = await verify.Interactions.AsNoTracking().Select(i => i.UserId).ToListAsync();
			Assert.DoesNotContain("guest-old", remainingInteractionUserIds);
			Assert.Contains("guest-new", remainingInteractionUserIds);

			var accepted = await verify.AcceptedMovies.AsNoTracking().SingleAsync(a => a.TmdbId == 42);
			Assert.Null(accepted.AcceptedByUserId);
		}
	}
}
