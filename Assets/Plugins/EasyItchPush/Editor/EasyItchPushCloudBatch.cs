using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    /// <summary>Entry point used only by the opt-in macOS GitHub Actions workflow.</summary>
    internal static class EasyItchPushCloudBatch
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            var profileName = ReadArgument(args, "-easyItchProfile");
            var expectedVersion = ReadArgument(args, "-easyItchVersion");

            if (string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(expectedVersion))
                throw new ArgumentException("Cloud build requires -easyItchProfile and -easyItchVersion.");

            var settings = EasyItchPushSettings.Instance;
            settings.AutoSyncVersionWithPlayerSettings();
            if (!string.Equals(settings.ResolvedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "CI project version does not match requested version: " +
                    settings.ResolvedVersion + " != " + expectedVersion);

            // Keep CI archives within the checked-out workspace for upload-artifact.
            settings.outputRoot = "Builds";
            var result = EasyItchPushBuilder.BuildCloudProfile(settings, profileName);
            Debug.Log("Easy Itch Push cloud artifact: " + result.ArchivePath);
        }

        private static string ReadArgument(string[] args, string name)
        {
            var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.Ordinal));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        }
    }
}
