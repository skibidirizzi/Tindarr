namespace Tindarr.Domain.Common;

public enum ServiceType
{
    Tmdb = 0,
    Plex = 1,
    Jellyfin = 2,
    Emby = 3,
    Radarr = 4
}

public static class ServiceTypeParser
{
    public static bool TryParse(string? value, out ServiceType serviceType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            serviceType = default;
            return false;
        }

        return Enum.TryParse(value, ignoreCase: true, out serviceType);
    }
}
