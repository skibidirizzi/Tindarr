using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Interfaces.Interactions;

public interface ISwipeDeckSource
{
    Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken);
}
