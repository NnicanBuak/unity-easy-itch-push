using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushSettingsGui
    {
        private const double SaveDebounceDelaySeconds = 0.3d;
        private static readonly string[] PushModeLabels = { "Release", "Test" };
        private static bool pendingSave;
        private static double lastEditTime;

        internal readonly struct DrawOptions
        {
            public readonly bool ShowPushModeSelector;

            public DrawOptions(bool showPushModeSelector)
            {
                ShowPushModeSelector = showPushModeSelector;
            }
        }

        public static bool Draw(EasyItchPushSettings settings, DrawOptions options)
        {
            settings.SyncProfileMappingsWithBuildProfiles();
            var versionBeforeEdit = settings.CurrentVersion;
            EditorGUI.BeginChangeCheck();

            if (options.ShowPushModeSelector)
            {
                EditorGUILayout.LabelField("Push Mode", EditorStyles.boldLabel);
                DrawPushModeToolbar(settings);
                EditorGUILayout.HelpBox(
                    "Release pushes to versioned itch channels for the main page. Test pushes to the separate test page with base channels.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Version", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.versionMajor = Mathf.Max(0, EditorGUILayout.IntField("Major", settings.versionMajor));
                settings.versionMinor = Mathf.Max(0, EditorGUILayout.IntField("Minor", settings.versionMinor));
                settings.versionPatch = Mathf.Max(0, EditorGUILayout.IntField("Patch", settings.versionPatch));
            }

            settings.versionIsHotfix = EditorGUILayout.Toggle("Is Hotfix", settings.versionIsHotfix);
            using (new EditorGUI.DisabledScope(!settings.versionIsHotfix))
            {
                settings.versionHotfixNumber = Mathf.Max(1, EditorGUILayout.IntField("Hotfix Number", settings.versionHotfixNumber));
            }

            EditorGUILayout.LabelField("Preview", $"{settings.ResolvedVersionWithPrefix} / {settings.ResolvedVersion}");
            EditorGUILayout.HelpBox(
                "Version sync is automatic: edits here update global PlayerSettings and Unity 6 Build Profile Player Settings.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            DrawActiveTargetFields(settings);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            settings.outputRoot = EditorGUILayout.TextField("Output Root", settings.outputRoot);
            settings.developmentBuild = EditorGUILayout.Toggle("Development Build", settings.developmentBuild);
            settings.allowDebugging = EditorGUILayout.Toggle("Allow Debugging", settings.allowDebugging);
            settings.strictMode = EditorGUILayout.Toggle("Strict Mode", settings.strictMode);
            settings.detailedBuildReport = EditorGUILayout.Toggle("Detailed Build Report", settings.detailedBuildReport);
            settings.cleanBuildDirectory = EditorGUILayout.Toggle("Clean Build Directory", settings.cleanBuildDirectory);
            settings.compressWithLz4HC = EditorGUILayout.Toggle("LZ4HC Compression", settings.compressWithLz4HC);
            settings.applyReleaseObfuscation = EditorGUILayout.Toggle("Release Obfuscation", settings.applyReleaseObfuscation);
            using (new EditorGUI.DisabledScope(!settings.applyReleaseObfuscation))
            {
                settings.forceIl2CppForRelease = EditorGUILayout.Toggle("Force IL2CPP", settings.forceIl2CppForRelease);
                settings.releaseManagedStrippingLevel =
                    (ManagedStrippingLevel)EditorGUILayout.EnumPopup("Managed Stripping", settings.releaseManagedStrippingLevel);
            }

            if (settings.applyReleaseObfuscation)
            {
                EditorGUILayout.HelpBox(
                    "Release Obfuscation applies Unity release hardening without mutating settings: forces release build options, enables IL2CPP where supported, high managed stripping, and Strip Engine Code.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            DrawProfileMappings(settings);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Butler", EditorStyles.boldLabel);
            var butlerPath = EasyItchPushButler.ResolveExecutable(settings);
            EditorGUILayout.LabelField("Executable", string.IsNullOrEmpty(butlerPath) ? "Auto-detect (not found yet)" : butlerPath);
            EditorGUILayout.HelpBox("Butler path is resolved automatically: installed copy in Library/EasyItchPush/Butler first, then PATH.", MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("After Push", EditorStyles.boldLabel);
            settings.openGamePageOnSuccess = EditorGUILayout.Toggle("Open Game Page", settings.openGamePageOnSuccess);
            settings.playSoundOnComplete = EditorGUILayout.Toggle("Completion Sound", settings.playSoundOnComplete);

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(BuildActivePushSummary(settings), MessageType.Info);

            DrawProfileValidation(settings);

            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            if (DidVersionChange(versionBeforeEdit, settings.CurrentVersion))
            {
                settings.SyncVersionToPlayerSettings();
            }

            ScheduleSave();
            return true;
        }

        public static void EnsureSettingsAreSynchronized(EasyItchPushSettings settings)
        {
            if (!settings.HasVersionSnapshotChanged())
            {
                return;
            }

            settings.AutoSyncVersionWithPlayerSettings();
        }

        public static bool DrawPushModeToolbar(EasyItchPushSettings settings)
        {
            var selectedIndex = GUILayout.Toolbar((int)settings.PushMode, PushModeLabels);
            if (selectedIndex == (int)settings.PushMode)
            {
                return false;
            }

            settings.PushMode = (EasyItchPushMode)selectedIndex;
            return true;
        }

        public static string BuildActivePushSummary(EasyItchPushSettings settings)
        {
            return BuildPushSummary(settings, settings.PushMode);
        }

        public static string BuildPushSummary(EasyItchPushSettings settings, EasyItchPushMode pushMode)
        {
            var builder = new StringBuilder();
            var modeLabel = settings.GetPushModeLabel(pushMode);
            var target = settings.HasItchTarget(pushMode) ? settings.GetItchGameTarget(pushMode) : "(not configured)";
            var pageUrl = settings.GetGamePageUrl(pushMode);

            builder.AppendLine($"Mode: {modeLabel}");
            builder.AppendLine($"Target: {target}");
            builder.AppendLine($"Project ID: {settings.GetProjectId(pushMode)}");
            builder.AppendLine($"Version: {settings.ResolvedVersion}");
            builder.AppendLine($"Output Root: {settings.GetOutputRootDirectory()}");
            if (!string.IsNullOrEmpty(pageUrl))
            {
                builder.AppendLine($"Page: {pageUrl}");
            }

            if (settings.profileChannelMappings != null && settings.profileChannelMappings.Length > 0)
            {
                builder.AppendLine("Channels:");
                foreach (var mapping in settings.profileChannelMappings)
                {
                    if (mapping == null || !settings.IsProfileEnabledNoSync(pushMode, mapping.profileName))
                    {
                        continue;
                    }

                    var baseChannel = settings.GetChannelForProfileNoSync(mapping.profileName);
                    var remoteChannel = settings.GetRemoteChannel(baseChannel, pushMode);
                    builder.AppendLine($"- {mapping.profileName}: {baseChannel} -> {remoteChannel}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static void DrawActiveTargetFields(EasyItchPushSettings settings)
        {
            var pushMode = settings.PushMode;
            var modeLabel = settings.GetPushModeLabel(pushMode);

            EditorGUILayout.LabelField($"{modeLabel} itch.io", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These values are stored privately in local EditorPrefs and are not written into project files.", MessageType.None);

            var username = settings.GetItchUsername(pushMode);
            var updatedUsername = EditorGUILayout.TextField("Username", username);
            if (!string.Equals(username, updatedUsername, System.StringComparison.Ordinal))
            {
                settings.SetItchUsername(pushMode, updatedUsername);
            }

            var gameSlug = settings.GetGameSlug(pushMode);
            var updatedGameSlug = EditorGUILayout.TextField("Game Slug", gameSlug);
            if (!string.Equals(gameSlug, updatedGameSlug, System.StringComparison.Ordinal))
            {
                settings.SetGameSlug(pushMode, updatedGameSlug);
            }

            var projectId = settings.GetProjectId(pushMode);
            var updatedProjectId = EditorGUILayout.TextField("Project ID", projectId);
            if (!string.Equals(projectId, updatedProjectId, System.StringComparison.Ordinal))
            {
                settings.SetProjectId(pushMode, updatedProjectId);
            }
        }

        private static void DrawProfileMappings(EasyItchPushSettings settings)
        {
            EditorGUILayout.LabelField("Build Profiles", EditorStyles.boldLabel);
            if (settings.profileChannelMappings == null || settings.profileChannelMappings.Length == 0)
            {
                EditorGUILayout.HelpBox("Create Unity Build Profiles first. Selected profiles here will be built and pushed.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "Selected profiles are built in sequence. Base channel controls the local build folder and the test push channel. Release pushes automatically append the major/minor/patch version to that base channel.",
                MessageType.None);

            var macOsProfileNames = GetMacOsProfileNames();
            var shouldWarnAboutMacOsMonoFallback = Application.platform != RuntimePlatform.OSXEditor;
            var pushMode = settings.PushMode;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(24f);
                EditorGUILayout.LabelField("Profile", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Base Channel", EditorStyles.miniBoldLabel);
            }

            for (var i = 0; i < settings.profileChannelMappings.Length; i++)
            {
                var mapping = settings.profileChannelMappings[i];
                if (mapping == null)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var isEnabled = mapping.IsEnabled(pushMode);
                    var updatedIsEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(18f));
                    if (updatedIsEnabled != isEnabled)
                    {
                        mapping.SetEnabled(pushMode, updatedIsEnabled);
                    }

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(mapping.profileName);
                    }

                    mapping.channel = EditorGUILayout.TextField(mapping.channel);
                }

                if (mapping.IsEnabled(pushMode) &&
                    shouldWarnAboutMacOsMonoFallback &&
                    macOsProfileNames.Contains(mapping.profileName))
                {
                    EditorGUILayout.HelpBox(
                        "This macOS profile will be built with Mono on the current editor host. IL2CPP for macOS requires a macOS machine.",
                        MessageType.Warning);
                }
            }
        }

        private static void DrawProfileValidation(EasyItchPushSettings settings)
        {
            var validationMessages = new System.Collections.Generic.List<string>();
            var enabledProfileCount = 0;
            var pushMode = settings.PushMode;

            if (settings.profileChannelMappings != null)
            {
                foreach (var mapping in settings.profileChannelMappings)
                {
                    if (mapping == null || !settings.IsProfileEnabledNoSync(pushMode, mapping.profileName))
                    {
                        continue;
                    }

                    enabledProfileCount++;
                    var baseChannel = settings.GetChannelForProfileNoSync(mapping.profileName);
                    var remoteChannel = settings.GetRemoteChannel(baseChannel, pushMode);
                    if (!settings.ValidatePushSettings(pushMode, remoteChannel, settings.ResolvedVersion, out var validationMessage))
                    {
                        validationMessages.Add($"{mapping.profileName}: {baseChannel} -> {remoteChannel}\n{validationMessage}");
                    }
                }
            }

            if (enabledProfileCount == 0)
            {
                validationMessages.Add("No enabled Unity Build Profiles were found.");
            }

            if (validationMessages.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n\n", validationMessages), MessageType.Warning);
            }
        }

        public static void ScheduleSave()
        {
            pendingSave = true;
            lastEditTime = EditorApplication.timeSinceStartup;
        }

        public static bool FlushPendingSaveIfIdle(EasyItchPushSettings settings)
        {
            if (!pendingSave)
            {
                return false;
            }

            if (EditorApplication.timeSinceStartup - lastEditTime < SaveDebounceDelaySeconds)
            {
                return false;
            }

            FlushPendingSave(settings);
            return true;
        }

        public static void FlushPendingSave(EasyItchPushSettings settings)
        {
            if (!pendingSave)
            {
                return;
            }

            pendingSave = false;
            settings.SyncVersionToPlayerSettings();
            settings.SaveSettings();
        }

        private static bool DidVersionChange(BuildVersion before, BuildVersion after)
        {
            return before.Major != after.Major ||
                   before.Minor != after.Minor ||
                   before.Patch != after.Patch ||
                   before.IsHotfix != after.IsHotfix ||
                   before.HotfixNumber != after.HotfixNumber;
        }

        private static HashSet<string> GetMacOsProfileNames()
        {
            var profileNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var profileAssets = EasyItchPushBuildProfiles.FindAllProfileAssets();
            for (var i = 0; i < profileAssets.Count; i++)
            {
                var profileAsset = profileAssets[i];
                if (profileAsset == null || string.IsNullOrEmpty(profileAsset.Path))
                {
                    continue;
                }

                var profile = EasyItchPushBuildProfiles.LoadProfile(profileAsset.Path);
                if (profile == null || EasyItchPushBuildProfiles.GetBuildTarget(profile) != BuildTarget.StandaloneOSX)
                {
                    continue;
                }

                profileNames.Add(profile.name);
            }

            return profileNames;
        }
    }
}
