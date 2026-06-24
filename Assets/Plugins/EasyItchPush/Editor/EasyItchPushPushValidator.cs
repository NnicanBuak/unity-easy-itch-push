using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace EasyItchPush.Editor
{
    internal sealed class EasyItchPushPushArtifact
    {
        public string ProfileName = string.Empty;
        public string BaseChannel = string.Empty;
        public string RemoteChannel = string.Empty;
        public string ArchivePath = string.Empty;
        public string Version = string.Empty;
    }

    internal static class EasyItchPushPushValidator
    {
        private static readonly Regex VersionSuffixRegex = new Regex(
            @"-v(?<version>\d+\.\d+\.\d+(?:-hotfix\d+)?)\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryCollectExistingBuilds(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            out List<EasyItchPushPushArtifact> artifacts)
        {
            artifacts = new List<EasyItchPushPushArtifact>();
            settings.AutoSyncVersionWithPlayerSettings();
            settings.SyncProfileMappingsWithBuildProfiles();

            var profiles = EasyItchPushBuildProfiles.FindAllProfileAssets()
                .Where(profile => profile != null && settings.IsProfileEnabled(pushMode, profile.Name))
                .ToList();
            var issues = new List<string>();
            if (profiles.Count == 0)
            {
                issues.Add("No enabled Unity Build Profiles were found.");
            }

            foreach (var profile in profiles)
            {
                var profileName = profile.Name;
                var baseChannel = settings.GetChannelForProfile(profileName);
                var archivePath = EasyItchPushBuilder.FindArchiveForProfileVersion(settings, baseChannel, profileName);
                if (string.IsNullOrEmpty(archivePath))
                {
                    var latestArchive = EasyItchPushBuilder.FindLatestArchiveForProfile(settings, baseChannel, profileName);
                    if (!string.IsNullOrEmpty(latestArchive) && TryParseVersionFromArchiveName(latestArchive, out var latestVersion))
                    {
                        issues.Add(
                            $"{profileName} ({baseChannel}): found {latestVersion.ToStringWithPrefix()}, but configured version is {settings.ResolvedVersionWithPrefix}. Build this profile with the configured version before pushing.");
                    }
                    else
                    {
                        issues.Add(
                            $"{profileName} ({baseChannel}): no zip archive found for {settings.ResolvedVersionWithPrefix} in {settings.GetBuildDirectory(baseChannel)}.");
                    }

                    continue;
                }

                artifacts.Add(new EasyItchPushPushArtifact
                {
                    ProfileName = profileName,
                    BaseChannel = baseChannel,
                    RemoteChannel = settings.GetRemoteChannel(baseChannel, pushMode),
                    ArchivePath = archivePath
                });
            }

            return ValidateArtifacts(settings, pushMode, artifacts, issues);
        }

        public static bool TryCollectBuildResult(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            EasyItchPushBuildAllResult result,
            out List<EasyItchPushPushArtifact> artifacts)
        {
            artifacts = new List<EasyItchPushPushArtifact>();
            var buildIssues = new List<string>();

            if (result.Results.Count == 0)
            {
                buildIssues.Add("No Build Profile results were produced.");
            }

            foreach (var item in result.Results)
            {
                if (!item.Succeeded)
                {
                    buildIssues.Add($"{item.ProfileName}: build result is {item.Result}.");
                    continue;
                }

                if (string.IsNullOrEmpty(item.ArchivePath))
                {
                    buildIssues.Add($"{item.ProfileName} ({item.Channel}): zip archive was not created.");
                    continue;
                }

                artifacts.Add(new EasyItchPushPushArtifact
                {
                    ProfileName = item.ProfileName,
                    BaseChannel = item.Channel,
                    RemoteChannel = settings.GetRemoteChannel(item.Channel, pushMode),
                    ArchivePath = item.ArchivePath
                });
            }

            if (buildIssues.Count > 0)
            {
                ShowBuildStageFailed(settings, pushMode, buildIssues);
                return false;
            }

            return ValidateArtifacts(settings, pushMode, artifacts, new List<string>());
        }

        private static bool ValidateArtifacts(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            List<EasyItchPushPushArtifact> artifacts,
            List<string> issues)
        {
            ValidateUnityProjectVersion(settings, issues);

            foreach (var artifact in artifacts)
            {
                if (!settings.ValidatePushSettings(pushMode, artifact.RemoteChannel, settings.ResolvedVersion, out var validationMessage))
                {
                    issues.Add($"{artifact.ProfileName} ({artifact.RemoteChannel}): {validationMessage}");
                }

                if (!File.Exists(artifact.ArchivePath))
                {
                    issues.Add($"{artifact.ProfileName} ({artifact.BaseChannel}): archive does not exist at {artifact.ArchivePath}.");
                    continue;
                }

                if (!string.Equals(Path.GetExtension(artifact.ArchivePath), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"{artifact.ProfileName} ({artifact.BaseChannel}): upload artifact is not a zip archive.");
                    continue;
                }

                if (!TryParseVersionFromArchiveName(artifact.ArchivePath, out var version))
                {
                    issues.Add($"{artifact.ProfileName} ({artifact.BaseChannel}): archive name must end with -vX.Y.Z.zip or -vX.Y.Z-hotfixN.zip.");
                    continue;
                }

                artifact.Version = version.ToStringWithPrefix();
                if (!string.Equals(artifact.Version, settings.ResolvedVersionWithPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(
                        $"{artifact.ProfileName} ({artifact.BaseChannel}): archive version {artifact.Version} does not match configured {settings.ResolvedVersionWithPrefix}.");
                }

                var resolvedChannel = EasyItchPushSettings.SanitizeItchChannel(artifact.RemoteChannel);
                if (string.Equals(resolvedChannel, "html5", StringComparison.OrdinalIgnoreCase) &&
                    !EasyItchPushButler.ZipContainsEntry(artifact.ArchivePath, "index.html"))
                {
                    issues.Add($"{artifact.ProfileName} ({artifact.RemoteChannel}): html5 zip must contain index.html in the archive root.");
                }
            }

            var versions = artifacts
                .Where(artifact => !string.IsNullOrEmpty(artifact.Version))
                .Select(artifact => artifact.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (versions.Count > 1)
            {
                issues.Add("Platform archive versions differ: " + string.Join(", ", versions));
            }

            if (artifacts.Count == 0)
            {
                issues.Add("No valid platform archives were found to push.");
            }

            if (issues.Count > 0)
            {
                ShowValidationFailed(settings, pushMode, issues);
                return false;
            }

            LogSummary(settings, pushMode, artifacts);
            return true;
        }

        private static void ValidateUnityProjectVersion(EasyItchPushSettings settings, List<string> issues)
        {
            if (!BuildVersion.TryParse(PlayerSettings.bundleVersion, out var playerVersion))
            {
                issues.Add($"Unity PlayerSettings.bundleVersion is invalid: {PlayerSettings.bundleVersion}.");
                return;
            }

            if (!string.Equals(playerVersion.ToStringWithoutPrefix(), settings.ResolvedVersion, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Unity PlayerSettings.bundleVersion {playerVersion.ToStringWithoutPrefix()} does not match configured {settings.ResolvedVersion}.");
            }

            var expectedVersionCode = settings.CurrentVersion.ToVersionCode();
            if (PlayerSettings.Android.bundleVersionCode != expectedVersionCode)
            {
                issues.Add($"Unity Android.bundleVersionCode {PlayerSettings.Android.bundleVersionCode} does not match configured {expectedVersionCode}.");
            }
        }

        private static bool TryParseVersionFromArchiveName(string archivePath, out BuildVersion version)
        {
            version = BuildVersion.Default;
            var fileName = Path.GetFileName(archivePath);
            var match = VersionSuffixRegex.Match(fileName);
            return match.Success && BuildVersion.TryParse(match.Groups["version"].Value, out version);
        }

        private static void ShowValidationFailed(EasyItchPushSettings settings, EasyItchPushMode pushMode, List<string> issues)
        {
            var uniqueIssues = issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Distinct()
                .ToList();

            var message = new StringBuilder();
            message.AppendLine($"Fix these {settings.GetPushModeLabel(pushMode)} issues before publishing:");
            message.AppendLine();

            foreach (var issue in uniqueIssues.Take(12))
            {
                message.AppendLine("- " + issue);
            }

            if (uniqueIssues.Count > 12)
            {
                message.AppendLine($"- ...and {uniqueIssues.Count - 12} more issue(s). See log file for details.");
            }

            message.AppendLine();
            message.AppendLine("Log file:");
            message.AppendLine(EasyItchPushLog.CurrentLogPath);

            EasyItchPushLog.Error(
                $"Push validation failed for {settings.GetPushModeLabel(pushMode)}:\n" +
                string.Join("\n", uniqueIssues.Select(issue => "- " + issue)));
            EditorUtility.DisplayDialog(
                $"Easy Itch Push {settings.GetPushModeLabel(pushMode)} validation failed",
                message.ToString(),
                "OK");
        }

        private static void ShowBuildStageFailed(EasyItchPushSettings settings, EasyItchPushMode pushMode, List<string> issues)
        {
            var uniqueIssues = issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Distinct()
                .ToList();

            var message = new StringBuilder();
            message.AppendLine($"Build stage failed, so {settings.GetPushModeLabel(pushMode)} publishing was skipped:");
            message.AppendLine();

            foreach (var issue in uniqueIssues.Take(12))
            {
                message.AppendLine("- " + issue);
            }

            if (uniqueIssues.Count > 12)
            {
                message.AppendLine($"- ...and {uniqueIssues.Count - 12} more issue(s). See log file for details.");
            }

            message.AppendLine();
            message.AppendLine("Log file:");
            message.AppendLine(EasyItchPushLog.CurrentLogPath);

            EasyItchPushLog.Error(
                $"Build stage failed before {settings.GetPushModeLabel(pushMode)} publishing:\n" +
                string.Join("\n", uniqueIssues.Select(issue => "- " + issue)));
            EditorUtility.DisplayDialog(
                $"Easy Itch Push {settings.GetPushModeLabel(pushMode)} build failed",
                message.ToString(),
                "OK");
        }

        private static void LogSummary(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            List<EasyItchPushPushArtifact> artifacts)
        {
            var lines = new List<string>
            {
                $"=== Easy Itch Push Push Validation ({settings.GetPushModeLabel(pushMode)} {settings.ResolvedVersionWithPrefix}) ===",
                $"target={settings.GetItchGameTarget(pushMode)} userversion={settings.ResolvedVersion}"
            };

            foreach (var artifact in artifacts)
            {
                lines.Add(
                    $"{artifact.ProfileName} base={artifact.BaseChannel} remote={artifact.RemoteChannel} version={artifact.Version} archive={artifact.ArchivePath}");
            }

            EasyItchPushLog.Info(string.Join("\n", lines));
        }
    }
}
