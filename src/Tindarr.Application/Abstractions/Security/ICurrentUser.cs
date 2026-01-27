namespace Tindarr.Application.Abstractions.Security;

public interface ICurrentUser
{
	string UserId { get; }

	IReadOnlyCollection<string> Roles { get; }

	bool IsInRole(string role);
}

