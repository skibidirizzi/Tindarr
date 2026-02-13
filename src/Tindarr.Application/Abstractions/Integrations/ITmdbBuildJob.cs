using Tindarr.Contracts.Tmdb;

namespace Tindarr.Application.Abstractions.Integrations;

public interface ITmdbBuildJob
{
	TmdbBuildStatusDto GetStatus();

	bool TryStart(StartTmdbBuildRequest request);

	bool TryCancel(string reason);
}
