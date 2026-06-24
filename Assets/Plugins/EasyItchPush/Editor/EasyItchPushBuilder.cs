using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace EasyItchPush.Editor
{
    internal sealed class EasyItchPushBuildResult
    {
        public string ProfileName = string.Empty;
        public string Channel = string.Empty;
        public string OutputPath = string.Empty;
        public string ArchivePath = string.Empty;
        public BuildResult Result = BuildResult.Unknown;
        public TimeSpan Duration;
        public ulong TotalSize;
        public int TotalErrors;
        public bool IsWeb;
        public bool Succeeded => Result == BuildResult.Succeeded;
        public bool Cancelled => Result == BuildResult.Cancelled;
    }

    internal sealed class EasyItchPushBuildAllResult
    {
        public readonly List<EasyItchPushBuildResult> Results = new List<EasyItchPushBuildResult>();
        public bool Succeeded => Results.Count > 0 && Results.All(result => result.Succeeded);
    }

    internal static class EasyItchPushBuilder
    {
        private const int FileOperationRetryCount = 20;
        private const int FileOperationRetryDelayMs = 250;
        private const string PackageName = "com.nnican.easy-itch-push";
        private const string LegacyChangelogAssetPath = "Assets/Plugins/EasyItchPush/CHANGELOG.md";

        // Build Profiles should be the source of truth by default.
        // Set a bool field/property named ApplyReleasePlayerSettingsOverrides=true in
        // EasyItchPushSettings if you intentionally want the old global PlayerSettings override behaviour.
        private const bool DefaultApplyReleasePlayerSettingsOverrides = false;

        // macOS builds made from a non-macOS editor often require Mono. Build them last so
        // any Standalone backend/profile switch cannot affect Windows/Linux builds in the same Build All run.
        private const bool MoveMacOsBuildsToEndOnNonMacHosts = true;

        private const string LinuxSysrootPackageName = "com.unity.sdk.linux-x86_64";
        private const string WindowsLinuxToolchainPackageName = "com.unity.toolchain.win-x86_64-linux";
        private const string LinuxSysrootTypeName = "UnityEditor.Il2Cpp.SysrootLinuxX86_64";
        private const string WindowsLinuxToolchainTypeName = "UnityEditor.Il2Cpp.ToolchainWindowsX86_64";

        private sealed class ProfileBuildJob
        {
            public readonly string ProfileName;
            public readonly string ProfilePath;
            public readonly BuildTarget BuildTarget;
            public readonly string Channel;
            public readonly string PlatformDirectory;
            public readonly string LatestDirectory;
            public readonly string OutputPath;
            public readonly bool IsWeb;

            public ProfileBuildJob(
                string profileName,
                string profilePath,
                BuildTarget buildTarget,
                string channel,
                string platformDirectory,
                string latestDirectory,
                string outputPath,
                bool isWeb)
            {
                ProfileName = profileName;
                ProfilePath = profilePath;
                BuildTarget = buildTarget;
                Channel = channel;
                PlatformDirectory = platformDirectory;
                LatestDirectory = latestDirectory;
                OutputPath = outputPath;
                IsWeb = isWeb;
            }
        }

        private sealed class AddressablesPlayerBuildScope : IDisposable
        {
            public static readonly AddressablesPlayerBuildScope SucceededWithoutChanges =
                new AddressablesPlayerBuildScope(true, null, null, false);

            public readonly bool Succeeded;

            private readonly AddressableAssetSettings _settings;
            private readonly AddressableAssetSettings.PlayerBuildOption? _originalBuildWithPlayerBuild;
            private readonly bool _shouldRestore;

            public AddressablesPlayerBuildScope(
                bool succeeded,
                AddressableAssetSettings settings,
                AddressableAssetSettings.PlayerBuildOption? originalBuildWithPlayerBuild,
                bool shouldRestore)
            {
                Succeeded = succeeded;
                _settings = settings;
                _originalBuildWithPlayerBuild = originalBuildWithPlayerBuild;
                _shouldRestore = shouldRestore;
            }

            public void Dispose()
            {
                if (!_shouldRestore ||
                    _settings == null ||
                    !_originalBuildWithPlayerBuild.HasValue)
                {
                    return;
                }

                _settings.BuildAddressablesWithPlayerBuild = _originalBuildWithPlayerBuild.Value;
            }
        }

        private sealed class BuildPreflightIssue
        {
            public readonly string Scope;
            public readonly string Message;

            public BuildPreflightIssue(string scope, string message)
            {
                Scope = string.IsNullOrWhiteSpace(scope) ? "Build" : scope.Trim();
                Message = string.IsNullOrWhiteSpace(message) ? "Unknown build preflight failure." : message.Trim();
            }

            public override string ToString()
            {
                return $"{Scope}: {Message}";
            }
        }

        public static string FindArchiveForProfileVersion(EasyItchPushSettings settings, string channel, string profileName)
        {
            var channelDirectory = settings.GetBuildDirectory(channel);
            if (!Directory.Exists(channelDirectory))
            {
                return string.Empty;
            }

            var archiveName = GetVersionedArtifactBaseName(settings, profileName) + ".zip";
            var archivePath = Path.Combine(channelDirectory, archiveName);
            return File.Exists(archivePath) ? archivePath : string.Empty;
        }

        public static string FindLatestArchiveForProfile(EasyItchPushSettings settings, string channel, string profileName)
        {
            var channelDirectory = settings.GetBuildDirectory(channel);
            if (!Directory.Exists(channelDirectory))
            {
                return string.Empty;
            }

            var archivePrefix = GetVersionedArtifactBaseNamePrefix(profileName);
            return Directory.GetFiles(channelDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).StartsWith(archivePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? string.Empty;
        }

        public static EasyItchPushBuildAllResult BuildAllProfiles(EasyItchPushSettings settings, bool forceRelease = false)
        {
            settings.AutoSyncVersionWithPlayerSettings();
            settings.SyncProfileMappingsWithBuildProfiles();
            settings.SyncVersionToPlayerSettings();

            var jobs = OrderJobsForSafeBackendSwitching(CreateProfileBuildJobs(settings));
            var result = new EasyItchPushBuildAllResult();
            if (jobs.Count == 0)
            {
                EditorUtility.DisplayDialog("No Build Profiles", "No enabled Unity Build Profiles were found.", "OK");
                return result;
            }

            if (!ValidateBuildAllPrerequisites(settings, jobs))
            {
                return result;
            }

            EasyItchPushLog.Info("Build All jobs: " + string.Join(", ", jobs.Select(job => $"{job.ProfileName}:{job.Channel}:{job.BuildTarget}")));

            var buildOptions = settings.GetBuildOptions(forceRelease);
#if UNITY_6000_0_OR_NEWER
            var scenes = Array.Empty<string>();
            EasyItchPushLog.Info("Unity 6 Build Profiles: using scenes from each Build Profile.");
#else
            var scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("No scenes configured", "Enable at least one scene in File > Build Profiles / Build Settings.", "OK");
                return result;
            }
#endif

            var progressId = Progress.Start("Easy Itch Push", "Building selected profiles");
            var mcpBuildScope = McpBuildIsolationScope.Enter();
            try
            {
                for (var i = 0; i < jobs.Count; i++)
                {
                    var job = jobs[i];
                    Progress.Report(progressId, (float)i / jobs.Count, $"Building {job.ProfileName}");
                    var profileResult = BuildSingleProfile(settings, job, scenes, buildOptions);
                    result.Results.Add(profileResult);

                    if (profileResult.Cancelled)
                    {
                        EasyItchPushLog.Warning(
                            $"Build All cancelled after {job.ProfileName}. Remaining profile builds were skipped.");
                        break;
                    }
                }
            }
            finally
            {
                mcpBuildScope.Dispose();
                Progress.Finish(progressId);
            }

            LogBuildAllSummary(settings, result);
            return result;
        }

        public static string GetProfileOutputPath(EasyItchPushSettings settings, BuildProfile profile)
        {
            settings.SyncProfileMappingsWithBuildProfiles();
            var channel = settings.GetChannelForProfileNoSync(profile.name);
            var buildDirectory = settings.GetLatestBuildDirectory(channel);
            return GetLocationPathName(settings, EasyItchPushBuildProfiles.GetBuildTarget(profile), buildDirectory, profile.name);
        }

        private static List<ProfileBuildJob> CreateProfileBuildJobs(EasyItchPushSettings settings)
        {
            var jobs = new List<ProfileBuildJob>();
            var profileAssets = EasyItchPushBuildProfiles.FindAllProfileAssets()
                .Where(profile => profile != null && settings.IsProfileEnabledNoSync(profile.Name))
                .ToList();

            foreach (var profileAsset in profileAssets)
            {
                if (profileAsset == null || string.IsNullOrEmpty(profileAsset.Path))
                {
                    EasyItchPushLog.Warning("Skipping missing Build Profile asset.");
                    continue;
                }

                var profile = EasyItchPushBuildProfiles.LoadProfile(profileAsset.Path);
                if (profile == null)
                {
                    EasyItchPushLog.Warning($"Skipping Build Profile that could not be loaded: {profileAsset.Path}");
                    continue;
                }

                var profileName = profile.name;
                var buildTarget = EasyItchPushBuildProfiles.GetBuildTarget(profile);
                var channel = settings.GetChannelForProfileNoSync(profileName);
                var platformDirectory = settings.GetBuildDirectory(channel);
                var latestDirectory = settings.GetLatestBuildDirectory(channel);
                var outputPath = GetLocationPathName(settings, buildTarget, latestDirectory, profileName);
                var isWeb = EasyItchPushBuildProfiles.IsWebProfile(profile);

                jobs.Add(new ProfileBuildJob(
                    profileName,
                    profileAsset.Path,
                    buildTarget,
                    channel,
                    platformDirectory,
                    latestDirectory,
                    outputPath,
                    isWeb));
            }

            return jobs;
        }


        private static List<ProfileBuildJob> OrderJobsForSafeBackendSwitching(List<ProfileBuildJob> jobs)
        {
            if (!MoveMacOsBuildsToEndOnNonMacHosts || jobs == null || jobs.Count <= 1)
            {
                return jobs ?? new List<ProfileBuildJob>();
            }

            var ordered = jobs
                .Select((job, index) => new { Job = job, Index = index })
                .OrderBy(item => IsMacOsBuildOnNonMacHost(item.Job.BuildTarget) ? 1 : 0)
                .ThenBy(item => item.Index)
                .Select(item => item.Job)
                .ToList();

            if (!jobs.SequenceEqual(ordered))
            {
                EasyItchPushLog.Info(
                    "Build All order adjusted for safer backend switching: " +
                    string.Join(", ", ordered.Select(job => $"{job.ProfileName}:{job.BuildTarget}")));
            }

            return ordered;
        }

        private static bool IsMacOsBuildOnNonMacHost(BuildTarget target)
        {
#if UNITY_EDITOR_OSX
            return false;
#else
            return target == BuildTarget.StandaloneOSX;
#endif
        }

        private static bool ValidateBuildAllPrerequisites(EasyItchPushSettings settings, List<ProfileBuildJob> jobs)
        {
            var issues = new List<BuildPreflightIssue>();
            ValidateEditorCompilationPrerequisites(issues);
            ValidateChangelogPrerequisite(settings, issues);

            foreach (var job in jobs)
            {
                ValidateProfileBuildPrerequisites(job, issues);
            }

            if (issues.Count == 0)
            {
                EasyItchPushLog.Info("Build preflight passed for all enabled profiles.");
                return true;
            }

            ShowBuildPreflightFailed(issues);
            return false;
        }

        private static void ValidateEditorCompilationPrerequisites(List<BuildPreflightIssue> issues)
        {
            if (HasScriptCompilationFailures())
            {
                issues.Add(new BuildPreflightIssue(
                    "Editor",
                    "Unity has script compilation errors. Fix Console compile errors before running Build All."));
            }
        }

        private static bool HasScriptCompilationFailures()
        {
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var property = typeof(EditorUtility).GetProperty("scriptCompilationFailed", flags);
                if (property != null && property.PropertyType == typeof(bool))
                {
                    return (bool)property.GetValue(null);
                }
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not inspect Unity script compilation state: {ex.Message}");
            }

            return false;
        }

        private static void ValidateChangelogPrerequisite(EasyItchPushSettings settings, List<BuildPreflightIssue> issues)
        {
            var changelogAssetPath = ResolveChangelogAssetPath();
            var changelogPath = Path.GetFullPath(changelogAssetPath);
            if (!File.Exists(changelogPath))
            {
                issues.Add(new BuildPreflightIssue(
                    "Build",
                    $"Required changelog file was not found at {changelogPath}."));
                return;
            }

            var expectedHeader = $"## v{settings.ResolvedVersion}";
            var changelogText = File.ReadAllText(changelogPath);
            if (!changelogText.Contains(expectedHeader, StringComparison.Ordinal))
            {
                issues.Add(new BuildPreflightIssue(
                    "Build",
                    $"Changelog {changelogAssetPath} does not contain the expected version header '{expectedHeader}'."));
            }
        }

        private static void ValidateProfileBuildPrerequisites(ProfileBuildJob job, List<BuildPreflightIssue> issues)
        {
            var targetGroup = BuildPipeline.GetBuildTargetGroup(job.BuildTarget);
            if (targetGroup == BuildTargetGroup.Unknown || job.BuildTarget == BuildTarget.NoTarget)
            {
                issues.Add(new BuildPreflightIssue(job.ProfileName, $"Invalid build target {job.BuildTarget}."));
                return;
            }

            if (!IsPlaybackEngineInstalled(targetGroup, job.BuildTarget))
            {
                issues.Add(new BuildPreflightIssue(
                    job.ProfileName,
                    $"Unity playback engine support for {job.BuildTarget} is not installed in the current Editor."));
                return;
            }

            if (job.BuildTarget == BuildTarget.StandaloneLinux64 &&
                ProfileUsesStandaloneIl2Cpp(job.ProfilePath) &&
                !TryEnsureLinuxIl2CppBuildSupport(out var linuxIssue))
            {
                issues.Add(new BuildPreflightIssue(job.ProfileName, linuxIssue));
            }
        }

        private static bool IsPlaybackEngineInstalled(BuildTargetGroup targetGroup, BuildTarget target)
        {
            try
            {
                var playbackEngineDirectory = BuildPipeline.GetPlaybackEngineDirectory(targetGroup, target, BuildOptions.None);
                return !string.IsNullOrWhiteSpace(playbackEngineDirectory) &&
                       Directory.Exists(playbackEngineDirectory);
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not inspect playback engine for {target}: {ex.Message}");
                return false;
            }
        }

        private static bool ProfileUsesStandaloneIl2Cpp(string profilePath)
        {
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(Path.GetFullPath(profilePath));
                var insideScriptingBackend = false;

                foreach (var rawLine in lines)
                {
                    var line = rawLine?.Trim() ?? string.Empty;
                    if (!line.StartsWith("- line: '|", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var yamlLine = ExtractEmbeddedProfileYaml(line);
                    if (string.Equals(yamlLine, "scriptingBackend:", StringComparison.Ordinal))
                    {
                        insideScriptingBackend = true;
                        continue;
                    }

                    if (!insideScriptingBackend)
                    {
                        continue;
                    }

                    if (!yamlLine.StartsWith("Standalone:", StringComparison.Ordinal))
                    {
                        if (!rawLine.Contains("|     ", StringComparison.Ordinal))
                        {
                            break;
                        }

                        continue;
                    }

                    var valueText = yamlLine.Substring("Standalone:".Length).Trim();
                    if (!int.TryParse(valueText, out var backendValue))
                    {
                        return false;
                    }

                    return backendValue == (int)ScriptingImplementation.IL2CPP;
                }
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not inspect scripting backend for Build Profile {profilePath}: {ex.Message}");
            }

            return false;
        }

        private static string ExtractEmbeddedProfileYaml(string line)
        {
            var prefix = "- line: '|";
            var startIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return string.Empty;
            }

            var yamlLine = line.Substring(startIndex + prefix.Length);
            if (yamlLine.EndsWith("'", StringComparison.Ordinal))
            {
                yamlLine = yamlLine.Substring(0, yamlLine.Length - 1);
            }

            return yamlLine.TrimStart();
        }

        private static bool TryEnsureLinuxIl2CppBuildSupport(out string issue)
        {
            issue = string.Empty;

            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
            {
                return true;
            }

            var il2CppDirectory = Path.Combine(
                BuildPipeline.GetPlaybackEngineDirectory(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64, BuildOptions.None),
                "Variations",
                "il2cpp");
            if (!Directory.Exists(il2CppDirectory))
            {
                issue = $"Linux IL2CPP player support is missing in the current Unity Editor: {il2CppDirectory}";
                return false;
            }

            if (!TryResolvePackageRoot(LinuxSysrootPackageName, out var sysrootPackageRoot))
            {
                issue = $"Required package {LinuxSysrootPackageName} is not resolved by Unity Package Manager.";
                return false;
            }

            if (!TryResolvePackageRoot(WindowsLinuxToolchainPackageName, out var toolchainPackageRoot))
            {
                issue = $"Required package {WindowsLinuxToolchainPackageName} is not resolved by Unity Package Manager.";
                return false;
            }

            if (!TryCreateInstance(LinuxSysrootTypeName, out var sysrootPackage, out var sysrootType))
            {
                issue = $"Could not load Linux sysroot integration type {LinuxSysrootTypeName}.";
                return false;
            }

            if (!TryCreateInstance(WindowsLinuxToolchainTypeName, out var toolchainPackage, out var toolchainType))
            {
                issue = $"Could not load Linux toolchain integration type {WindowsLinuxToolchainTypeName}.";
                return false;
            }

            if (!TryGetInstallPath(sysrootPackage, sysrootType, out var sysrootInstallDirectory, out issue))
            {
                return false;
            }

            if (!TryGetInstallPath(toolchainPackage, toolchainType, out var toolchainInstallDirectory, out issue))
            {
                return false;
            }

            if (!TryEnsurePayloadInstalled(
                    LinuxSysrootPackageName,
                    sysrootPackageRoot,
                    sysrootInstallDirectory,
                    IsLinuxSysrootInstallComplete,
                    out issue))
            {
                return false;
            }

            if (!TryEnsurePayloadInstalled(
                    WindowsLinuxToolchainPackageName,
                    toolchainPackageRoot,
                    toolchainInstallDirectory,
                    IsLinuxToolchainInstallComplete,
                    out issue))
            {
                return false;
            }

            if (!TryInitializeSysrootPackage(sysrootPackage, sysrootType, LinuxSysrootPackageName, out issue))
            {
                return false;
            }

            if (!TryInitializeSysrootPackage(toolchainPackage, toolchainType, WindowsLinuxToolchainPackageName, out issue))
            {
                return false;
            }

            if (!TryGetStringMethodResult(sysrootPackage, sysrootType, "GetSysrootPath", out var sysrootPath) ||
                string.IsNullOrWhiteSpace(sysrootPath) ||
                !Directory.Exists(sysrootPath))
            {
                issue = $"Linux sysroot path is unavailable after initialization: {sysrootInstallDirectory}";
                return false;
            }

            if (!TryGetStringMethodResult(toolchainPackage, toolchainType, "GetToolchainPath", out var toolchainPath) ||
                string.IsNullOrWhiteSpace(toolchainPath) ||
                !Directory.Exists(toolchainPath))
            {
                issue = $"Linux toolchain path is unavailable after initialization: {toolchainInstallDirectory}";
                return false;
            }

            var linkerPath = ResolveLinuxToolchainLinkerPath(toolchainPath);
            if (string.IsNullOrWhiteSpace(linkerPath))
            {
                issue = $"Linux toolchain linker was not found under {Path.Combine(toolchainPath, "bin")}.";
                return false;
            }

            EasyItchPushLog.Info($"Linux IL2CPP support is ready: sysroot={sysrootPath}, toolchain={toolchainPath}");
            return true;
        }

        private static bool TryResolvePackageRoot(string packageName, out string packageRoot)
        {
            packageRoot = string.Empty;
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(item => item != null && string.Equals(item.name, packageName, StringComparison.OrdinalIgnoreCase));
                if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    return false;
                }

                packageRoot = Path.GetFullPath(package.resolvedPath);
                return Directory.Exists(packageRoot);
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not resolve package {packageName}: {ex.Message}");
                return false;
            }
        }

        private static bool TryCreateInstance(string typeName, out object instance, out Type type)
        {
            instance = null;
            type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName, false))
                .FirstOrDefault(candidate => candidate != null);
            if (type == null)
            {
                return false;
            }

            try
            {
                instance = Activator.CreateInstance(type);
                return instance != null;
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not instantiate {typeName}: {ex.Message}");
                instance = null;
                type = null;
                return false;
            }
        }

        private static bool TryGetInstallPath(object instance, Type type, out string installPath, out string issue)
        {
            issue = string.Empty;
            if (!TryGetStringMethodResult(instance, type, "PathToPayload", out installPath) ||
                string.IsNullOrWhiteSpace(installPath))
            {
                issue = $"Could not resolve payload install directory for {type?.FullName ?? "unknown type"}.";
                return false;
            }

            installPath = Path.GetFullPath(installPath);
            return true;
        }

        private static bool TryGetStringMethodResult(object instance, Type type, string methodName, out string result)
        {
            result = string.Empty;
            if (instance == null || type == null)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(string))
            {
                return false;
            }

            result = method.Invoke(instance, null) as string ?? string.Empty;
            return true;
        }

        private static bool TryEnsurePayloadInstalled(
            string packageName,
            string packageRoot,
            string installDirectory,
            Func<string, bool> isInstallComplete,
            out string issue)
        {
            issue = string.Empty;
            if (isInstallComplete(installDirectory))
            {
                return true;
            }

            var payloadArchivePath = Path.Combine(packageRoot, "data~", "payload.tar.7z");
            if (!File.Exists(payloadArchivePath))
            {
                issue = $"Payload archive for {packageName} was not found at {payloadArchivePath}.";
                return false;
            }

            if (!TryExtractPayloadArchive(payloadArchivePath, installDirectory, packageName, out issue))
            {
                return false;
            }

            if (!isInstallComplete(installDirectory))
            {
                issue = $"Payload for {packageName} was extracted, but the install is still incomplete at {installDirectory}.";
                return false;
            }

            EasyItchPushLog.Info($"Repaired payload for {packageName}: {installDirectory}");
            return true;
        }

        private static bool TryExtractPayloadArchive(
            string payloadArchivePath,
            string installDirectory,
            string packageName,
            out string issue)
        {
            issue = string.Empty;
            var sevenZipPath = EditorApplication.sevenZipPath;
            if (string.IsNullOrWhiteSpace(sevenZipPath) || !File.Exists(sevenZipPath))
            {
                issue = $"Unity 7-Zip executable was not found at {sevenZipPath}.";
                return false;
            }

            try
            {
                if (Directory.Exists(installDirectory))
                {
                    Directory.Delete(installDirectory, true);
                }

                Directory.CreateDirectory(installDirectory);
            }
            catch (Exception ex)
            {
                issue = $"Could not prepare install directory {installDirectory} for {packageName}: {ex.Message}";
                return false;
            }

            var command = $"\"{sevenZipPath}\" x -y \"{payloadArchivePath}\" -so | \"{sevenZipPath}\" x -y -aoa -ttar -si";
            var process = new global::System.Diagnostics.Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c \"{command}\"";
            process.StartInfo.WorkingDirectory = installDirectory;

            try
            {
                process.Start();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    return true;
                }

                issue = $"Could not extract payload for {packageName} from {payloadArchivePath}. ExitCode={process.ExitCode}.";
            }
            catch (Exception ex)
            {
                issue = $"Could not extract payload for {packageName}: {ex.Message}";
            }

            TryDeleteDirectory(installDirectory);
            return false;
        }

        private static bool IsLinuxSysrootInstallComplete(string installDirectory)
        {
            if (!Directory.Exists(installDirectory))
            {
                return false;
            }

            return Directory.EnumerateDirectories(installDirectory, "x86_64-unity-linux-gnu", SearchOption.TopDirectoryOnly)
                .Select(path => Path.Combine(path, "sysroot"))
                .Any(Directory.Exists);
        }

        private static bool IsLinuxToolchainInstallComplete(string installDirectory)
        {
            if (!Directory.Exists(installDirectory))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(ResolveLinuxToolchainLinkerPath(installDirectory));
        }

        private static string ResolveLinuxToolchainLinkerPath(string toolchainRoot)
        {
            if (string.IsNullOrWhiteSpace(toolchainRoot))
            {
                return string.Empty;
            }

            var binDirectory = Path.Combine(toolchainRoot, "bin");
            if (!Directory.Exists(binDirectory))
            {
                return string.Empty;
            }

            var linkerCandidates = new[]
            {
                Path.Combine(binDirectory, "ld.lld"),
                Path.Combine(binDirectory, "ld.lld.exe"),
                Path.Combine(binDirectory, "lld"),
                Path.Combine(binDirectory, "lld.exe")
            };

            return linkerCandidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static bool TryInitializeSysrootPackage(object instance, Type type, string packageName, out string issue)
        {
            issue = string.Empty;
            if (instance == null || type == null)
            {
                issue = $"Package {packageName} could not be initialized because its integration type is missing.";
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = type.GetMethod("Initialize", flags, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(bool))
            {
                issue = $"Package {packageName} does not expose a compatible Initialize() method.";
                return false;
            }

            try
            {
                var initialized = (bool)method.Invoke(instance, null);
                if (initialized)
                {
                    return true;
                }

                issue = $"Package {packageName} initialization returned false.";
                return false;
            }
            catch (TargetInvocationException ex)
            {
                issue = $"Package {packageName} initialization threw: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                issue = $"Package {packageName} initialization threw: {ex.Message}";
                return false;
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Best-effort cleanup after a failed payload repair.
            }
        }

        private static void ShowBuildPreflightFailed(List<BuildPreflightIssue> issues)
        {
            var uniqueIssues = issues
                .Where(issue => issue != null)
                .Select(issue => issue.ToString())
                .Distinct()
                .ToList();

            var message = new StringBuilder();
            message.AppendLine("Build All was blocked by preflight checks:");
            message.AppendLine();

            foreach (var issue in uniqueIssues.Take(12))
            {
                message.AppendLine("- " + issue);
            }

            if (uniqueIssues.Count > 12)
            {
                message.AppendLine($"- ...and {uniqueIssues.Count - 12} more issue(s).");
            }

            message.AppendLine();
            message.AppendLine("Log file:");
            message.AppendLine(EasyItchPushLog.CurrentLogPath);

            EasyItchPushLog.Error(
                "Build preflight failed:\n" +
                string.Join("\n", uniqueIssues.Select(issue => "- " + issue)));
            EditorUtility.DisplayDialog("Easy Itch Push Build preflight failed", message.ToString(), "OK");
        }

        private static bool ShouldApplyReleasePlayerSettingsOverrides(EasyItchPushSettings settings)
        {
            // Backwards-compatible opt-in without requiring a Settings class/UI migration immediately.
            // Supported names if you add them to EasyItchPushSettings later:
            //   ApplyReleasePlayerSettingsOverrides == true  -> use old override behaviour.
            //   UseBuildProfilePlayerSettings == true        -> use Build Profile settings as-is.
            if (TryGetBoolSetting(settings, "ApplyReleasePlayerSettingsOverrides", out var applyOverrides))
            {
                return applyOverrides;
            }

            if (TryGetBoolSetting(settings, "applyReleasePlayerSettingsOverrides", out applyOverrides))
            {
                return applyOverrides;
            }

            if (TryGetBoolSetting(settings, "UseBuildProfilePlayerSettings", out var useBuildProfileSettings))
            {
                return !useBuildProfileSettings;
            }

            if (TryGetBoolSetting(settings, "useBuildProfilePlayerSettings", out useBuildProfileSettings))
            {
                return !useBuildProfileSettings;
            }

            return DefaultApplyReleasePlayerSettingsOverrides;
        }

        private static bool TryGetBoolSetting(EasyItchPushSettings settings, string memberName, out bool value)
        {
            value = false;
            if (settings == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            var type = settings.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var property = type.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
            {
                value = (bool)property.GetValue(settings);
                return true;
            }

            var field = type.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                value = (bool)field.GetValue(settings);
                return true;
            }

            return false;
        }

        private static void LogCurrentScriptingBackend(ProfileBuildJob job)
        {
            try
            {
                var targetGroup = BuildPipeline.GetBuildTargetGroup(job.BuildTarget);
                if (targetGroup == BuildTargetGroup.Unknown)
                {
                    return;
                }

                var namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
                var backend = PlayerSettings.GetScriptingBackend(namedTarget);
                EasyItchPushLog.Info($"Scripting backend before {job.ProfileName} build: {backend} ({job.BuildTarget}).");
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not read scripting backend for {job.ProfileName}: {ex.Message}");
            }
        }

        private static EasyItchPushBuildResult BuildSingleProfile(
            EasyItchPushSettings settings,
            ProfileBuildJob job,
            string[] scenes,
            BuildOptions buildOptions)
        {
            var started = DateTime.Now;
            var result = new EasyItchPushBuildResult
            {
                ProfileName = job.ProfileName,
                Channel = job.Channel,
                OutputPath = job.LatestDirectory,
                IsWeb = job.IsWeb
            };

            try
            {
                var profile = EasyItchPushBuildProfiles.LoadProfile(job.ProfilePath);
                if (profile == null)
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = 1;
                    EasyItchPushLog.Error($"Build Profile not found: {job.ProfilePath}");
                    return result;
                }

                if (!ActivateBuildTargetAndProfile(job, profile))
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    return result;
                }

                // Build Profiles own their platform-specific Player Settings. After activating each profile,
                // write only the version into the currently active profile/player settings. Do not override backend,
                // stripping, or other per-profile settings here.
                settings.SyncVersionToActivePlayerSettingsOnly();

                if (ShouldApplyReleasePlayerSettingsOverrides(settings))
                {
                    EasyItchPushLog.Info($"Applying Easy Itch Push PlayerSettings overrides for {job.ProfileName} ({job.BuildTarget}).");
                    settings.ApplyReleasePlayerSettings(job.BuildTarget);
                }
                else
                {
                    EasyItchPushLog.Info($"Using Unity Build Profile PlayerSettings for {job.ProfileName} ({job.BuildTarget}); Easy Itch Push overrides are disabled.");
                }

                if (HasScriptCompilationFailures())
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    EasyItchPushLog.Error(
                        $"Cannot build {job.ProfileName} because Unity still has script compilation errors. Fix Console compile errors first.");
                    return result;
                }

                if (job.BuildTarget == BuildTarget.StandaloneLinux64 &&
                    ProfileUsesStandaloneIl2Cpp(job.ProfilePath) &&
                    !TryEnsureLinuxIl2CppBuildSupport(out var linuxIssue))
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    EasyItchPushLog.Error($"Linux build prerequisites failed for {job.ProfileName}: {linuxIssue}");
                    return result;
                }

                LogCurrentScriptingBackend(job);

                var addressablesBuildScope = PrepareAddressablesForPlayerBuild(job);
                if (!addressablesBuildScope.Succeeded)
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    return result;
                }

                PrepareBuildDirectory(settings, job.LatestDirectory);

                EasyItchPushLog.Info($"Building profile {job.ProfileName} ({job.BuildTarget}) to {job.OutputPath}");
                BuildReport report;
                try
                {
#if UNITY_6000_0_OR_NEWER
                    var options = new BuildPlayerWithProfileOptions
                    {
                        buildProfile = profile,
                        locationPathName = job.OutputPath,
                        options = buildOptions
                    };

                    report = BuildPipeline.BuildPlayer(options);
#else
                    BuildProfile.SetActiveBuildProfile(profile);

                    var options = new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = job.OutputPath,
                        target = job.BuildTarget,
                        targetGroup = BuildPipeline.GetBuildTargetGroup(job.BuildTarget),
                        options = buildOptions
                    };

                    report = BuildPipeline.BuildPlayer(options);
