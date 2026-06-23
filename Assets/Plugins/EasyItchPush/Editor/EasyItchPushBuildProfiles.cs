using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal sealed class EasyItchPushBuildProfileAsset
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
    }

    internal static class EasyItchPushBuildProfiles
    {
        public const string BuildProfilesDirectory = "Assets/Settings/Build Profiles";
        public const string WindowsProfilePath = BuildProfilesDirectory + "/Windows.asset";
        public const string WebProfilePath = BuildProfilesDirectory + "/Web.asset";

        public static void EnsureDefaultBuildProfiles()
        {
            EnsureBuildProfilesDirectory();
            var windows = EnsureProfile("Windows", WindowsProfilePath, BuildTarget.StandaloneWindows64, 2);
            var web = EnsureProfile("Web", WebProfilePath, BuildTarget.WebGL, 0);

            EditorUtility.SetDirty(windows);
            EditorUtility.SetDirty(web);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EasyItchPushLog.Info("Ensured default Build Profiles: Windows and Web.");
        }

        public static List<EasyItchPushBuildProfileAsset> FindAllProfileAssets()
        {
            return AssetDatabase.FindAssets("t:BuildProfile")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => new
                {
                    Path = path,
                    Profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path)
                })
                .Where(item => item.Profile != null)
                .OrderBy(item => GetProfileSortKey(item.Profile))
                .ThenBy(item => item.Profile.name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new EasyItchPushBuildProfileAsset
                {
                    Path = item.Path,
                    Name = item.Profile.name
                })
                .ToList();
        }

        public static BuildProfile LoadProfile(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath);
        }

        public static BuildTarget GetBuildTarget(BuildProfile profile)
        {
            var serializedObject = new SerializedObject(profile);
            var targetProperty = serializedObject.FindProperty("m_BuildTarget");
            if (targetProperty != null)
            {
                return (BuildTarget)targetProperty.intValue;
            }

            return BuildTarget.NoTarget;
        }

        public static bool IsWebProfile(BuildProfile profile)
        {
            return GetBuildTarget(profile) == BuildTarget.WebGL ||
                   string.Equals(profile.name, "Web", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(profile.name, "WebGL", StringComparison.OrdinalIgnoreCase);
        }

        private static BuildProfile EnsureProfile(string profileName, string assetPath, BuildTarget target, int subtarget)
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<BuildProfile>();
                profile.name = profileName;
                AssetDatabase.CreateAsset(profile, assetPath);
            }

            profile.name = profileName;
            ConfigureProfile(profile, target, subtarget);
            return profile;
        }

        private static void ConfigureProfile(BuildProfile profile, BuildTarget target, int subtarget)
        {
            var serializedObject = new SerializedObject(profile);
            SetInt(serializedObject, "m_BuildTarget", (int)target);
            SetInt(serializedObject, "m_Subtarget", subtarget);
            SetBool(serializedObject, "m_OverrideGlobalSceneList", false);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureBuildProfilesDirectory()
        {
            EnsureAssetFolder("Assets", "Settings");
            EnsureAssetFolder("Assets/Settings", "Build Profiles");
            AssetDatabase.Refresh();
        }

        private static void EnsureAssetFolder(string parentFolder, string folderName)
        {
            var assetPath = parentFolder + "/" + folderName;
            var fullPath = Path.GetFullPath(assetPath);

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static int GetProfileSortKey(BuildProfile profile)
        {
            if (string.Equals(profile.name, "Windows", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(profile.name, "Web", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.name, "WebGL", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }
    }
}
