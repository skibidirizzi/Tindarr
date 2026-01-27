namespace Tindarr.Contracts.Auth;

public sealed record SetPasswordRequest(string? CurrentPassword, string NewPassword);

