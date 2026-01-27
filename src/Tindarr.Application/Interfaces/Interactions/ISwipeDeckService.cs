using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Interfaces.Interactions;

public interface ISwipeDeckService
{
    Task<IReadOnlyList<SwipeCard>> GetDeckAsync(string userId, ServiceScope scope, int limit, CancellationToken cancellationToken);
}
