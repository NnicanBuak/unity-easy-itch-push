using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace EasyItchPush.Editor
{
    [Serializable]
    internal sealed class EasyItchPushReleaseRecord
    {
        public string id;
        public string publishedAtUtc;
        public string version;
        public string pushMode;
        public string itchTarget;
        public string gamePageUrl;
        public string channels;
        public string archiveNames;
        public string changelog;
        public int publishedCount;
        public int expectedCount;
        public bool completed;
    }

    [Serializable]
    internal sealed class EasyItchPushReleaseHistoryData
    {
        public List<EasyItchPushReleaseRecord> records = new List<EasyItchPushReleaseRecord>();
    }

    /// <summary>
    /// Persistent project-scoped publishing log. Does not store itch.io credentials.
    /// One record is appended per attempted multi-channel Butler publication,
    /// including partial pushes, so they cannot disappear from release history.
    /// </summary>
    internal static class EasyItchPushReleaseHistory
    {
        public const string FilePath = "ProjectSettings/EasyItchPushReleaseHistory.json";
        private static EasyItchPushReleaseHistoryData cached;

        public static IReadOnlyList<EasyItchPushReleaseRecord> GetRecords(bool reload = false)
        {
            if (cached == null || reload)
                cached = Load();
            return cached.records;
        }

        public static EasyItchPushReleaseRecord Latest
        {
            get
            {
                var records = GetRecords();
                return records.Count > 0 ? records[0] : null;
            }
        }

        public static void Record(
            EasyItchPushSettings settings,
            EasyItchPushMode mode,
            IEnumerable<EasyItchPushPushArtifact> artifacts,
            int publishedCount,
            bool completed)
        {
            var items = artifacts == null
                ? new List<EasyItchPushPushArtifact>()
                : artifacts.Where(item => item != null).ToList();
            if (items.Count == 0 || publishedCount == 0)
                return; // No remote change occurred: nothing to record as a release.

            var changeText = string.Empty;
            if (!EasyItchPushChangelog.TryGetVersionChangelogMarkdown(
                    settings.ResolvedVersion, out changeText, out var error))
                EasyItchPushLog.Warning("Cannot snapshot release notes for history: " + error);

            var record = new EasyItchPushReleaseRecord
            {
                id = Guid.NewGuid().ToString("N"),
                publishedAtUtc = DateTime.UtcNow.ToString("o"),
                version = settings.ResolvedVersionWithPrefix,
                pushMode = settings.GetPushModeLabel(mode),
                itchTarget = settings.GetItchGameTarget(mode),
                gamePageUrl = settings.GetGamePageUrl(mode),
                channels = string.Join(", ", items.Take(publishedCount).Select(a => a.RemoteChannel)),
                archiveNames = string.Join(", ", items.Take(publishedCount).Select(a => Path.GetFileName(a.ArchivePath))),
                changelog = changeText,
                publishedCount = publishedCount,
                expectedCount = items.Count,
                completed = completed && publishedCount == items.Count
            };

            var data = Load(); // Re-read on every write to avoid overwriting external changes.
            data.records.Insert(0, record);
            Save(data);
            cached = data;
            EasyItchPushLog.Info("Saved release history entry: " + record.version +
                " (" + record.pushMode + ", " + record.publishedCount + "/" + record.expectedCount + ").");
        }

        private static EasyItchPushReleaseHistoryData Load()
        {
            if (!File.Exists(FilePath))
                return new EasyItchPushReleaseHistoryData();

            try
            {
                var result = JsonUtility.FromJson<EasyItchPushReleaseHistoryData>(
                    File.ReadAllText(FilePath, Encoding.UTF8));
                if (result == null)
                    throw new InvalidDataException("Empty release history document.");
                if (result.records == null)
                    result.records = new List<EasyItchPushReleaseRecord>();
                return result;
            }
            catch (Exception exception)
            {
                EasyItchPushLog.Error("Could not load release history; refusing to overwrite it: " + exception.Message);
                throw;
            }
        }

        private static void Save(EasyItchPushReleaseHistoryData data)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true), new UTF8Encoding(false));

            try
            {
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(temp, FilePath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temp, FilePath, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, FilePath);
                }
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}