#endif
                }
                finally
                {
                    addressablesBuildScope.Dispose();
                }

                if (report == null)
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    EasyItchPushLog.Error($"Build profile {job.ProfileName} did not return a BuildReport.");
                    return result;
                }

                var reportedResult = report.summary.result;
                result.Result = reportedResult;
                result.TotalSize = report.summary.totalSize;
                result.TotalErrors = report.summary.totalErrors;

                if (reportedResult != BuildResult.Succeeded)
                {
                    var logMessage =
                        $"Build profile {job.ProfileName} ended with {reportedResult}. Output={job.LatestDirectory}, size={(result.TotalSize / 1048576f):0.0} MB, errors={result.TotalErrors}";
                    if (reportedResult == BuildResult.Cancelled)
                    {
                        EasyItchPushLog.Warning(logMessage);
                        return result;
                    }

                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    LogBuildReportIssues(report, job.ProfileName);
                    EasyItchPushLog.Error(logMessage);
                    return result;
                }

                if (!BuildOutputContainsFiles(job.LatestDirectory))
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    EasyItchPushLog.Error($"Build profile {job.ProfileName} reported success but produced no files at {job.LatestDirectory}.");
                    return result;
                }

                if (!ValidateWebBuildBeforeArchive(job))
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    return result;
                }

                result.ArchivePath = CreateZipArchive(settings, job.LatestDirectory, job.PlatformDirectory, job.ProfileName);
                if (string.IsNullOrEmpty(result.ArchivePath) || !File.Exists(result.ArchivePath) || new FileInfo(result.ArchivePath).Length == 0)
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                    EasyItchPushLog.Error($"Build profile {job.ProfileName} succeeded, but archive creation failed.");
                }
                else if (!ValidateWebArchive(job, result.ArchivePath))
                {
                    result.Result = BuildResult.Failed;
                    result.TotalErrors = Math.Max(1, result.TotalErrors);
                }
            }
            catch (Exception ex)
            {
                result.Result = BuildResult.Failed;
                result.TotalErrors = Math.Max(1, result.TotalErrors);
                EasyItchPushLog.Error($"Build profile {job.ProfileName} failed: {ex}");
            }
            finally
            {
                result.Duration = DateTime.Now - started;
            }

            return result;
        }


        private static AddressablesPlayerBuildScope PrepareAddressablesForPlayerBuild(ProfileBuildJob job)
        {
            // Localization depends on Addressables. If Addressables content is stale or was built for a
            // previous active target, WebGL will boot but fail to load StreamingAssets/aa/settings.json.
            var addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (addressableSettings == null)
            {
                EasyItchPushLog.Info($"No Addressables settings asset found; skipping Addressables content build for {job.ProfileName}.");
                return AddressablesPlayerBuildScope.SucceededWithoutChanges;
            }

            var originalBuildWithPlayerBuild = addressableSettings.BuildAddressablesWithPlayerBuild;

            try
            {
                EasyItchPushLog.Info($"Cleaning Addressables player content for {job.ProfileName} ({job.BuildTarget}) before player build.");
                AddressableAssetSettings.CleanPlayerContent();

                EasyItchPushLog.Info($"Building Addressables content for {job.ProfileName} ({job.BuildTarget}) before player build.");
                AddressableAssetSettings.BuildPlayerContent();

                EasyItchPushLog.Info($"Addressables content built for {job.ProfileName} ({job.BuildTarget}).");
                var shouldRestore = DisableAddressablesBuildWithPlayerBuild(addressableSettings, job);

                return new AddressablesPlayerBuildScope(
                    true,
                    addressableSettings,
                    originalBuildWithPlayerBuild,
                    shouldRestore);
            }
            catch (TargetInvocationException ex)
            {
                EasyItchPushLog.Error($"Addressables build failed for {job.ProfileName} ({job.BuildTarget}): {ex.InnerException ?? ex}");
                return new AddressablesPlayerBuildScope(false, null, null, false);
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Error($"Addressables build failed for {job.ProfileName} ({job.BuildTarget}): {ex}");
                return new AddressablesPlayerBuildScope(false, null, null, false);
            }
        }

        private static bool DisableAddressablesBuildWithPlayerBuild(AddressableAssetSettings addressableSettings, ProfileBuildJob job)
        {
            if (addressableSettings == null ||
                addressableSettings.BuildAddressablesWithPlayerBuild == AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer)
            {
                return false;
            }

            addressableSettings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            EasyItchPushLog.Info(
                $"Addressables content was built explicitly for {job.ProfileName}; " +
                "temporarily disabled Build With Player to avoid a duplicate Unity Build Profile pre-build.");
            return true;
        }

        private static string GetStringMember(object instance, Type type, string memberName)
        {
            if (instance == null || type == null)
            {
                return string.Empty;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var property = type.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == typeof(string))
            {
                return property.GetValue(instance) as string ?? string.Empty;
            }

            var field = type.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(string))
            {
                return field.GetValue(instance) as string ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool ValidateWebBuildBeforeArchive(ProfileBuildJob job)
        {
            if (!job.IsWeb)
            {
                return true;
            }

            var buildRoot = Path.GetFullPath(job.LatestDirectory);
            var indexPath = Path.Combine(buildRoot, "index.html");
            if (!File.Exists(indexPath))
            {
                EasyItchPushLog.Error($"Web build {job.ProfileName} is missing index.html at {indexPath}. The itch.io HTML channel will not run this build.");
                return false;
            }

            var streamingAssets = Path.Combine(buildRoot, "StreamingAssets");
            if (!Directory.Exists(streamingAssets))
            {
                EasyItchPushLog.Warning($"Web build {job.ProfileName} has no StreamingAssets directory at {streamingAssets}.");
                return true;
            }

            var addressablesSettings = Path.Combine(streamingAssets, "aa", "settings.json");
            if (!File.Exists(addressablesSettings))
            {
                var foundSettings = Directory.GetFiles(streamingAssets, "settings.json", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(buildRoot, path).Replace('\\', '/'))
                    .ToArray();

                EasyItchPushLog.Error(
                    $"Web build {job.ProfileName} is missing StreamingAssets/aa/settings.json. " +
                    "Localization/Addressables will fail at runtime. " +
                    (foundSettings.Length > 0
                        ? "Found settings.json at: " + string.Join(", ", foundSettings)
                        : "No settings.json was found under StreamingAssets."));
                return false;
            }

            EasyItchPushLog.Info($"Validated WebGL StreamingAssets for {job.ProfileName}: {Path.GetRelativePath(buildRoot, addressablesSettings).Replace('\\', '/')} found.");
            return true;
        }

        private static bool ValidateWebArchive(ProfileBuildJob job, string archivePath)
        {
            if (!job.IsWeb)
            {
                return true;
            }

            try
            {
                using (var zip = ZipFile.OpenRead(archivePath))
                {
                    if (zip.GetEntry("index.html") == null)
                    {
                        EasyItchPushLog.Error($"Web archive {archivePath} is missing index.html at the archive root.");
                        return false;
                    }

                    if (zip.GetEntry("StreamingAssets/aa/settings.json") == null)
                    {
                        var foundSettings = zip.Entries
                            .Where(entry => entry.FullName.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase))
                            .Select(entry => entry.FullName)
                            .ToArray();

                        EasyItchPushLog.Error(
                            $"Web archive {archivePath} is missing StreamingAssets/aa/settings.json. " +
                            "The published itch.io build would fail to load Addressables/Localization. " +
                            (foundSettings.Length > 0
                                ? "Found settings.json in archive at: " + string.Join(", ", foundSettings)
                                : "No settings.json was found in the archive."));
                        return false;
                    }
                }

                EasyItchPushLog.Info($"Validated WebGL archive contents for {job.ProfileName}: index.html and StreamingAssets/aa/settings.json found.");
                return true;
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Error($"Could not validate Web archive {archivePath}: {ex}");
                return false;
            }
        }

        private static void LogBuildReportIssues(BuildReport report, string profileName)
        {
            if (report == null || report.steps == null || report.steps.Length == 0)
            {
                return;
            }

            const int maxMessages = 60;
            var errors = new List<string>();
            var warnings = new List<string>();

            foreach (var step in report.steps)
            {
                if (step.messages == null || step.messages.Length == 0)
                {
                    continue;
                }

                foreach (var message in step.messages)
                {
                    var text = string.IsNullOrWhiteSpace(message.content) ? "<empty message>" : message.content.Trim();
                    var line = $"[{message.type}] {step.name}: {text}";

                    if (message.type == UnityEngine.LogType.Error ||
                        message.type == UnityEngine.LogType.Exception ||
                        message.type == UnityEngine.LogType.Assert)
                    {
                        errors.Add(line);
                    }
                    else if (message.type == UnityEngine.LogType.Warning)
                    {
                        warnings.Add(line);
                    }
                }
            }

            var lines = errors.Count > 0 ? errors : warnings;
            if (lines.Count == 0)
            {
                if (HasScriptCompilationFailures())
                {
                    EasyItchPushLog.Error(
                        $"Build profile {profileName} failed because Unity still has script compilation errors. " +
                        "Fix Console compile errors before running Easy Itch Push.");
                    return;
                }

                EasyItchPushLog.Warning($"Build profile {profileName} failed, but BuildReport did not contain detailed error messages. Check Editor.log for the full Unity output.");
                return;
            }

            var title = errors.Count > 0 ? "errors" : "warnings";
            EasyItchPushLog.Error(
                $"BuildReport {title} for {profileName}:\n" +
                string.Join("\n", lines.Take(maxMessages)));

            if (lines.Count > maxMessages)
            {
                EasyItchPushLog.Warning($"BuildReport for {profileName} contained {lines.Count} issue messages; only first {maxMessages} were printed.");
            }
        }

        private static bool ActivateBuildTargetAndProfile(ProfileBuildJob job, BuildProfile profile)
        {
            var targetGroup = BuildPipeline.GetBuildTargetGroup(job.BuildTarget);
            if (targetGroup == BuildTargetGroup.Unknown || job.BuildTarget == BuildTarget.NoTarget)
            {
                EasyItchPushLog.Error($"Build profile {job.ProfileName} has an invalid build target: {job.BuildTarget}.");
                return false;
            }

            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != job.BuildTarget)
                {
                    EasyItchPushLog.Info($"Switching active build target: {EditorUserBuildSettings.activeBuildTarget} -> {job.BuildTarget}");
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, job.BuildTarget))
                    {
                        EasyItchPushLog.Error($"Could not switch active build target to {job.BuildTarget} for profile {job.ProfileName}.");
                        return false;
                    }
                }

