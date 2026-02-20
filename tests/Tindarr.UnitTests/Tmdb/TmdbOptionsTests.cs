using Tindarr.Application.Options;

namespace Tindarr.UnitTests.Tmdb;

public sealed class TmdbOptionsTests
{
	[Fact]
	public void IsValid_ReturnsTrue_WhenAllFieldsWithinBounds()
	{
		var options = ValidOptions();
		Assert.True(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenBaseUrlIsInvalid()
	{
		var options = ValidOptions(baseUrl: "not-a-valid-uri");
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenBaseUrlIsRelative()
	{
		var options = ValidOptions(baseUrl: "/relative/path");
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenImageBaseUrlIsInvalid()
	{
		var options = ValidOptions(imageBaseUrl: "not-a-valid-uri");
		Assert.False(options.IsValid());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void IsValid_ReturnsFalse_WhenRequestsPerSecondBelowOne(int value)
	{
		var options = ValidOptions(requestsPerSecond: value);
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenRequestsPerSecondAboveFifty()
	{
		var options = ValidOptions(requestsPerSecond: 51);
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsTrue_WhenRequestsPerSecondAtBoundaryOneAndFifty()
	{
		Assert.True(ValidOptions(requestsPerSecond: 1).IsValid());
		Assert.True(ValidOptions(requestsPerSecond: 50).IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenDiscoverCacheSecondsNegative()
	{
		var options = ValidOptions(discoverCacheSeconds: -1);
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenDetailsCacheSecondsNegative()
	{
		var options = ValidOptions(detailsCacheSeconds: -1);
		Assert.False(options.IsValid());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void IsValid_ReturnsFalse_WhenOperationTimeoutSecondsBelowOne(int value)
	{
		var options = ValidOptions(operationTimeoutSeconds: value);
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsFalse_WhenOperationTimeoutSecondsAbove120()
	{
		var options = ValidOptions(operationTimeoutSeconds: 121);
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsTrue_WhenOperationTimeoutSecondsAtBoundaryOneAnd120()
	{
		Assert.True(ValidOptions(operationTimeoutSeconds: 1).IsValid());
		Assert.True(ValidOptions(operationTimeoutSeconds: 120).IsValid());
	}

	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void IsValid_ReturnsFalse_WhenPosterSizeEmpty(string? value)
	{
		var options = ValidOptions(posterSize: value ?? "");
		Assert.False(options.IsValid());
	}

	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void IsValid_ReturnsFalse_WhenBackdropSizeEmpty(string? value)
	{
		var options = ValidOptions(backdropSize: value ?? "");
		Assert.False(options.IsValid());
	}

	[Fact]
	public void IsValid_ReturnsTrue_WhenCacheSecondsZero()
	{
		var options = ValidOptions(discoverCacheSeconds: 0, detailsCacheSeconds: 0);
		Assert.True(options.IsValid());
	}

	private static TmdbOptions ValidOptions(
		string? baseUrl = null,
		string? imageBaseUrl = null,
		int? requestsPerSecond = null,
		int? discoverCacheSeconds = null,
		int? detailsCacheSeconds = null,
		int? operationTimeoutSeconds = null,
		string? posterSize = null,
		string? backdropSize = null)
	{
		return new TmdbOptions
		{
			ApiKey = "key",
			BaseUrl = baseUrl ?? "https://api.themoviedb.org/3/",
			ImageBaseUrl = imageBaseUrl ?? "https://image.tmdb.org/t/p/",
			PosterSize = posterSize ?? "w500",
			BackdropSize = backdropSize ?? "w780",
			RequestsPerSecond = requestsPerSecond ?? 4,
			DiscoverCacheSeconds = discoverCacheSeconds ?? 60,
			DetailsCacheSeconds = detailsCacheSeconds ?? 600,
			OperationTimeoutSeconds = operationTimeoutSeconds ?? 12
		};
	}
}
