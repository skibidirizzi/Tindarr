using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Api.Services;
using Tindarr.Infrastructure.Backup;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/admin/backup")]
public sealed class AdminBackupController(
	BackupDataPaths paths,
	MasterBackupRestoreService masterBackupRestoreService,
	TmdbBackupRestoreService tmdbBackupRestoreService) : ControllerBase
{
	/// <summary>Download a ZIP containing all DBs (tindarr, Plex, Jellyfin, Emby, TMDB) and optionally tmdb-images.</summary>
	[HttpGet("master")]
	public async Task<IActionResult> DownloadMasterBackup(CancellationToken cancellationToken = default)
	{
		var includeTmdbImages = await tmdbBackupRestoreService.GetIncludeImagesAsync(cancellationToken).ConfigureAwait(false);
		var stream = new MemoryStream();
		await masterBackupRestoreService.WriteMasterBackupZipAsync(stream, includeTmdbImages, cancellationToken).ConfigureAwait(false);
		stream.Position = 0;
		return File(stream, "application/zip", "tindarr-master-backup.zip");
	}

	/// <summary>Download the main app database (tindarr.db or configured name).</summary>
	[HttpGet("main")]
	public IActionResult DownloadMainBackup()
	{
		if (!System.IO.File.Exists(paths.MainDbPath))
			return NotFound("Main database file not found.");
		var fileName = Path.GetFileName(paths.MainDbPath);
		var stream = new FileStream(paths.MainDbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		return File(stream, "application/octet-stream", fileName);
	}

	/// <summary>Download the Plex cache database.</summary>
	[HttpGet("plex")]
	public IActionResult DownloadPlexBackup()
	{
		if (!System.IO.File.Exists(paths.PlexCacheDbPath))
			return NotFound("Plex cache database not found.");
		var stream = new FileStream(paths.PlexCacheDbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		return File(stream, "application/octet-stream", "plexcache.db");
	}

	[HttpPost("plex/restore")]
	[RequestSizeLimit(500 * 1024 * 1024)]
	public async Task<IActionResult> RestorePlex(IFormFile? file, CancellationToken cancellationToken = default)
	{
		if (file is null || file.Length == 0)
			return BadRequest("No file uploaded.");
		await CopyUploadToPathAsync(file, paths.PlexCacheDbPath, cancellationToken).ConfigureAwait(false);
		return Ok(new { message = "Plex cache restored." });
	}

	/// <summary>Download the Jellyfin cache database.</summary>
	[HttpGet("jellyfin")]
	public IActionResult DownloadJellyfinBackup()
	{
		if (!System.IO.File.Exists(paths.JellyfinCacheDbPath))
			return NotFound("Jellyfin cache database not found.");
		var stream = new FileStream(paths.JellyfinCacheDbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		return File(stream, "application/octet-stream", "jellyfincache.db");
	}

	[HttpPost("jellyfin/restore")]
	[RequestSizeLimit(500 * 1024 * 1024)]
	public async Task<IActionResult> RestoreJellyfin(IFormFile? file, CancellationToken cancellationToken = default)
	{
		if (file is null || file.Length == 0)
			return BadRequest("No file uploaded.");
		await CopyUploadToPathAsync(file, paths.JellyfinCacheDbPath, cancellationToken).ConfigureAwait(false);
		return Ok(new { message = "Jellyfin cache restored." });
	}

	/// <summary>Download the Emby cache database.</summary>
	[HttpGet("emby")]
	public IActionResult DownloadEmbyBackup()
	{
		if (!System.IO.File.Exists(paths.EmbyCacheDbPath))
			return NotFound("Emby cache database not found.");
		var stream = new FileStream(paths.EmbyCacheDbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		return File(stream, "application/octet-stream", "embycache.db");
	}

	[HttpPost("emby/restore")]
	[RequestSizeLimit(500 * 1024 * 1024)]
	public async Task<IActionResult> RestoreEmby(IFormFile? file, CancellationToken cancellationToken = default)
	{
		if (file is null || file.Length == 0)
			return BadRequest("No file uploaded.");
		await CopyUploadToPathAsync(file, paths.EmbyCacheDbPath, cancellationToken).ConfigureAwait(false);
		return Ok(new { message = "Emby cache restored." });
	}

	private static async Task CopyUploadToPathAsync(IFormFile file, string targetPath, CancellationToken cancellationToken)
	{
		var dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		await using var src = file.OpenReadStream();
		await using var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
		await src.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
	}
}
