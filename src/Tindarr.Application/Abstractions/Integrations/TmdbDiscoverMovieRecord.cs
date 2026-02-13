using System.Text.Json.Serialization;

namespace Tindarr.Application.Abstractions.Integrations;

public sealed record TmdbDiscoverMovieRecord(
	[property: JsonPropertyName("id")] int Id,
	[property: JsonPropertyName("title")] string? Title,
	[property: JsonPropertyName("original_title")] string? OriginalTitle,
	[property: JsonPropertyName("overview")] string? Overview,
	[property: JsonPropertyName("poster_path")] string? PosterPath,
	[property: JsonPropertyName("backdrop_path")] string? BackdropPath,
	[property: JsonPropertyName("release_date")] string? ReleaseDate,
	[property: JsonPropertyName("original_language")] string? OriginalLanguage,
	[property: JsonPropertyName("vote_average")] double? VoteAverage,
	[property: JsonPropertyName("genre_ids")] IReadOnlyList<int>? GenreIds);
