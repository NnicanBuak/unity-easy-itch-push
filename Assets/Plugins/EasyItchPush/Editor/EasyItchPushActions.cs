using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushActions
    {
        [MenuItem("Tools/Easy Itch Push/Build All Profiles", priority = 0)]
        public static void BuildAllProfiles()
        {
            var settings = GetPreparedSettings();
            var result = EasyItchPushBuilder.BuildAllProfiles(settings);
            ShowBuildAllResult(result, "Build All Profiles");
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
            if (EasyItchPushPushValidator.TryCollectBuildResult(settings, pushMode, result, out var artifacts))
            {
                PushArtifacts(settings, pushMode, artifacts);
            }

            ShowBuildAllResult(result, $"Build All Profiles ({modeLabel})");
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

            if (EasyItchPushPushValidator.TryCollectExistingBuilds(settings, pushMode, out var artifacts))
            {
                PushArtifacts(settings, pushMode, artifacts);
            }
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

        private static void PushArtifacts(
            EasyItchPushSettings settings,
            EasyItchPushMode pushMode,
            IEnumerable<EasyItchPushPushArtifact> artifacts)
        {
            var pushedCount = 0;
            var modeLabel = settings.GetPushModeLabel(pushMode);

            foreach (var artifact in artifacts)
            {
                if (!EasyItchPushButler.Push(settings, pushMode, artifact.ArchivePath, artifact.RemoteChannel, settings.ResolvedVersion))
                {
                    EasyItchPushLog.Error(
                        $"Stopped publishing {modeLabel} build after Butler failed for {artifact.ProfileName} ({artifact.RemoteChannel}).");
                    return;
                }

                pushedCount++;
            }

            if (pushedCount == 0)
            {
                return;
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
        }

        private static void ShowBuildAllResult(EasyItchPushBuildAllResult result, string title)
        {
            var succeeded = result.Results.FindAll(item => item.Succeeded).Count;
            var failed = result.Results.Count - succeeded;
            EasyItchPushLog.Info(
                $"{title} finished. Profiles={result.Results.Count}, Succeeded={succeeded}, FailedOrCancelled={failed}, LogFile={EasyItchPushLog.CurrentLogPath}");
        }

        private static EasyItchPushSettings GetPreparedSettings()
        {
            var settings = EasyItchPushSettings.Instance;
            EasyItchPushSettingsGui.FlushPendingSave(settings);
            EasyItchPushSettingsGui.EnsureSettingsAreSynchronized(settings);
            return settings;
        }
    }
}
