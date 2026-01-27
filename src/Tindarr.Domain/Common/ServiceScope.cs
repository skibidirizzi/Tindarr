namespace Tindarr.Domain.Common;

public sealed record ServiceScope(ServiceType ServiceType, string ServerId)
{
    public static bool TryCreate(string? serviceType, string? serverId, out ServiceScope? scope)
    {
        scope = null;

        if (!ServiceTypeParser.TryParse(serviceType, out var parsedType))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(serverId))
        {
            return false;
        }

        scope = new ServiceScope(parsedType, serverId.Trim());
        return true;
    }
}
