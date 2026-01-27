using Tindarr.Application.Abstractions.Persistence;

namespace Tindarr.Application.Interfaces.Preferences;

public interface IUserPreferencesService
{
	Task<UserPreferencesRecord> GetOrDefaultAsync(string userId, CancellationToken cancellationToken);

	Task<UserPreferencesRecord> UpdateAsync(string userId, UserPreferencesUpsert upsert, CancellationToken cancellationToken);
}