#if UNITY_6000_0_OR_NEWER
                BuildProfile.SetActiveBuildProfile(profile);
#endif

                AssetDatabase.SaveAssets();
                // Do not force a full asset refresh here. Switching targets/profiles already triggers the
                // imports Unity needs; ForceUpdate between every profile can restart shader/import workers
                // and make Build All fail even when each profile builds from Unity's UI.

                if (EditorUserBuildSettings.activeBuildTarget != job.BuildTarget)
                {
                    EasyItchPushLog.Error(
                        $"Active build target is still {EditorUserBuildSettings.activeBuildTarget}, expected {job.BuildTarget} for profile {job.ProfileName}.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Error($"Could not activate build profile {job.ProfileName} ({job.BuildTarget}): {ex}");
                return false;
            }
        }

        private static string GetLocationPathName(
            EasyItchPushSettings settings,
            BuildTarget target,
            string buildDirectory,
            string profileName)
        {
            var productName = SanitizeFileName(PlayerSettings.productName);
            if (string.IsNullOrWhiteSpace(productName))
            {
                productName = "Game";
            }

            switch (target)
            {
                case BuildTarget.WebGL:
                case BuildTarget.iOS:
                    return buildDirectory;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(buildDirectory, productName + ".exe");
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(buildDirectory, productName + ".app");
                case BuildTarget.StandaloneLinux64:
                    return Path.Combine(buildDirectory, productName + ".x86_64");
                case BuildTarget.Android:
                    // itch.io surfaces the APK name from inside the uploaded zip. Use the mapped
                    // channel name here so the download label stays normalized and avoids profile-only
                    // display names such as Android(TM).
                    return Path.Combine(buildDirectory, GetAndroidArtifactBaseName(settings, profileName) + ".apk");
                default:
                    return Path.Combine(buildDirectory, productName);
            }
        }

        private static string[] GetEnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static void PrepareBuildDirectory(EasyItchPushSettings settings, string buildDirectory)
        {
            if (settings.cleanBuildDirectory && Directory.Exists(buildDirectory))
            {
                DeleteDirectoryWithRetry(buildDirectory);
            }

            Directory.CreateDirectory(buildDirectory);
        }

        private static bool BuildOutputContainsFiles(string buildDirectory)
        {
            return Directory.Exists(buildDirectory) &&
                   Directory.EnumerateFiles(buildDirectory, "*", SearchOption.AllDirectories)
                       .Any(file => !ShouldSkipArchiveFile(buildDirectory, string.Empty, file));
        }

        private static string CreateZipArchive(EasyItchPushSettings settings, string sourceBuildDirectory, string archiveDirectory, string profileName)
        {
            var sourceDirectory = Path.GetFullPath(sourceBuildDirectory);
            if (!Directory.Exists(sourceDirectory))
            {
                EasyItchPushLog.Warning($"Cannot archive {profileName}: build directory not found at {sourceDirectory}");
                return string.Empty;
            }

            Directory.CreateDirectory(archiveDirectory);
            var archiveName = GetVersionedArtifactBaseName(settings, profileName) + ".zip";
            var archivePath = Path.Combine(archiveDirectory, archiveName);
            var tempArchivePath = archivePath + $".{Guid.NewGuid():N}.tmp";

            var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(file => !ShouldSkipArchiveFile(sourceDirectory, archivePath, file))
                .ToList();

            if (files.Count == 0)
            {
                EasyItchPushLog.Warning($"No files to archive for {profileName} at {sourceDirectory}");
                return string.Empty;
            }

            try
            {
                using (var zip = ZipFile.Open(tempArchivePath, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        var entryName = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    }

                    AddChangelogToArchive(zip, settings.ResolvedVersion);
                }

                if (File.Exists(archivePath))
                {
                    DeleteFileWithRetry(archivePath);
                }

                MoveFileWithRetry(tempArchivePath, archivePath);
                EasyItchPushLog.Info($"Created archive: {archivePath}");
                return archivePath;
            }
            catch
            {
                TryDeleteFile(tempArchivePath);
                throw;
            }
        }

        private static void AddChangelogToArchive(ZipArchive zip, string expectedVersion)
        {
            var changelogAssetPath = ResolveChangelogAssetPath();
            var changelogPath = Path.GetFullPath(changelogAssetPath);
            if (!File.Exists(changelogPath))
            {
                throw new FileNotFoundException($"Build changelog was not found at {changelogPath}.", changelogPath);
            }

            var expectedHeader = $"## v{expectedVersion}";
            var changelogText = File.ReadAllText(changelogPath);
            if (!changelogText.Contains(expectedHeader, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Build changelog {changelogAssetPath} does not contain the expected version header '{expectedHeader}'.");
            }

            zip.CreateEntryFromFile(changelogPath, "CHANGELOG.md", CompressionLevel.Optimal);
        }

        private static string ResolveChangelogAssetPath()
        {
            var packageRoot = ResolvePackageRootPath(PackageName);
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                var packageChangelogPath = Path.Combine(packageRoot, "CHANGELOG.md");
                if (File.Exists(packageChangelogPath))
                {
                    return packageChangelogPath;
                }
            }

            return LegacyChangelogAssetPath;
        }

        private static string ResolvePackageRootPath(string packageName)
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(item => item != null && string.Equals(item.name, packageName, StringComparison.OrdinalIgnoreCase));

                return package != null ? package.resolvedPath : string.Empty;
            }
            catch (Exception ex)
            {
                EasyItchPushLog.Warning($"Could not resolve package root for {packageName}: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool ShouldSkipArchiveFile(string sourceDirectory, string archivePath, string file)
        {
            var fullFile = Path.GetFullPath(file);
            if (!string.IsNullOrEmpty(archivePath) &&
                string.Equals(fullFile, Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var extension = Path.GetExtension(fullFile);
            if (string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase) &&
                fullFile.IndexOf(".zip.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var relative = Path.GetRelativePath(sourceDirectory, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Any(segment =>
                segment.IndexOf("DontShip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                segment.IndexOf("DoNotShip", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void DeleteFileWithRetry(string path)
        {
            RetryFileOperation(() =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }, $"delete file {path}");
        }

        private static void MoveFileWithRetry(string sourcePath, string destinationPath)
        {
            RetryFileOperation(() => File.Move(sourcePath, destinationPath), $"move file {sourcePath} to {destinationPath}");
        }

        private static void DeleteDirectoryWithRetry(string path)
        {
            RetryFileOperation(() =>
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }, $"delete directory {path}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup. The original archive exception is more useful to report.
            }
        }

        private static void RetryFileOperation(Action operation, string description)
        {
            Exception lastException = null;
            for (var attempt = 1; attempt <= FileOperationRetryCount; attempt++)
            {
                try
                {
                    operation();
                    return;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastException = ex;
                }

                if (attempt < FileOperationRetryCount)
                {
                    Thread.Sleep(FileOperationRetryDelayMs);
                }
            }

            throw new IOException($"Could not {description} after {FileOperationRetryCount} attempts.", lastException);
        }

        private static void LogBuildAllSummary(EasyItchPushSettings settings, EasyItchPushBuildAllResult result)
        {
            var lines = new List<string>
            {
                $"=== Easy Itch Push Build All ({settings.ResolvedVersionWithPrefix}) ==="
            };

            foreach (var item in result.Results)
            {
                var archive = string.IsNullOrEmpty(item.ArchivePath) ? string.Empty : $" archive={item.ArchivePath}";
                lines.Add($"{item.Result}: {item.ProfileName} -> {item.OutputPath} [{item.Duration:mm\\:ss}] size={(item.TotalSize / 1048576f):0.0} MB errors={item.TotalErrors}{archive}");
            }

            EasyItchPushLog.Info(string.Join("\n", lines));
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Game";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '-';
                }
            }

            return new string(chars);
        }

        private static string GetVersionedArtifactBaseName(EasyItchPushSettings settings, string profileName)
        {
            var productName = SanitizeFileName(PlayerSettings.productName);
            if (string.IsNullOrWhiteSpace(productName))
            {
                productName = "Game";
            }

            var version = settings != null && !string.IsNullOrWhiteSpace(settings.ResolvedVersionWithPrefix)
                ? settings.ResolvedVersionWithPrefix
                : "v0.0.0";

            return $"{productName}-{SanitizeFileName(profileName)}-{version}";
        }

        private static string GetVersionedArtifactBaseNamePrefix(string profileName)
        {
            var productName = SanitizeFileName(PlayerSettings.productName);
            if (string.IsNullOrWhiteSpace(productName))
            {
                productName = "Game";
            }

            return $"{productName}-{SanitizeFileName(profileName)}-";
        }

        private static string GetAndroidArtifactBaseName(EasyItchPushSettings settings, string profileName)
        {
            var productName = EasyItchPushSettings.SanitizeItchChannel(PlayerSettings.productName);
            if (string.IsNullOrWhiteSpace(productName))
            {
                productName = "game";
            }

            var channel = settings != null
                ? EasyItchPushSettings.SanitizeItchChannel(settings.GetChannelForProfileNoSync(profileName))
                : EasyItchPushSettings.SanitizeItchChannel(profileName);

            var version = settings != null && !string.IsNullOrWhiteSpace(settings.ResolvedVersionWithPrefix)
                ? settings.ResolvedVersionWithPrefix
                : "v0.0.0";

            return $"{productName}-{channel}-{version}";
        }

        private sealed class McpBuildIsolationScope : IDisposable
        {
            private const string MCP_AUTO_START_ON_LOAD_KEY = "MCPForUnity.AutoStartOnLoad";
            private const string MCP_RESUME_HTTP_AFTER_RELOAD_KEY = "MCPForUnity.ResumeHttpAfterReload";
            private const string MCP_RESUME_STDIO_AFTER_RELOAD_KEY = "MCPForUnity.ResumeStdioAfterReload";
            private const string MCP_SERVICE_LOCATOR_TYPE = "MCPForUnity.Editor.Services.MCPServiceLocator, MCPForUnity.Editor";
            private const string MCP_TRANSPORT_MODE_TYPE = "MCPForUnity.Editor.Services.Transport.TransportMode, MCPForUnity.Editor";
            private const string MCP_STDIO_BRIDGE_HOST_TYPE = "MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost, MCPForUnity.Editor";

            private readonly bool _hadAutoStartSetting;
            private readonly bool _previousAutoStartValue;
            private bool _disposed;

            private McpBuildIsolationScope()
            {
                _hadAutoStartSetting = EditorPrefs.HasKey(MCP_AUTO_START_ON_LOAD_KEY);
                _previousAutoStartValue = EditorPrefs.GetBool(MCP_AUTO_START_ON_LOAD_KEY, false);
            }

            public static McpBuildIsolationScope Enter()
            {
                var scope = new McpBuildIsolationScope();
                scope.DisableMcpAutoStart();
                scope.DisableMcpReloadResume();
                scope.StopMcpTransports();
                return scope;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                RestoreMcpAutoStart();
            }

            private void DisableMcpAutoStart()
            {
                if (!_previousAutoStartValue)
                {
                    return;
                }

                EditorPrefs.SetBool(MCP_AUTO_START_ON_LOAD_KEY, false);
                EasyItchPushLog.Info("Disabled MCP For Unity auto-start during player builds.");
            }

            private void DisableMcpReloadResume()
            {
                EditorPrefs.DeleteKey(MCP_RESUME_HTTP_AFTER_RELOAD_KEY);
                EditorPrefs.DeleteKey(MCP_RESUME_STDIO_AFTER_RELOAD_KEY);
            }

            private void RestoreMcpAutoStart()
            {
                if (!_hadAutoStartSetting)
                {
                    return;
                }

                EditorPrefs.SetBool(MCP_AUTO_START_ON_LOAD_KEY, _previousAutoStartValue);
            }

            private void StopMcpTransports()
            {
                try
                {
                    var serviceLocatorType = Type.GetType(MCP_SERVICE_LOCATOR_TYPE);
                    var transportManagerProperty = serviceLocatorType?.GetProperty("TransportManager", BindingFlags.Public | BindingFlags.Static);
                    var transportManager = transportManagerProperty?.GetValue(null);

                    StopTransportManagerAsync(transportManager);
                    ForceStopTransport(transportManager, "Http");
                    ForceStopTransport(transportManager, "Stdio");
                    StopLegacyStdioBridge();
                    DisableMcpReloadResume();
                }
                catch (Exception ex)
                {
                    EasyItchPushLog.Warning($"Could not isolate MCP For Unity before build: {GetReflectionMessage(ex)}");
                }
            }

            private static void StopTransportManagerAsync(object transportManager)
            {
                var stopAsyncMethod = transportManager?.GetType().GetMethod("StopAsync", BindingFlags.Public | BindingFlags.Instance);
                var stopTask = stopAsyncMethod?.Invoke(transportManager, new object[] { null }) as Task;
                stopTask?.GetAwaiter().GetResult();
            }

            private static void ForceStopTransport(object transportManager, string modeName)
            {
                var transportModeType = Type.GetType(MCP_TRANSPORT_MODE_TYPE);
                if (transportManager == null || transportModeType == null)
                {
                    return;
                }

                var mode = Enum.Parse(transportModeType, modeName);
                var forceStopMethod = transportManager.GetType().GetMethod("ForceStop", BindingFlags.Public | BindingFlags.Instance);
                forceStopMethod?.Invoke(transportManager, new[] { mode });
            }

            private static void StopLegacyStdioBridge()
            {
                var stdioBridgeHostType = Type.GetType(MCP_STDIO_BRIDGE_HOST_TYPE);
                var stopMethod = stdioBridgeHostType?.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static);
                stopMethod?.Invoke(null, null);
            }

            private static string GetReflectionMessage(Exception exception)
            {
                while (exception is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null)
                {
                    exception = targetInvocationException.InnerException;
                }

                var result = exception.Message;

                return result;
            }
        }
    }
}
