using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Ops;

public sealed class EffectiveRegistrationOptions(
	IServiceScopeFactory scopeFactory,
	IOptions<RegistrationOptions> configOptions) : IEffectiveRegistrationOptions
{
	private readonly RegistrationOptions _config = configOptions.Value;
	private (bool AllowOpenRegistration, bool RequireAdminApprovalForNewUsers, string DefaultRole)? _cache;
	private readonly object _lock = new();

	public bool AllowOpenRegistration
	{
		get
		{
			EnsureLoaded();
			return _cache!.Value.AllowOpenRegistration;
		}
	}

	public bool RequireAdminApprovalForNewUsers
	{
		get
		{
			EnsureLoaded();
			return _cache!.Value.RequireAdminApprovalForNewUsers;
		}
	}

	public string DefaultRole
	{
		get
		{
			EnsureLoaded();
			return _cache!.Value.DefaultRole;
		}
	}

	public void Invalidate()
	{
		lock (_lock)
		{
			_cache = null;
		}
	}

	private void EnsureLoaded()
	{
		if (_cache is not null)
		{
			return;
		}

		lock (_lock)
		{
			if (_cache is not null)
			{
				return;
			}

			RegistrationSettingsRecord? record = null;
			try
			{
				using var scope = scopeFactory.CreateScope();
				var repo = scope.ServiceProvider.GetRequiredService<IRegistrationSettingsRepository>();
				record = repo.GetAsync(CancellationToken.None).GetAwaiter().GetResult();
			}
			catch
			{
				// Use config defaults if DB not available
			}

			var allowOpen = record?.AllowOpenRegistration ?? _config.AllowOpenRegistration;
			var requireApproval = record?.RequireAdminApprovalForNewUsers ?? _config.RequireAdminApprovalForNewUsers;
			var defaultRole = !string.IsNullOrWhiteSpace(record?.DefaultRole) ? record.DefaultRole!.Trim() : _config.DefaultRole;
			if (string.IsNullOrEmpty(defaultRole))
			{
				defaultRole = "Contributor";
			}

			_cache = (allowOpen, requireApproval, defaultRole);
		}
	}
}
