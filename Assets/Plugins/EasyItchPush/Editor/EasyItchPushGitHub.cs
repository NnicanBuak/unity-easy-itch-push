using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EasyItchPush.Editor
{
    /// <summary>GitHub CLI transport for macOS builds; credentials stay in gh auth's vault.</summary>
    internal static class EasyItchPushGitHub
    {
        [Serializable] private sealed class RunList { public RunInfo[] runs; }
        [Serializable] private sealed class RunInfo
        {
            public long databaseId;
            public string displayTitle;
            public string headSha;
            public string status;
            public string conclusion;
            public string url;
        }

        public static string Status { get; private set; } = "Not started";
        public static string LastRunUrl { get; private set; } = "";
        public static bool IsBusy { get; private set; }

        private const string Workflow = @"name: Easy Itch Push macOS
run-name: Easy Itch Push __DOLLAR__{{ inputs.request_id }}
on:
  workflow_dispatch:
    inputs:
      profile:
        description: Unity Build Profile name
        required: true
        type: string
      version:
        description: Expected committed game version
        required: true
        type: string
      request_id:
        description: Unique build identifier
        required: true
        type: string
permissions:
  contents: read
jobs:
  macos:
    runs-on: macos-latest
    timeout-minutes: 90
    steps:
      - uses: actions/checkout@v4
        with:
          lfs: true
      - name: Build macOS IL2CPP using the project Build Profile
        uses: game-ci/unity-builder@v4
        env:
          UNITY_LICENSE: __DOLLAR__{{ secrets.UNITY_LICENSE }}
          UNITY_EMAIL: __DOLLAR__{{ secrets.UNITY_EMAIL }}
          UNITY_PASSWORD: __DOLLAR__{{ secrets.UNITY_PASSWORD }}
        with:
          targetPlatform: StandaloneOSX
          buildMethod: EasyItchPush.Editor.EasyItchPushCloudBatch.Build
          customParameters: -easyItchProfile ""__DOLLAR__{{ inputs.profile }}"" -easyItchVersion ""__DOLLAR__{{ inputs.version }}""
          cacheUnityInstallationOnMac: true
      - name: Upload archives
        uses: actions/upload-artifact@v4
        with:
          name: easy-itch-push-__DOLLAR__{{ inputs.request_id }}
          path: Builds/**/*.zip
          if-no-files-found: error
          retention-days: 7
";

        public static void InstallWorkflow()
        {
            var path = Path.GetFullPath(".github/workflows/easy-itch-push-macos.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && !EditorUtility.DisplayDialog("Existing workflow",
                    "Replace .github/workflows/easy-itch-push-macos.yml?", "Replace", "Cancel"))
                return;
            File.WriteAllText(path, Workflow.Replace("__DOLLAR__", "$"), new UTF8Encoding(false));
            EditorUtility.DisplayDialog("Cloud workflow generated",
                "Commit and push .github/workflows/easy-itch-push-macos.yml to your game repository's DEFAULT branch.\n\n" +
                "Also commit the plugin, project version, Build Profiles and changelog. " +
                "Configure UNITY_LICENSE, UNITY_EMAIL and UNITY_PASSWORD in GitHub Actions secrets, then authenticate locally with gh auth login.", "OK");
        }

        private static EasyItchPushProcessResult Gh(string args)
        {
            var result = EasyItchPushProcess.Run("gh", args);
            if (!result.Succeeded)
                throw new InvalidOperationException("GitHub CLI failed. Install gh and run 'gh auth login'. " + result.Error);
            return result;
        }

        private static string ValidateSource(EasyItchPushSettings settings)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(settings.githubBranch ?? "", @"^[A-Za-z0-9][A-Za-z0-9._/-]*$"))
                throw new InvalidOperationException("Invalid GitHub branch.");
            if (string.IsNullOrWhiteSpace(settings.githubRepository) ||
                !System.Text.RegularExpressions.Regex.IsMatch(settings.githubRepository,
                    @"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$"))
                throw new InvalidOperationException("Configure GitHub Repository as owner/repo.");
            var status = EasyItchPushProcess.Run("git", "status --porcelain --untracked-files=normal");
            if (!status.Succeeded)
                throw new InvalidOperationException("Unity project must be tracked in Git.");
            if (!string.IsNullOrWhiteSpace(status.Output))
                throw new InvalidOperationException("Commit all project changes before starting a cloud build.");
            var head = EasyItchPushProcess.Run("git", "rev-parse HEAD");
            if (!head.Succeeded) throw new InvalidOperationException("Could not read local Git HEAD.");
            var remote = Gh("api " + EasyItchPushProcess.Quote(
                "repos/" + settings.githubRepository.Trim() + "/branches/" + settings.githubBranch.Trim()) +
                " --jq .commit.sha");
            if (!string.Equals(head.Output.Trim(), remote.Output.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Local HEAD is not the GitHub branch tip. Push your changes first.");
            return head.Output.Trim();
        }

        public static EasyItchPushBuildResult Build(EasyItchPushSettings settings, string profileName)
        {
            if (IsBusy) throw new InvalidOperationException("A cloud build is already running.");
            IsBusy = true;
            LastRunUrl = "";
            try
            {
                var asset = EasyItchPushBuildProfiles.FindAllProfileAssets()
                    .FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
                var profile = asset == null ? null : EasyItchPushBuildProfiles.LoadProfile(asset.Path);
                if (profile == null || EasyItchPushBuildProfiles.GetBuildTarget(profile) != BuildTarget.StandaloneOSX)
                    throw new InvalidOperationException("Cloud builds currently support macOS Build Profiles only.");
                Status = "Checking committed source...";
                var head = ValidateSource(settings);
                var repoArg = " -R " + EasyItchPushProcess.Quote(settings.githubRepository.Trim());
                var requestId = Guid.NewGuid().ToString("N").Substring(0, 16);
                Status = "Starting macOS runner...";
                Gh("workflow run " + EasyItchPushProcess.Quote(settings.githubWorkflow) + repoArg +
                    " --ref " + EasyItchPushProcess.Quote(settings.githubBranch) +
                    " -f " + EasyItchPushProcess.Quote("profile=" + profileName) +
                    " -f " + EasyItchPushProcess.Quote("version=" + settings.ResolvedVersion) +
                    " -f " + EasyItchPushProcess.Quote("request_id=" + requestId));

                var run = FindRun(settings, repoArg, head, requestId);
                LastRunUrl = run.url;
                Status = "Building on macOS... " + LastRunUrl;
                Gh("run watch " + run.databaseId + " --exit-status" + repoArg);

                // Get the final conclusion as well as the CLI's exit status.
                var result = Gh("run view " + run.databaseId + repoArg +
                    " --json conclusion --jq .conclusion");
                if (!string.Equals(result.Output.Trim(), "success", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("macOS workflow did not succeed: " + result.Output.Trim());

                Status = "Downloading macOS artifact...";
                var staging = Path.Combine("Library", "EasyItchPush", "GitHub", requestId);
                Directory.CreateDirectory(staging);
                Gh("run download " + run.databaseId + repoArg + " --name " +
                    EasyItchPushProcess.Quote("easy-itch-push-" + requestId) +
                    " --dir " + EasyItchPushProcess.Quote(Path.GetFullPath(staging)));

                var candidates = Directory.GetFiles(staging, "*.zip", SearchOption.AllDirectories)
                    .Where(path =>
                        Path.GetFileName(path).IndexOf("-" + profileName + "-" +
                            settings.ResolvedVersionWithPrefix + ".zip", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();
                if (candidates.Length != 1)
                    throw new InvalidDataException("Expected one matching versioned macOS archive; found " + candidates.Length + ".");

                var channel = settings.GetChannelForProfileNoSync(profileName);
                var directory = settings.GetBuildDirectory(channel);
                Directory.CreateDirectory(directory);
                var target = Path.Combine(directory, Path.GetFileName(candidates[0]));
                File.Copy(candidates[0], target, true);
                Status = "macOS build ready";
                return new EasyItchPushBuildResult
                {
                    ProfileName = profileName,
                    Channel = channel,
                    ArchivePath = target,
                    OutputPath = directory,
                    Result = BuildResult.Succeeded,
                    TotalSize = (ulong)new FileInfo(target).Length
                };
            }
            catch (Exception ex)
            {
                Status = "Cloud build failed: " + ex.Message;
                EasyItchPushLog.Error(Status);
                throw;
            }
            finally { IsBusy = false; }
        }

        private static RunInfo FindRun(EasyItchPushSettings settings, string repoArg, string head, string id)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var output = Gh("run list" + repoArg +
                    " --workflow " + EasyItchPushProcess.Quote(settings.githubWorkflow) +
                    " --branch " + EasyItchPushProcess.Quote(settings.githubBranch) +
                    " --event workflow_dispatch --limit 75" +
                    " --json databaseId,displayTitle,headSha,status,conclusion,url");
                var parsed = JsonUtility.FromJson<RunList>("{\"runs\":" + output.Output.Trim() + "}");
                var run = parsed?.runs?.FirstOrDefault(item =>
                    item.displayTitle != null && item.displayTitle.Contains(id) &&
                    string.Equals(item.headSha, head, StringComparison.OrdinalIgnoreCase));
                if (run != null) return run;
                Status = "Waiting for GitHub runner...";
                Thread.Sleep(3000);
            }
            throw new TimeoutException("GitHub did not create the workflow run within 150 seconds.");
        }
    }
}
