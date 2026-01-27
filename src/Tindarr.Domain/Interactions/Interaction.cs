using Tindarr.Domain.Common;

namespace Tindarr.Domain.Interactions;

public sealed record Interaction(
    string UserId,
    ServiceScope Scope,
    int TmdbId,
    InteractionAction Action,
    DateTimeOffset CreatedAtUtc);
