namespace Tindarr.Contracts.Admin;

public sealed record AdminDeleteInteractionsRequest(
	IReadOnlyList<long> Ids);
