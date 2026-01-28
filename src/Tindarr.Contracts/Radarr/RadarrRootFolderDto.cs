namespace Tindarr.Contracts.Radarr;

public sealed record RadarrRootFolderDto(int Id, string Path, long? FreeSpaceBytes);
