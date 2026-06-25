using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushActions
    {
        private sealed class PublishPipelineResult
        {
            public bool ValidationPassed;
            public bool Succeeded;
            public int PushedCount;
        }

        [MenuItem("Tools/Easy Itch Push/Build All Profiles", priority = 0)]
        public static void BuildAllProfiles()
        {
            var settings = GetPreparedSettings();
            var result = EasyItchPushBuilder.BuildAllProfiles(settings);
            ShowBuildAllResult(result, "Build All Profiles", publishResult: null);
        }

        [MenuItem("Tools/Easy Itch Push/Build All Profiles and Push", priority = 1)]
        public static void BuildAllProfilesAndPush()
        {
            var settings = GetPreparedSettings();
            var pushMode = settings.PushMode;
            var modeLabel = settings.GetPushModeLabel(pushMode);
            if (!ValidateItchSettingsForProfiles(settings, pushMode))
            {
                return;
            }

            EasyItchPushLog.Info($"Starting Build All Profiles and Push ({modeLabel})");

            var result = EasyItchPushBuilder.BuildAllProfiles(settings, forceRelease: true);
            var publishResult = new PublishPipelineResult();
            if (EasyItchPushPushValidator.TryCollectBuildResult(settings, pushMode, result, out var artifacts))
            {
                publishResult.ValidationPassed = true;
                publishResult.Succeeded = PushArtifacts(settings, pushMode, artifacts, out publishResult.PushedCount);
            }

            ShowBuildAllResult(result, $"Build All Profiles ({modeLabel})", publishResult);
        }

        [MenuItem("Tools/Easy Itch Push/Push Existing Builds", priority = 2)]
        public static void PushExistingBuilds()
        {
            var settings = GetPreparedSettings();
            var pushMode = settings.PushMode;
            var modeLabel = settings.GetPushModeLabel(pushMode);
            if (!ValidateItchSettingsForProfiles(settings, pushMode))
            {
                return;
            }

            EasyItchPushLog.Info($"Starting Push Existing Builds ({modeLabel})");

            var publishResult = new PublishPipelineResult();
            if (EasyItchPushPushValidator.TryCollectExistingBuilds(settings, pushMode, out var artifacts))
            {
                publishResult.ValidationPassed = true;
                publishResult.Succeeded = PushArtifacts(settings, pushMode, artifacts, out publishResult.PushedCount);
            }

            ShowPushExistingResult(settings, pushMode, publishResult);
        }

        [MenuItem("Tools/Easy Itch Push/Install or Upgrade Butler", priority = 20)]
        public static void InstallOrUpgradeButler()
        {
            EasyItchPushButler.InstallOrUpgrade(GetPreparedSettings());
        }

        [MenuItem("Tools/Easy Itch Push/Login to itch.io", priority = 21)]
        public static void Login()
        {
            EasyItchPushButler.Login(GetPreparedSettings());
        }

        [MenuItem("Tools/Easy Itch Push/Check Butler Status", priority = 22)]
        public static void Status()
        {
            var settings = GetPreparedSettings();
            EasyItchPushButler.Status(settings, settings.PushMode);
        }

        [MenuItem("Tools/Easy Itch Push/Open Build Folder", priority = 40)]
        public static void OpenBuildFolder()
        {
            var buildDirectory = GetPreparedSettings().GetOutputRootDirectory();
            Directory.CreateDirectory(buildDirectory);
            EditorUtility.RevealInFinder(buildDirectory);
        }

        [MenuItem("Tools/Easy Itch Push/Open Game Page", priority = 41)]
        public static void OpenGamePage()
        {
            var settings = GetPreparedSettings();
            var pushMode = settings.PushMode;
            var modeLabel = settings.GetPushModeLabel(pushMode);
            var url = settings.GetGamePageUrl(pushMode);
            if (string.IsNullOrEmpty(url))
            {
                EditorUtility.DisplayDialog($"Missing {modeLabel} itch.io settings", $"Set Username and Game Slug for {modeLabel} first.", "OK");
                return;
            }

            Application.OpenURL(url);
        }

        private static bool ValidateItchSettingsForProfiles(EasyItchPushSettings settings, EasyItchPushMode pushMode)
        {
            settings.SyncProfileMappingsWithBuildProfiles();
            var profiles = EasyItchPushBuildProfiles.FindAllProfileAssets();
            profiles = profiles.FindAll(profile => profile != null && settings.IsProfileEnabledNoSync(pushMode, profile.Name));
            var validationMessages = new List<string>();
            var modeLabel = settings.GetPushModeLabel(pushMode);

            foreach (var profile in profiles)
            {
                var baseChannel = settings.GetChannelForProfileNoSync(profile.Name);
                var remoteChannel = settings.GetRemoteChannel(baseChannel, pushMode);
                if (!settings.ValidatePushSettings(pushMode, remoteChannel, settings.ResolvedVersion, out var validationMessage))
                {
                    validationMessages.Add($"{profile.Name} -> {remoteChannel}\n{validationMessage}");
                }
            }

            if (profiles.Count == 0)
            {
                validationMessages.Add("No enabled Unity Build Profiles were found.");
            }

            if (validationMessages.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    $"Missing {modeLabel} itch.io settings",
                    string.Join("\n\n", validationMessages),
                    "OK");
                SettingsService.OpenProjectSettings("Project/Easy Itch Push");
                return false;
            }

            return true;
        }

        private static bool PushArtifacts(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            IEnumerable<EasyItchPushPushArtifact> artifacts,
            out int pushedCount)
        {
            pushedCount = 0;
            var modeLabel = settings.GetPushModeLabel(pushMode);

            foreach (var artifact in artifacts)
            {
                if (!EasyItchPushButler.Push(settings, pushMode, artifact.ArchivePath, artifact.RemoteChannel, settings.ResolvedVersion))
                {
                    EasyItchPushLog.Error(
                        $"Stopped publishing {modeLabel} build after Butler failed for {artifact.ProfileName} ({artifact.RemoteChannel}).");
                    return false;
                }

                pushedCount++;
            }

            if (pushedCount == 0)
            {
                return false;
            }

            EasyItchPushLog.Info($"Finished publishing {pushedCount} {modeLabel} artifact(s).");

            if (settings.playSoundOnComplete)
            {
                EditorApplication.Beep();
            }

            var url = settings.GetGamePageUrl(pushMode);
            if (settings.openGamePageOnSuccess && !string.IsNullOrEmpty(url))
            {
                Application.OpenURL(url);
            }

            return true;
        }

        private static void ShowBuildAllResult(EasyItchPushBuildAllResult result, string title, PublishPipelineResult publishResult)
        {
            var succeeded = result.Results.FindAll(item => item.Succeeded).Count;
            var failed = result.Results.Count - succeeded;
            EasyItchPushLog.Info(
                $"{title} finished. Profiles={result.Results.Count}, Succeeded={succeeded}, FailedOrCancelled={failed}, LogFile={EasyItchPushLog.CurrentLogPath}");

            if (result.Results.Count == 0)
            {
                return;
            }

            var message = new List<string>
            {
                $"Profiles: {result.Results.Count}",
                $"Succeeded: {succeeded}",
                $"Failed or cancelled: {failed}"
            };

            if (publishResult != null)
            {
                if (publishResult.Succeeded)
                {
                    message.Add($"Published artifacts: {publishResult.PushedCount}");
                }
                else if (publishResult.ValidationPassed)
                {
                    message.Add("Publishing failed before all artifacts were uploaded.");
                }
                else
                {
                    message.Add("Publishing was skipped because build output validation failed.");
                }
            }

            message.Add(string.Empty);
            message.Add("Log file:");
            message.Add(EasyItchPushLog.CurrentLogPath);

            var dialogTitle = result.Succeeded && (publishResult == null || publishResult.Succeeded)
                ? $"{title} succeeded"
                : $"{title} finished with errors";
            EditorUtility.DisplayDialog(dialogTitle, string.Join("\n", message), "OK");
        }

        private static void ShowPushExistingResult(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            PublishPipelineResult publishResult)
        {
            var modeLabel = settings.GetPushModeLabel(pushMode);
            var lines = new List<string>();
            var succeeded = publishResult != null && publishResult.Succeeded;

            if (succeeded)
            {
                lines.Add($"Published artifacts: {publishResult.PushedCount}");
            }
            else if (publishResult != null && publishResult.ValidationPassed)
            {
                lines.Add("Publishing failed before all artifacts were uploaded.");
            }
            else
            {
                lines.Add("Publishing was skipped because validation failed.");
            }

            lines.Add(string.Empty);
            lines.Add("Log file:");
            lines.Add(EasyItchPushLog.CurrentLogPath);

            EditorUtility.DisplayDialog(
                succeeded ? $"Push Existing ({modeLabel}) succeeded" : $"Push Existing ({modeLabel}) finished with errors",
                string.Join("\n", lines),
                "OK");
        }

        private static EasyItchPushSettings GetPreparedSettings()
        {
            var settings = EasyItchPushSettings.Instance;
            EasyItchPushChangelog.EnsureProjectChangelogExists();
            EasyItchPushSettingsGui.FlushPendingSave(settings);
            EasyItchPushSettingsGui.FlushPendingChangelogSave();
            EasyItchPushSettingsGui.EnsureSettingsAreSynchronized(settings);
            return settings;
        }
    }
}
