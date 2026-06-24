using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal enum EasyItchPushMode
    {
        Release = 0,
        Test = 1
    }

    [Serializable]
    internal sealed class EasyItchPushProfileMapping
    {
        public bool isEnabled = true;
        public bool releaseIsEnabled = true;
        public bool testIsEnabled = true;
        public bool modeEnabledInitialized;
        public string profileName = string.Empty;
        public string channel = string.Empty;

        public EasyItchPushProfileMapping()
        {
        }

        public EasyItchPushProfileMapping(string profileName, string channel)
        {
            this.profileName = profileName;
            this.channel = channel;
        }

        public void EnsureModeEnabledInitialized()
        {
            if (modeEnabledInitialized)
            {
                return;
            }

            releaseIsEnabled = isEnabled;
            testIsEnabled = isEnabled;
            modeEnabledInitialized = true;
        }

        public bool IsEnabled(EasyItchPushMode pushMode)
        {
            EnsureModeEnabledInitialized();
            return pushMode == EasyItchPushMode.Release ? releaseIsEnabled : testIsEnabled;
        }

        public void SetEnabled(EasyItchPushMode pushMode, bool value)
        {
            EnsureModeEnabledInitialized();
            if (pushMode == EasyItchPushMode.Release)
            {
                releaseIsEnabled = value;
            }
            else
            {
                testIsEnabled = value;
            }

            isEnabled = value;
        }
    }

    [FilePath("ProjectSettings/EasyItchPushSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class EasyItchPushSettings : ScriptableSingleton<EasyItchPushSettings>
    {
        private sealed class UnityObjectReferenceComparer : IEqualityComparer<UnityEngine.Object>
        {
            public static readonly UnityObjectReferenceComparer Instance = new UnityObjectReferenceComparer();

            public bool Equals(UnityEngine.Object x, UnityEngine.Object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(UnityEngine.Object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private const string EditorPrefsKeyPrefix = "EasyItchPush.";
        private const string LegacyItchUsernameKey = "itchUsername";
        private const string LegacyGameSlugKey = "gameSlug";
        private const string LegacyProjectIdKey = "projectId";
        private const string LegacyButlerExecutablePathKey = "butlerExecutablePath";
        private const string GameAnalyticsSettingsAssetPath = "Assets/Resources/GameAnalytics/Settings.asset";
        private const string GameAnalyticsBuildPropertyName = "Build";
        private const string PrivateLegacyItchUsernameKey = EditorPrefsKeyPrefix + "itchUsername";
        private const string PrivateLegacyGameSlugKey = EditorPrefsKeyPrefix + "gameSlug";
        private const string PrivateLegacyProjectIdKey = EditorPrefsKeyPrefix + "projectId";
        private const string PrivateReleaseItchUsernameKey = EditorPrefsKeyPrefix + "release.itchUsername";
        private const string PrivateReleaseGameSlugKey = EditorPrefsKeyPrefix + "release.gameSlug";
        private const string PrivateReleaseProjectIdKey = EditorPrefsKeyPrefix + "release.projectId";
        private const string PrivateTestItchUsernameKey = EditorPrefsKeyPrefix + "test.itchUsername";
        private const string PrivateTestGameSlugKey = EditorPrefsKeyPrefix + "test.gameSlug";
        private const string PrivateTestProjectIdKey = EditorPrefsKeyPrefix + "test.projectId";
        private const string PrivateButlerExecutablePathKey = EditorPrefsKeyPrefix + "butlerExecutablePath";

        [SerializeField, HideInInspector] private string itchUsername = string.Empty;
        [SerializeField, HideInInspector] private string gameSlug = string.Empty;
        [SerializeField, HideInInspector] private string projectId = string.Empty;
        [SerializeField, HideInInspector] private string butlerExecutablePath = string.Empty;

        public EasyItchPushMode pushMode;
        public int versionMajor = 1;
        public int versionMinor;
        public int versionPatch = 1;
        public bool versionIsHotfix;
        public int versionHotfixNumber = 1;
        public bool versionInitialized;
        public string lastSyncedVersion = string.Empty;
        public int lastSyncedAndroidVersionCode = -1;

        public string outputRoot = "../Builds";
        public bool developmentBuild;
        public bool allowDebugging;
        public bool strictMode = true;
        public bool detailedBuildReport = true;
        public bool cleanBuildDirectory = true;
        public bool compressWithLz4HC;
        public bool applyReleaseObfuscation = true;
        public bool forceIl2CppForRelease = true;
        public ManagedStrippingLevel releaseManagedStrippingLevel = ManagedStrippingLevel.High;

        public bool openGamePageOnSuccess;
        public bool playSoundOnComplete;
        public EasyItchPushProfileMapping[] profileChannelMappings =
        {
            new EasyItchPushProfileMapping("Windows", "windows"),
            new EasyItchPushProfileMapping("Web", "html5")
        };

        public static EasyItchPushSettings Instance => instance;

        public EasyItchPushMode PushMode
        {
            get => pushMode;
            set => pushMode = value;
        }

        public string ItchUsername
        {
            get => GetItchUsername(PushMode);
            set => SetItchUsername(PushMode, value);
        }

        public string GameSlug
        {
            get => GetGameSlug(PushMode);
            set => SetGameSlug(PushMode, value);
        }

        public string ProjectId
        {
            get => GetProjectId(PushMode);
            set => SetProjectId(PushMode, value);
        }

        public string ButlerExecutablePath
        {
            get => EditorPrefs.GetString(PrivateButlerExecutablePathKey, string.Empty);
            set => EditorPrefs.SetString(PrivateButlerExecutablePathKey, value?.Trim() ?? string.Empty);
        }

        public BuildVersion CurrentVersion
        {
            get
            {
                EnsureVersionInitialized();
                return new BuildVersion(versionMajor, versionMinor, versionPatch, versionIsHotfix, versionHotfixNumber);
            }
        }

        public string ResolvedVersion
        {
            get => CurrentVersion.ToStringWithoutPrefix();
        }

        public string ResolvedVersionWithPrefix => CurrentVersion.ToStringWithPrefix();

        public string ResolvedOutputRoot => string.IsNullOrWhiteSpace(outputRoot) ? "../Builds" : outputRoot.Trim();

        public string ItchGameTarget => GetItchGameTarget(PushMode);

        public string GamePageUrl => GetGamePageUrl(PushMode);

        public bool HasVersionSnapshotChanged()
        {
            EnsureVersionInitialized();

            var pluginVersion = CurrentVersion;
            var pluginVersionText = pluginVersion.ToStringWithoutPrefix();
            var pluginVersionCode = pluginVersion.ToVersionCode();
            var hasPlayerVersion = BuildVersion.TryParse(PlayerSettings.bundleVersion, out var playerVersion);
            var playerVersionText = hasPlayerVersion ? playerVersion.ToStringWithoutPrefix() : string.Empty;
            var playerVersionCode = PlayerSettings.Android.bundleVersionCode;

            var pluginMatchesSnapshot = string.Equals(pluginVersionText, lastSyncedVersion, StringComparison.OrdinalIgnoreCase) &&
                                       pluginVersionCode == lastSyncedAndroidVersionCode;
            var playerMatchesSnapshot = hasPlayerVersion &&
                                       string.Equals(playerVersionText, lastSyncedVersion, StringComparison.OrdinalIgnoreCase) &&
                                       playerVersionCode == lastSyncedAndroidVersionCode;

            return !pluginMatchesSnapshot || !playerMatchesSnapshot;
        }

        public void SaveSettings()
        {
            MigrateLegacyPrivateSettings();
            EnsureVersionInitialized();
            NormalizeLegacyValues();
            SyncProfileMappingsWithBuildProfiles();
            Save(true);
        }

        public void AutoSyncVersionWithPlayerSettings()
        {
            MigrateLegacyPrivateSettings();
            EnsureVersionInitialized();

            var pluginVersion = CurrentVersion;
            var pluginVersionText = pluginVersion.ToStringWithoutPrefix();
            var pluginVersionCode = pluginVersion.ToVersionCode();
            var hasPlayerVersion = BuildVersion.TryParse(PlayerSettings.bundleVersion, out var playerVersion);
            var playerVersionText = hasPlayerVersion ? playerVersion.ToStringWithoutPrefix() : string.Empty;
            var playerVersionCode = PlayerSettings.Android.bundleVersionCode;

            var pluginMatchesSnapshot = string.Equals(pluginVersionText, lastSyncedVersion, StringComparison.OrdinalIgnoreCase) &&
                                       pluginVersionCode == lastSyncedAndroidVersionCode;
            var playerMatchesSnapshot = hasPlayerVersion &&
                                       string.Equals(playerVersionText, lastSyncedVersion, StringComparison.OrdinalIgnoreCase) &&
                                       playerVersionCode == lastSyncedAndroidVersionCode;

            if (hasPlayerVersion &&
                string.Equals(pluginVersionText, playerVersionText, StringComparison.OrdinalIgnoreCase) &&
                pluginVersionCode == playerVersionCode)
            {
                var needsSave = !string.Equals(lastSyncedVersion, pluginVersionText, StringComparison.OrdinalIgnoreCase) ||
                                lastSyncedAndroidVersionCode != pluginVersionCode;
                MarkVersionAsSynchronized(pluginVersion);
                if (needsSave)
                {
                    Save(true);
                }

                return;
            }

            if (pluginMatchesSnapshot && hasPlayerVersion && !playerMatchesSnapshot)
            {
                SetVersion(playerVersion);
                MarkVersionAsSynchronized(playerVersion);
                Save(true);
                EasyItchPushLog.Info($"Loaded version from PlayerSettings: {playerVersionText}");
                return;
            }

            if (playerMatchesSnapshot && !pluginMatchesSnapshot)
            {
                SyncVersionToPlayerSettings();
                Save(true);
                EasyItchPushLog.Info($"Pushed plugin version into PlayerSettings: {pluginVersionText}");
                return;
            }

            if (!hasPlayerVersion)
            {
                SyncVersionToPlayerSettings();
                Save(true);
                EasyItchPushLog.Warning("PlayerSettings.bundleVersion was invalid. Restored it from Easy Itch Push settings.");
                return;
            }

            SyncVersionToPlayerSettings();
            Save(true);
            EasyItchPushLog.Warning(
                $"Version conflict between Easy Itch Push ({pluginVersionText}) and PlayerSettings ({playerVersionText}). Kept plugin value.");
        }

        public string GetItchUsername(EasyItchPushMode pushMode)
        {
            return GetEditorPrefString(
                GetItchUsernameKey(pushMode),
                pushMode == EasyItchPushMode.Release ? PrivateLegacyItchUsernameKey : null);
        }

        public void SetItchUsername(EasyItchPushMode pushMode, string value)
        {
            SetEditorPrefStringIfChanged(GetItchUsernameKey(pushMode), value);
        }

        public string GetGameSlug(EasyItchPushMode pushMode)
        {
            return GetEditorPrefString(
                GetGameSlugKey(pushMode),
                pushMode == EasyItchPushMode.Release ? PrivateLegacyGameSlugKey : null);
        }

        public void SetGameSlug(EasyItchPushMode pushMode, string value)
        {
            SetEditorPrefStringIfChanged(GetGameSlugKey(pushMode), value);
        }

        public string GetProjectId(EasyItchPushMode pushMode)
        {
            return GetEditorPrefString(
                GetProjectIdKey(pushMode),
                pushMode == EasyItchPushMode.Release ? PrivateLegacyProjectIdKey : null);
        }

        public void SetProjectId(EasyItchPushMode pushMode, string value)
        {
            SetEditorPrefStringIfChanged(GetProjectIdKey(pushMode), value);
        }

        public string GetPushModeLabel(EasyItchPushMode pushMode)
        {
            return pushMode == EasyItchPushMode.Release ? "Release" : "Test";
        }

        public bool HasItchTarget()
        {
            return HasItchTarget(PushMode);
        }

        public bool HasItchTarget(EasyItchPushMode pushMode)
        {
            return !string.IsNullOrWhiteSpace(GetItchUsername(pushMode)) &&
                   !string.IsNullOrWhiteSpace(GetGameSlug(pushMode));
        }

        public bool ValidatePushSettings(string channelName, string version, out string message)
        {
            return ValidatePushSettings(PushMode, channelName, version, out message);
        }

        public bool ValidatePushSettings(EasyItchPushMode pushMode, string channelName, string version, out string message)
        {
            var missing = new List<string>();

            if (string.IsNullOrWhiteSpace(GetItchUsername(pushMode)))
            {
                missing.Add("Username");
            }

            if (string.IsNullOrWhiteSpace(GetGameSlug(pushMode)))
            {
                missing.Add("Game Slug");
            }

            if (string.IsNullOrWhiteSpace(GetProjectId(pushMode)))
            {
                missing.Add("Project ID");
            }

            if (string.IsNullOrWhiteSpace(channelName))
            {
                missing.Add("Channel");
            }

            if (string.IsNullOrWhiteSpace(version) || !BuildVersion.TryParse(version, out _))
            {
                missing.Add("Version");
            }

            if (missing.Count == 0)
            {
                message = string.Empty;
                return true;
            }

            message = $"Fill required {GetPushModeLabel(pushMode)} itch.io publishing fields before pushing:\n\n- " + string.Join("\n- ", missing);
            return false;
        }

        public string GetOutputRootDirectory()
        {
            NormalizeLegacyValues();
            return Path.GetFullPath(ResolvedOutputRoot);
        }

        public string GetBuildDirectory(string baseChannelName)
        {
            return Path.Combine(GetOutputRootDirectory(), SanitizeItchChannel(baseChannelName));
        }

        public string GetLatestBuildDirectory(string baseChannelName)
        {
            return Path.GetFullPath(Path.Combine(GetBuildDirectory(baseChannelName), "latest"));
        }

        public string GetItchTarget(string channelName)
        {
            return GetItchTarget(PushMode, channelName);
        }

        public string GetItchTarget(EasyItchPushMode pushMode, string channelName)
        {
            return $"{GetItchUsername(pushMode).Trim()}/{GetGameSlug(pushMode).Trim()}:{SanitizeItchChannel(channelName)}";
        }

        public string GetItchGameTarget(EasyItchPushMode pushMode)
        {
            return $"{GetItchUsername(pushMode).Trim()}/{GetGameSlug(pushMode).Trim()}";
        }

        public string GetGamePageUrl(EasyItchPushMode pushMode)
        {
            if (string.IsNullOrWhiteSpace(GetItchUsername(pushMode)) || string.IsNullOrWhiteSpace(GetGameSlug(pushMode)))
            {
                return string.Empty;
            }

            return $"https://{GetItchUsername(pushMode).Trim()}.itch.io/{GetGameSlug(pushMode).Trim()}";
        }

        public string GetRemoteChannel(string baseChannelName)
        {
            return GetRemoteChannel(baseChannelName, PushMode);
        }

        public string GetRemoteChannel(string baseChannelName, EasyItchPushMode pushMode)
        {
            var sanitizedBaseChannel = SanitizeItchChannel(baseChannelName);
            if (pushMode == EasyItchPushMode.Test)
            {
                return sanitizedBaseChannel;
            }

            return $"{sanitizedBaseChannel}-{CurrentVersion.ToReleaseChannelSuffix()}";
        }

        public string GetChannelForProfile(string profileName)
        {
            SyncProfileMappingsWithBuildProfiles();
            return GetChannelForProfileNoSync(profileName);
        }

        public string GetChannelForProfileNoSync(string profileName)
        {
            if (profileChannelMappings != null)
            {
                foreach (var mapping in profileChannelMappings)
                {
                    if (mapping == null)
                    {
                        continue;
                    }

                    if (string.Equals(mapping.profileName, profileName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(mapping.channel))
                    {
                        return SanitizeItchChannel(mapping.channel);
                    }
                }
            }

            return GetDefaultChannelForProfile(profileName);
        }

        public bool IsProfileEnabled(string profileName)
        {
            SyncProfileMappingsWithBuildProfiles();
            return IsProfileEnabledNoSync(profileName);
        }

        public bool IsProfileEnabled(EasyItchPushMode pushMode, string profileName)
        {
            SyncProfileMappingsWithBuildProfiles();
            return IsProfileEnabledNoSync(pushMode, profileName);
        }

        public bool IsProfileEnabledNoSync(string profileName)
        {
            return IsProfileEnabledNoSync(PushMode, profileName);
        }

        public bool IsProfileEnabledNoSync(EasyItchPushMode pushMode, string profileName)
        {
            if (profileChannelMappings != null)
            {
                foreach (var mapping in profileChannelMappings)
                {
                    if (mapping == null)
                    {
                        continue;
                    }

                    if (string.Equals(mapping.profileName, profileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return mapping.IsEnabled(pushMode);
                    }
                }
            }

            return false;
        }

        public void SyncProfileMappingsWithBuildProfiles()
        {
            var profiles = EasyItchPushBuildProfiles.FindAllProfileAssets();
            var profileNames = new HashSet<string>(
                profiles
                    .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    .Select(profile => profile.Name),
                StringComparer.OrdinalIgnoreCase);
            var existingMappings = new Dictionary<string, EasyItchPushProfileMapping>(StringComparer.OrdinalIgnoreCase);

            if (profileChannelMappings != null)
            {
                foreach (var mapping in profileChannelMappings)
                {
                    if (mapping == null || string.IsNullOrWhiteSpace(mapping.profileName))
                    {
                        continue;
                    }

                    mapping.EnsureModeEnabledInitialized();
                    existingMappings[mapping.profileName] = mapping;
                }
            }

            var orderedMappings = new List<EasyItchPushProfileMapping>(profiles.Count);
            var consumedProfileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (profileChannelMappings != null)
            {
                foreach (var existingMapping in profileChannelMappings)
                {
                    if (existingMapping == null ||
                        string.IsNullOrWhiteSpace(existingMapping.profileName) ||
                        !profileNames.Contains(existingMapping.profileName) ||
                        !consumedProfileNames.Add(existingMapping.profileName))
                    {
                        continue;
                    }

                    existingMapping.EnsureModeEnabledInitialized();
                    if (string.IsNullOrWhiteSpace(existingMapping.channel))
                    {
                        existingMapping.channel = GetDefaultChannelForProfile(existingMapping.profileName);
                    }

                    orderedMappings.Add(existingMapping);
                }
            }

            foreach (var profile in profiles)
            {
                if (profile == null ||
                    string.IsNullOrWhiteSpace(profile.Name) ||
                    consumedProfileNames.Contains(profile.Name))
                {
                    continue;
                }

                var profileName = profile.Name;
                if (!existingMappings.TryGetValue(profileName, out var mapping) || mapping == null)
                {
                    mapping = new EasyItchPushProfileMapping(profileName, GetDefaultChannelForProfile(profileName));
                }

                mapping.profileName = profileName;
                mapping.EnsureModeEnabledInitialized();
                if (string.IsNullOrWhiteSpace(mapping.channel))
                {
                    mapping.channel = GetDefaultChannelForProfile(profileName);
                }

                orderedMappings.Add(mapping);
                consumedProfileNames.Add(profileName);
            }

            profileChannelMappings = orderedMappings.ToArray();
        }

        public bool MoveProfileMapping(int fromIndex, int toIndex)
        {
            if (profileChannelMappings == null ||
                fromIndex < 0 ||
                toIndex < 0 ||
                fromIndex >= profileChannelMappings.Length ||
                toIndex >= profileChannelMappings.Length ||
                fromIndex == toIndex)
            {
                return false;
            }

            var mappings = new List<EasyItchPushProfileMapping>(profileChannelMappings);
            var mapping = mappings[fromIndex];
            mappings.RemoveAt(fromIndex);
            mappings.Insert(toIndex, mapping);
            profileChannelMappings = mappings.ToArray();
            return true;
        }

        public void LoadVersionFromPlayerSettings()
        {
            if (!BuildVersion.TryParse(PlayerSettings.bundleVersion, out var parsed))
            {
                return;
            }

            SetVersion(parsed);
            SaveSettings();
        }

        public void SyncVersionToActivePlayerSettingsOnly()
        {
            EnsureVersionInitialized();

            var version = CurrentVersion;
            var versionText = version.ToStringWithoutPrefix();
            var versionCode = version.ToVersionCode();

            PlayerSettings.bundleVersion = versionText;
            PlayerSettings.Android.bundleVersionCode = versionCode;
            SyncGameAnalyticsBuildVersion(version);
            MarkVersionAsSynchronized(version);

            EasyItchPushLog.Info(
                $"Synchronized active PlayerSettings: bundleVersion={PlayerSettings.bundleVersion}, " +
                $"Android.bundleVersionCode={PlayerSettings.Android.bundleVersionCode}, GameAnalytics.Build={versionText}");
        }

        public void SyncVersionToPlayerSettings()
        {
            EnsureVersionInitialized();

            var version = CurrentVersion;
            var versionText = version.ToStringWithoutPrefix();

            SyncVersionToActivePlayerSettingsOnly();
            var synchronizedProfiles = SyncVersionToBuildProfilePlayerSettings(version);

            EasyItchPushLog.Info(
                $"Synchronized version to global/active PlayerSettings and {synchronizedProfiles} Unity Build Profile asset(s): {versionText}");
        }

        private static int SyncVersionToBuildProfilePlayerSettings(BuildVersion version)
        {
#if UNITY_6000_0_OR_NEWER
            var versionText = version.ToStringWithoutPrefix();
            var versionCode = version.ToVersionCode();
            var synchronizedProfiles = 0;
            var profileAssets = EasyItchPushBuildProfiles.FindAllProfileAssets();

            foreach (var profileAsset in profileAssets)
            {
                if (profileAsset == null || string.IsNullOrWhiteSpace(profileAsset.Path))
                {
                    continue;
                }

                var profile = EasyItchPushBuildProfiles.LoadProfile(profileAsset.Path);
                if (profile == null)
                {
                    continue;
                }

                if (TrySyncVersionToBuildProfile(profile, versionText, versionCode, out var changedProperties))
                {
                    synchronizedProfiles++;
                    EasyItchPushLog.Info(
                        $"Synchronized Build Profile '{profile.name}': bundleVersion={versionText}, " +
                        $"Android.bundleVersionCode={versionCode}" +
                        (string.IsNullOrWhiteSpace(changedProperties) ? string.Empty : $" ({changedProperties})"));
                }
                else
                {
                    EasyItchPushLog.Warning(
                        $"Build Profile '{profile.name}' did not expose serialized bundleVersion/androidBundleVersionCode overrides. " +
                        "The global PlayerSettings version was still synchronized. If this profile uses its own Player Settings, " +
                        "open the profile once and check that version override fields exist in the Build Profile asset.");
                }
            }

            if (synchronizedProfiles > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return synchronizedProfiles;
#else
            return 0;
#endif
        }

#if UNITY_6000_0_OR_NEWER
        private static bool TrySyncVersionToBuildProfile(
            BuildProfile profile,
            string versionText,
            int versionCode,
            out string changedProperties)
        {
            changedProperties = string.Empty;
            if (profile == null)
            {
                return false;
            }

            var rootAssetPath = AssetDatabase.GetAssetPath(profile);
            var visited = new HashSet<UnityEngine.Object>(UnityObjectReferenceComparer.Instance);
            var queue = new Queue<UnityEngine.Object>();
            var changedPaths = new List<string>();
            var changed = false;

            queue.Enqueue(profile);

            while (queue.Count > 0)
            {
                var targetObject = queue.Dequeue();
                if (targetObject == null || !visited.Add(targetObject))
                {
                    continue;
                }

                if (TrySyncVersionToSerializedObject(
                        targetObject,
                        rootAssetPath,
                        versionText,
                        versionCode,
                        queue,
                        changedPaths))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            changedProperties = string.Join(", ", changedPaths.Distinct());
            return changedPaths.Count > 0;
        }

        private static bool TrySyncVersionToSerializedObject(
            UnityEngine.Object targetObject,
            string rootAssetPath,
            string versionText,
            int versionCode,
            Queue<UnityEngine.Object> referencedObjectsToScan,
            List<string> changedPaths)
        {
            var serializedObject = new SerializedObject(targetObject);
            var changed = false;
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            var objectLabel = $"{targetObject.GetType().Name}:{targetObject.name}";

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyType == SerializedPropertyType.String && IsBundleVersionProperty(iterator))
                {
                    if (!string.Equals(iterator.stringValue, versionText, StringComparison.Ordinal))
                    {
                        iterator.stringValue = versionText;
                        changed = true;
                    }

                    changedPaths.Add($"{objectLabel}.{iterator.propertyPath}");
                    continue;
                }

                if (iterator.propertyType == SerializedPropertyType.Integer && IsAndroidBundleVersionCodeProperty(iterator))
                {
                    if (iterator.intValue != versionCode)
                    {
                        iterator.intValue = versionCode;
                        changed = true;
                    }

                    changedPaths.Add($"{objectLabel}.{iterator.propertyPath}");
                    continue;
                }

                if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                    iterator.objectReferenceValue != null &&
                    ShouldScanReferencedProfileObject(iterator.objectReferenceValue, rootAssetPath))
                {
                    referencedObjectsToScan.Enqueue(iterator.objectReferenceValue);
                }
            }

            if (changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(targetObject);
                AssetDatabase.SaveAssetIfDirty(targetObject);
            }

            return changed;
        }

        private static bool ShouldScanReferencedProfileObject(UnityEngine.Object referencedObject, string rootAssetPath)
        {
            if (referencedObject == null)
            {
                return false;
            }

            // Scan sub-assets embedded in the same Build Profile asset. This catches Unity 6 profile-specific
            // Player Settings objects without touching unrelated project assets.
            var referencedPath = AssetDatabase.GetAssetPath(referencedObject);
            if (!string.IsNullOrEmpty(rootAssetPath) &&
                string.Equals(referencedPath, rootAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var typeName = referencedObject.GetType().FullName ?? referencedObject.GetType().Name;
            return typeName.IndexOf("BuildProfile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("PlayerSettings", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBundleVersionProperty(SerializedProperty property)
        {
            var name = property.name ?? string.Empty;
            var path = property.propertyPath ?? string.Empty;

            if (EqualsAny(name,
                    "bundleVersion",
                    "m_BundleVersion",
                    "applicationVersion",
                    "m_ApplicationVersion",
                    "playerVersion",
                    "m_PlayerVersion"))
            {
                return true;
            }

            var normalizedPath = path.Replace("_", string.Empty).Replace(".", string.Empty).ToLowerInvariant();
            return normalizedPath.Contains("bundleversion") &&
                   !normalizedPath.Contains("code") &&
                   !normalizedPath.Contains("android");
        }

        private static bool IsAndroidBundleVersionCodeProperty(SerializedProperty property)
        {
            var name = property.name ?? string.Empty;
            var path = property.propertyPath ?? string.Empty;

            if (EqualsAny(name,
                    "bundleVersionCode",
                    "androidBundleVersionCode",
                    "m_BundleVersionCode",
                    "m_AndroidBundleVersionCode"))
            {
                return true;
            }

            var normalizedPath = path.Replace("_", string.Empty).Replace(".", string.Empty).ToLowerInvariant();
            return normalizedPath.Contains("android") &&
                   normalizedPath.Contains("bundleversioncode");
        }

        private static bool EqualsAny(string value, params string[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(value, candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
#endif

        public void ApplyReleasePlayerSettings(BuildTarget target)
        {
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            var namedTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);

            try
            {
                var scriptingBackendLabel = "Build Profile";

                if (RequiresMonoFallbackForMacOsBuild(target))
                {
                    PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.Mono2x);
                    scriptingBackendLabel = "Mono";
                    EasyItchPushLog.Warning(
                        "macOS build profile is running on a non-macOS editor. The build will be compiled with Mono because IL2CPP requires a macOS host.");
                }
                else if (forceIl2CppForRelease && SupportsIl2Cpp(target))
                {
                    if (UsesSharedStandaloneScriptingBackend(target))
                    {
                        EasyItchPushLog.Info(
                            $"Skipped forcing IL2CPP for {target}. Desktop standalone targets share one scripting backend, so Easy Itch Push keeps the Build Profile backend to avoid leaking IL2CPP into other standalone profiles.");
                    }
                    else
                    {
                        PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
                        scriptingBackendLabel = "IL2CPP";
                    }
                }

                if (!applyReleaseObfuscation)
                {
                    EasyItchPushLog.Info($"Applied release scripting backend for {target}: backend={scriptingBackendLabel}");
                    return;
                }

                PlayerSettings.SetManagedStrippingLevel(namedTarget, releaseManagedStrippingLevel);
                PlayerSettings.stripEngineCode = true;
                EasyItchPushLog.Info(
                    $"Applied release protection for {target}: backend={scriptingBackendLabel}, stripping={releaseManagedStrippingLevel}, stripEngineCode=true");
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not apply all release protection settings for {target}: {ex.Message}");
            }
        }

        private static bool RequiresMonoFallbackForMacOsBuild(BuildTarget target)
        {
            return target == BuildTarget.StandaloneOSX
                && Application.platform != RuntimePlatform.OSXEditor;
        }

        private static bool UsesSharedStandaloneScriptingBackend(BuildTarget target)
        {
            return BuildPipeline.GetBuildTargetGroup(target) == BuildTargetGroup.Standalone;
        }

        private static bool SupportsIl2Cpp(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.WebGL:
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.Android:
                case BuildTarget.iOS:
                    return true;
                default:
                    return false;
            }
        }

        private static void SyncGameAnalyticsBuildVersion(BuildVersion version)
        {
            var settingsAsset = AssetDatabase.LoadMainAssetAtPath(GameAnalyticsSettingsAssetPath);
            if (settingsAsset == null)
            {
                EasyItchPushLog.Warning($"GameAnalytics settings asset was not found at {GameAnalyticsSettingsAssetPath}.");
                return;
            }

            var serializedObject = new SerializedObject(settingsAsset);
            var buildProperty = serializedObject.FindProperty(GameAnalyticsBuildPropertyName);
            if (buildProperty == null || !buildProperty.isArray)
            {
                EasyItchPushLog.Warning(
                    $"GameAnalytics settings asset does not expose an array property named {GameAnalyticsBuildPropertyName}.");
                return;
            }

            var versionText = version.ToStringWithoutPrefix();
            var hasChanges = false;
            var targetArraySize = buildProperty.arraySize > 0 ? buildProperty.arraySize : 1;
            if (buildProperty.arraySize != targetArraySize)
            {
                buildProperty.arraySize = targetArraySize;
                hasChanges = true;
            }

            for (var i = 0; i < buildProperty.arraySize; i++)
            {
                var element = buildProperty.GetArrayElementAtIndex(i);
                if (element.stringValue == versionText)
                {
                    continue;
                }

                element.stringValue = versionText;
                hasChanges = true;
            }

            if (!hasChanges)
            {
                return;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settingsAsset);
            AssetDatabase.SaveAssetIfDirty(settingsAsset);
        }

        public void EnsureVersionInitialized()
        {
            if (versionInitialized)
            {
                return;
            }

            if (BuildVersion.TryParse(PlayerSettings.bundleVersion, out var parsed))
            {
                SetVersion(parsed);
            }
            else
            {
                SetVersion(BuildVersion.Default);
            }
        }

        private void SetVersion(BuildVersion version)
        {
            versionMajor = version.Major;
            versionMinor = version.Minor;
            versionPatch = version.Patch;
            versionIsHotfix = version.IsHotfix;
            versionHotfixNumber = version.HotfixNumber;
            versionInitialized = true;
        }

        private void MarkVersionAsSynchronized(BuildVersion version)
        {
            lastSyncedVersion = version.ToStringWithoutPrefix();
            lastSyncedAndroidVersionCode = version.ToVersionCode();
        }

        private void NormalizeLegacyValues()
        {
            if (!Enum.IsDefined(typeof(EasyItchPushMode), pushMode))
            {
                pushMode = EasyItchPushMode.Release;
            }

            if (string.Equals(outputRoot?.Trim(), "Builds", StringComparison.OrdinalIgnoreCase))
            {
                outputRoot = "../Builds";
            }
        }

        private void MigrateLegacyPrivateSettings()
        {
            MigrateLegacyString(LegacyItchUsernameKey, PrivateReleaseItchUsernameKey);
            MigrateLegacyString(LegacyGameSlugKey, PrivateReleaseGameSlugKey);
            MigrateLegacyString(LegacyProjectIdKey, PrivateReleaseProjectIdKey);
            MigrateLegacyString(LegacyButlerExecutablePathKey, PrivateButlerExecutablePathKey);

            MigrateEditorPrefsString(PrivateLegacyItchUsernameKey, PrivateReleaseItchUsernameKey);
            MigrateEditorPrefsString(PrivateLegacyGameSlugKey, PrivateReleaseGameSlugKey);
            MigrateEditorPrefsString(PrivateLegacyProjectIdKey, PrivateReleaseProjectIdKey);
        }

        private void MigrateLegacyString(string legacyFieldName, string editorPrefsKey)
        {
            var serializedObject = new SerializedObject(this);
            var property = serializedObject.FindProperty(legacyFieldName);
            if (property == null || property.propertyType != SerializedPropertyType.String)
            {
                return;
            }

            var legacyValue = property.stringValue?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(legacyValue) && string.IsNullOrEmpty(EditorPrefs.GetString(editorPrefsKey, string.Empty)))
            {
                EditorPrefs.SetString(editorPrefsKey, legacyValue);
            }

            if (property.stringValue != string.Empty)
            {
                property.stringValue = string.Empty;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void MigrateEditorPrefsString(string sourceKey, string targetKey)
        {
            var sourceValue = EditorPrefs.GetString(sourceKey, string.Empty);
            if (string.IsNullOrEmpty(sourceValue))
            {
                return;
            }

            if (string.IsNullOrEmpty(EditorPrefs.GetString(targetKey, string.Empty)))
            {
                EditorPrefs.SetString(targetKey, sourceValue);
            }

            EditorPrefs.DeleteKey(sourceKey);
        }

        public BuildOptions GetBuildOptions(bool forceRelease = false)
        {
            var options = BuildOptions.None;
            var shouldUseReleaseDefaults = forceRelease || applyReleaseObfuscation;
            var useDevelopmentBuild = shouldUseReleaseDefaults ? false : developmentBuild;
            var useAllowDebugging = shouldUseReleaseDefaults ? false : allowDebugging;
            var useStrictMode = shouldUseReleaseDefaults || strictMode;
            var useLz4HC = shouldUseReleaseDefaults || compressWithLz4HC;

            if (useDevelopmentBuild)
            {
                options |= BuildOptions.Development;
            }

            if (useAllowDebugging)
            {
                options |= BuildOptions.AllowDebugging;
            }

            if (useStrictMode)
            {
                options |= BuildOptions.StrictMode;
            }

            if (detailedBuildReport)
            {
                options |= BuildOptions.DetailedBuildReport;
            }

            if (useLz4HC)
            {
                options |= BuildOptions.CompressWithLz4HC;
            }

            return options;
        }

        public static string SanitizeItchChannel(string value)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim().ToLowerInvariant();
            var chars = raw.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                chars[i] = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-';
            }

            var sanitized = new string(chars).Trim('-', '.', '_');
            return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
        }

        private static string GetDefaultChannelForProfile(string profileName)
        {
            if (string.Equals(profileName, "Web", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profileName, "WebGL", StringComparison.OrdinalIgnoreCase))
            {
                return "html5";
            }

            if (string.Equals(profileName, "Windows", StringComparison.OrdinalIgnoreCase))
            {
                return "windows";
            }

            return SanitizeItchChannel(profileName);
        }

        private static string GetItchUsernameKey(EasyItchPushMode pushMode)
        {
            return pushMode == EasyItchPushMode.Release ? PrivateReleaseItchUsernameKey : PrivateTestItchUsernameKey;
        }

        private static string GetGameSlugKey(EasyItchPushMode pushMode)
        {
            return pushMode == EasyItchPushMode.Release ? PrivateReleaseGameSlugKey : PrivateTestGameSlugKey;
        }

        private static string GetProjectIdKey(EasyItchPushMode pushMode)
        {
            return pushMode == EasyItchPushMode.Release ? PrivateReleaseProjectIdKey : PrivateTestProjectIdKey;
        }

        private static string GetEditorPrefString(string primaryKey, string fallbackKey)
        {
            var value = EditorPrefs.GetString(primaryKey, string.Empty);
            if (!string.IsNullOrEmpty(value) || string.IsNullOrEmpty(fallbackKey))
            {
                return value;
            }

            return EditorPrefs.GetString(fallbackKey, string.Empty);
        }

        private static void SetEditorPrefStringIfChanged(string key, string value)
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (string.Equals(EditorPrefs.GetString(key, string.Empty), normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            EditorPrefs.SetString(key, normalizedValue);
        }
    }
}
