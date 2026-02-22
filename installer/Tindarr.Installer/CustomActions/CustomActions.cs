using System;
using System.Diagnostics;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace Tindarr.Installer.CustomActions
{
	public static class CustomActions
	{
		/// <summary>Validates PORT property (1024-65535) and sets PORT_VALID to "1" or "0". Must never throw or return Failure when run from UI.</summary>
		[CustomAction]
		public static ActionResult ValidatePort(Session session)
		{
			try
			{
				try { session.Log("ValidatePort: start"); } catch { }
				string portStr = "";
				try
				{
					var portObj = session["PORT"];
					portStr = (portObj == null) ? "" : Convert.ToString(portObj).Trim();
				}
				catch { }
				try { session["PORT_VALID"] = "0"; } catch { }
				if (string.IsNullOrEmpty(portStr))
					return ActionResult.Success;
				if (!int.TryParse(portStr, out var port))
					return ActionResult.Success;
				if (port >= 1024 && port <= 65535)
				{
					try { session["PORT_VALID"] = "1"; } catch { }
				}
				return ActionResult.Success;
			}
			catch
			{
				return ActionResult.Success;
			}
		}

		/// <summary>Launch InstallTindarrService.bat elevated (runas) so user gets UAC and service is created/started.</summary>
		[CustomAction]
		public static ActionResult LaunchInstallServiceBatch(Session session)
		{
			try
			{
				var folder = session["INSTALLFOLDER"]?.ToString().Trim();
				if (string.IsNullOrEmpty(folder))
					return ActionResult.Success;
				var batPath = Path.Combine(folder, "InstallTindarrService.bat");
				if (!File.Exists(batPath))
					return ActionResult.Success;
				var startInfo = new ProcessStartInfo
				{
					FileName = batPath,
					UseShellExecute = true,
					Verb = "runas",
					WorkingDirectory = folder
				};
				Process.Start(startInfo);
			}
			catch (Exception ex)
			{
				try { session.Log("LaunchInstallServiceBatch: " + ex.ToString()); } catch { }
			}
			return ActionResult.Success;
		}

		/// <summary>If PORT invalid, abort install. No Message() call - that can crash; installer will show generic error.</summary>
		[CustomAction]
		public static ActionResult ValidatePortFail(Session session)
		{
			session.Log("ValidatePortFail: PORT_VALID = " + (session["PORT_VALID"] ?? "null"));
			if (session["PORT_VALID"] != "1")
			{
				session.Log("ValidatePortFail: port invalid, returning Failure");
				return ActionResult.Failure;
			}
			return ActionResult.Success;
		}

		/// <summary>Deferred CA: CustomActionData = "PORT;CommonAppDataFolder". Creates %ProgramData%\Tindarr, writes port.txt only if file does not exist (preserve on upgrade).</summary>
		[CustomAction]
		public static ActionResult WritePortTxt(Session session)
		{
			session.Log("WritePortTxt: start");
			try
			{
				var data = session.CustomActionData?.ToString().Trim() ?? "";
				if (string.IsNullOrEmpty(data))
				{
					session.Log("WritePortTxt: CustomActionData is empty");
					return ActionResult.Success;
				}
				var parts = data.Split(new[] { ';' }, 2, StringSplitOptions.None);
				if (parts.Length < 2)
				{
					session.Log("WritePortTxt: CustomActionData format invalid");
					return ActionResult.Success;
				}
				var portVal = parts[0].Trim();
				var baseDir = parts[1].Trim();
				if (portVal.Length == 0)
					return ActionResult.Success;

				var dir = Path.Combine(baseDir, "Tindarr");
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var path = Path.Combine(dir, "port.txt");
				if (File.Exists(path))
				{
					session.Log("WritePortTxt: port.txt exists, preserving");
					return ActionResult.Success;
				}
				File.WriteAllText(path, portVal);
				session.Log("WritePortTxt: wrote " + path);
				return ActionResult.Success;
			}
			catch (Exception ex)
			{
				session.Log("WritePortTxt: " + ex.ToString());
				return ActionResult.Success;
			}
		}

		/// <summary>Runs during uninstall (deferred). Stops and deletes the Tindarr.Api Windows service if it exists. Never fails uninstall.</summary>
		[CustomAction]
		public static ActionResult RemoveTindarrService(Session session)
		{
			const string ServiceName = "Tindarr.Api";
			var scExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
			try
			{
				try { session.Log("RemoveTindarrService: start"); } catch { }
				// Stop the service (ignore exit code - may already be stopped or not exist).
				RunSc(session, scExe, "stop \"" + ServiceName + "\"", 15000);
				// Give SCM time to release the service before delete.
				System.Threading.Thread.Sleep(500);
				// Delete the service (ignore exit code - may not exist).
				RunSc(session, scExe, "delete \"" + ServiceName + "\"", 10000);
				try { session.Log("RemoveTindarrService: done"); } catch { }
			}
			catch (Exception ex)
			{
				try { session.Log("RemoveTindarrService: " + ex.ToString()); } catch { }
			}
			return ActionResult.Success;
		}

		private static void RunSc(Session session, string scExe, string arguments, int waitMs)
		{
			using (var proc = new Process())
			{
				proc.StartInfo.FileName = scExe;
				proc.StartInfo.Arguments = arguments;
				proc.StartInfo.UseShellExecute = false;
				proc.StartInfo.CreateNoWindow = true;
				proc.Start();
				proc.WaitForExit(waitMs);
				try { session.Log("RemoveTindarrService: sc " + arguments + " exit " + proc.ExitCode); } catch { }
			}
		}
	}
}
