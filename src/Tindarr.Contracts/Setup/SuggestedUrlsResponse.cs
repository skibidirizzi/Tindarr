namespace Tindarr.Contracts.Setup;

public sealed record SuggestedUrlsResponse(
	int? Port,
	string? SuggestedLanHostPort,
	string? SuggestedWanHostPort);
