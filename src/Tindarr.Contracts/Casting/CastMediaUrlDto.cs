namespace Tindarr.Contracts.Casting;

public sealed record CastMediaUrlDto(
	string Url,
	string ContentType,
	string Title,
	string? SubTitle);
