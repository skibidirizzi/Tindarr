namespace Tindarr.Contracts.Radarr;

public sealed record RadarrAutoAddResponse(int Attempted, int Added, int SkippedExisting, int Failed, string? Message);
