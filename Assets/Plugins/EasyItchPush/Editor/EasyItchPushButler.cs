using System;
using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushButler
    {
        private const string ButlerBaseUrl = "https://broth.itch.zone/butler";

        public static string ResolveExecutable(EasyItchPushSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ButlerExecutablePath) && File.Exists(settings.ButlerExecutablePath))
            {
                return settings.ButlerExecutablePath;
            }

            var installedPath = GetInstalledExecutablePath();
            if (File.Exists(installedPath))
            {
                return installedPath;
            }

            var pathResult = Application.platform == RuntimePlatform.WindowsEditor
                ? EasyItchPushProcess.Run("where", "butler")
                : EasyItchPushProcess.Run("which", "butler");

            if (!pathResult.Succeeded)
            {
                return string.Empty;
            }

            var lines = pathResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 && File.Exists(lines[0]) ? lines[0] : string.Empty;
        }

        public static string EnsureExecutable(EasyItchPushSettings settings)
        {
            var executable = ResolveExecutable(settings);
            if (!string.IsNullOrEmpty(executable))
            {
                return executable;
            }

            if (!EditorUtility.DisplayDialog(
                    "Butler is not installed",
                    "Easy Itch Push can install Butler into Library/EasyItchPush/Butler.",
                    "Install",
                    "Cancel"))
            {
                return string.Empty;
            }

            return InstallOrUpgrade(settings);
        }

        public static string InstallOrUpgrade(EasyItchPushSettings settings)
        {
            var platform = GetButlerPlatform();
            if (string.IsNullOrEmpty(platform))
            {
                EditorUtility.DisplayDialog("Unsupported platform", "Automatic Butler installation is not supported on this editor platform.", "OK");
                return string.Empty;
            }

            var installRoot = GetInstallRoot();
            var extractRoot = Path.Combine(installRoot, platform);
            var archivePath = Path.Combine(installRoot, "butler.zip");
            Directory.CreateDirectory(installRoot);

            var url = $"{ButlerBaseUrl}/{platform}/LATEST/archive/default";
            var progressId = Progress.Start("Easy Itch Push", "Downloading Butler");

            try
            {
                using (var client = new WebClient())
                {
                    EasyItchPushLog.Info($"Downloading Butler from {url}");
                    client.DownloadFile(url, archivePath);
                }

                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, true);
                }

                Directory.CreateDirectory(extractRoot);
                ExtractArchive(archivePath, extractRoot);

                var executable = GetInstalledExecutablePath();
                if (!File.Exists(executable))
                {
                    throw new FileNotFoundException("Butler executable was not found after extraction.", executable);
                }

                if (Application.platform != RuntimePlatform.WindowsEditor)
                {
                    EasyItchPushProcess.Run("chmod", $"+x {EasyItchPushProcess.Quote(executable)}");
                }

                settings.ButlerExecutablePath = executable;
                settings.SaveSettings();

                EasyItchPushLog.Info($"Butler installed at {executable}");
                return executable;
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Error(ex.Message);
                EditorUtility.DisplayDialog("Butler install failed", ex.Message, "OK");
                return string.Empty;
            }
            finally
            {
                Progress.Finish(progressId);
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
            }
        }

        public static void Login(EasyItchPushSettings settings)
        {
            var executable = EnsureExecutable(settings);
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            var result = EasyItchPushProcess.Run(executable, "login", Directory.GetCurrentDirectory(), "Butler Login");
            ReportProcessResult("Butler login", result);
        }

        public static void Status(EasyItchPushSettings settings, EasyItchPushMode pushMode)
        {
            var modeLabel = settings.GetPushModeLabel(pushMode);
            if (string.IsNullOrWhiteSpace(settings.GetItchUsername(pushMode)) ||
                string.IsNullOrWhiteSpace(settings.GetGameSlug(pushMode)))
            {
                EditorUtility.DisplayDialog(
                    $"Missing {modeLabel} itch.io settings",
                    $"Set Username and Game Slug for {modeLabel} before checking Butler status.",
                    "OK");
                SettingsService.OpenProjectSettings("Project/Easy Itch Push");
                return;
            }

            var executable = EnsureExecutable(settings);
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            var result = EasyItchPushProcess.Run(
                executable,
                "status " + EasyItchPushProcess.Quote(settings.GetItchGameTarget(pushMode)),
                Directory.GetCurrentDirectory(),
                $"Butler Status ({modeLabel})");
            ReportProcessResult($"Butler status ({modeLabel})", result);
        }

        public static bool Push(EasyItchPushSettings settings, EasyItchPushMode pushMode, string pathToPush, string channel, string version)
        {
            var modeLabel = settings.GetPushModeLabel(pushMode);
            if (!settings.ValidatePushSettings(pushMode, channel, version, out var validationMessage))
            {
                EditorUtility.DisplayDialog($"Missing {modeLabel} itch.io settings", validationMessage, "OK");
                SettingsService.OpenProjectSettings("Project/Easy Itch Push");
                return false;
            }

            if (!Directory.Exists(pathToPush) && !File.Exists(pathToPush))
            {
                EditorUtility.DisplayDialog("Build output not found", $"Nothing to push at:\n{pathToPush}", "OK");
                return false;
            }

            if (!File.Exists(pathToPush) || !string.Equals(Path.GetExtension(pathToPush), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid upload artifact", $"Only zip archives can be pushed to itch.io.\n\nArtifact:\n{pathToPush}", "OK");
                return false;
            }

            var resolvedChannel = EasyItchPushSettings.SanitizeItchChannel(channel);
            if (string.Equals(resolvedChannel, "html5", StringComparison.OrdinalIgnoreCase) &&
                !ZipContainsEntry(pathToPush, "index.html"))
            {
                EditorUtility.DisplayDialog("Invalid WebGL archive", $"HTML5 uploads must contain index.html in the zip root:\n{pathToPush}", "OK");
                return false;
            }

            var executable = EnsureExecutable(settings);
            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            var args = BuildPushArguments(settings, pushMode, pathToPush, resolvedChannel, version);
            EasyItchPushLog.Info(
                $"Prepared {modeLabel} push: target={settings.GetItchGameTarget(pushMode)} channel={resolvedChannel} userversion={version} localArchive={pathToPush} uploadArtifact={pathToPush}");

            var result = EasyItchPushProcess.Run(executable, args, Directory.GetCurrentDirectory(), $"Publishing to itch.io ({modeLabel})");
            ReportProcessResult($"Butler push ({modeLabel})", result);

            return result.Succeeded;
        }

        public static string BuildPushArguments(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            string pathToPush,
            string channel,
            string version)
        {
            return string.Join(" ", new[]
            {
                "push",
                EasyItchPushProcess.Quote(pathToPush),
                EasyItchPushProcess.Quote(settings.GetItchTarget(pushMode, channel)),
                "--userversion",
                EasyItchPushProcess.Quote(version)
            });
        }

        public static bool ZipContainsEntry(string zipPath, string entryName)
        {
            try
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    return archive.GetEntry(entryName) != null;
                }
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not inspect zip archive {zipPath}: {ex.Message}");
                return false;
            }
        }

        private static void ReportProcessResult(string label, EasyItchPushProcessResult result)
        {
            if (result.Succeeded)
            {
                EasyItchPushLog.Info($"{label} finished.");
                return;
            }

            EasyItchPushLog.Error($"{label} failed with exit code {result.ExitCode}.");
            EditorUtility.DisplayDialog($"{label} failed", $"Exit code: {result.ExitCode}\n\n{result.Error}", "OK");
        }

        private static string GetButlerPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return "windows-amd64";
                case RuntimePlatform.LinuxEditor:
                    return "linux-amd64";
                case RuntimePlatform.OSXEditor:
                    return "darwin-amd64";
                default:
                    return string.Empty;
            }
        }

        private static string GetInstallRoot()
        {
            return Path.GetFullPath(Path.Combine("Library", "EasyItchPush", "Butler"));
        }

        private static string GetInstalledExecutablePath()
        {
            var platform = GetButlerPlatform();
            var executableName = Application.platform == RuntimePlatform.WindowsEditor ? "butler.exe" : "butler";
            return Path.Combine(GetInstallRoot(), platform, executableName);
        }

        private static void ExtractArchive(string archivePath, string destinationPath)
        {
            EasyItchPushProcessResult result;

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var command = "Expand-Archive -LiteralPath " +
                              ToPowerShellLiteral(archivePath) +
                              " -DestinationPath " +
                              ToPowerShellLiteral(destinationPath) +
                              " -Force";

                result = EasyItchPushProcess.Run(
                    "powershell",
                    "-NoProfile -ExecutionPolicy Bypass -Command " + EasyItchPushProcess.Quote(command),
                    Directory.GetCurrentDirectory(),
                    "Extracting Butler");
            }
            else
            {
                result = EasyItchPushProcess.Run(
                    "unzip",
                    $"-oq {EasyItchPushProcess.Quote(archivePath)} -d {EasyItchPushProcess.Quote(destinationPath)}",
                    Directory.GetCurrentDirectory(),
                    "Extracting Butler");
            }

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error);
            }
        }

        private static string ToPowerShellLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

    }
}
