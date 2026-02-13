using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class UserPreferencesRepository(TindarrDbContext db) : IUserPreferencesRepository
{
	public async Task<UserPreferencesRecord?> GetAsync(string userId, CancellationToken cancellationToken)
	{
		var entity = await db.UserPreferences
			.AsNoTracking()
			.Where(x => x.UserId == userId)
			.SingleOrDefaultAsync(cancellationToken);

		if (entity is null)
		{
			return null;
		}

		return new UserPreferencesRecord(
			entity.IncludeAdult,
			entity.MinReleaseYear,
			entity.MaxReleaseYear,
			entity.MinRating,
			entity.MaxRating,
			DeserializeIntList(entity.PreferredGenresJson),
			DeserializeIntList(entity.ExcludedGenresJson),
			DeserializeStringList(entity.PreferredOriginalLanguagesJson),
			DeserializeStringList(entity.ExcludedOriginalLanguagesJson),
			DeserializeStringList(entity.PreferredRegionsJson),
			DeserializeStringList(entity.ExcludedRegionsJson),
			string.IsNullOrWhiteSpace(entity.SortBy) ? "popularity.desc" : entity.SortBy,
			entity.UpdatedAtUtc);
	}

	public async Task UpsertAsync(string userId, UserPreferencesUpsert upsert, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken)
	{
		var entity = await db.UserPreferences.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
		if (entity is null)
		{
			entity = new UserPreferencesEntity
			{
				UserId = userId,
				IncludeAdult = upsert.IncludeAdult,
				MinReleaseYear = upsert.MinReleaseYear,
				MaxReleaseYear = upsert.MaxReleaseYear,
				MinRating = upsert.MinRating,
				MaxRating = upsert.MaxRating,
				PreferredGenresJson = Serialize(upsert.PreferredGenres),
				ExcludedGenresJson = Serialize(upsert.ExcludedGenres),
				PreferredOriginalLanguagesJson = Serialize(upsert.PreferredOriginalLanguages),
				ExcludedOriginalLanguagesJson = Serialize(upsert.ExcludedOriginalLanguages),
				PreferredRegionsJson = Serialize(upsert.PreferredRegions),
				ExcludedRegionsJson = Serialize(upsert.ExcludedRegions),
				SortBy = string.IsNullOrWhiteSpace(upsert.SortBy) ? "popularity.desc" : upsert.SortBy.Trim(),
				UpdatedAtUtc = updatedAtUtc
			};

			db.UserPreferences.Add(entity);
		}
		else
		{
			entity.IncludeAdult = upsert.IncludeAdult;
			entity.MinReleaseYear = upsert.MinReleaseYear;
			entity.MaxReleaseYear = upsert.MaxReleaseYear;
			entity.MinRating = upsert.MinRating;
			entity.MaxRating = upsert.MaxRating;
			entity.PreferredGenresJson = Serialize(upsert.PreferredGenres);
			entity.ExcludedGenresJson = Serialize(upsert.ExcludedGenres);
			entity.PreferredOriginalLanguagesJson = Serialize(upsert.PreferredOriginalLanguages);
			entity.ExcludedOriginalLanguagesJson = Serialize(upsert.ExcludedOriginalLanguages);
			entity.PreferredRegionsJson = Serialize(upsert.PreferredRegions);
			entity.ExcludedRegionsJson = Serialize(upsert.ExcludedRegions);
			entity.SortBy = string.IsNullOrWhiteSpace(upsert.SortBy) ? "popularity.desc" : upsert.SortBy.Trim();
			entity.UpdatedAtUtc = updatedAtUtc;
		}

		await db.SaveChangesAsync(cancellationToken);
	}

	private static IReadOnlyList<int> DeserializeIntList(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<List<int>>(json) ?? [];
		}
		catch
		{
			return [];
		}
	}

	private static IReadOnlyList<string> DeserializeStringList(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<List<string>>(json) ?? [];
		}
		catch
		{
			return [];
		}
	}

	private static string Serialize<T>(IReadOnlyList<T> values)
	{
		return JsonSerializer.Serialize(values ?? []);
	}
}

