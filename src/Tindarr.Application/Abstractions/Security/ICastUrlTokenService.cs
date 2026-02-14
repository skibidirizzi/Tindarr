namespace Tindarr.Application.Abstractions.Security;

public interface ICastUrlTokenService
{
	string IssueRoomQrToken(string roomId, DateTimeOffset nowUtc);

	bool TryValidateRoomQrToken(string token, string roomId, DateTimeOffset nowUtc);
}
