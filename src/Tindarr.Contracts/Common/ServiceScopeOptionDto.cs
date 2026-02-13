namespace Tindarr.Contracts.Common;

public sealed record ServiceScopeOptionDto(
	string ServiceType,
	string ServerId,
	string DisplayName);
