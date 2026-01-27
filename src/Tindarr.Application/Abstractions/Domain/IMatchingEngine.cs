using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Abstractions.Domain;

public interface IMatchingEngine
{
	IReadOnlyList<int> ComputeLikedByAllMatches(
		ServiceScope scope,
		IReadOnlyList<Interaction> interactions,
		int minUsers = 2);
}

