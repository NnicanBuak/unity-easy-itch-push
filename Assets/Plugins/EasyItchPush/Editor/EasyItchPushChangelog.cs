using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;

namespace EasyItchPush.Editor
{
    [InitializeOnLoad]
    internal static class EasyItchPushChangelogBootstrap
    {
        static EasyItchPushChangelogBootstrap()
        {
            EditorApplication.delayCall += EasyItchPushChangelog.EnsureProjectChangelogExists;
        }
    }

    internal static class EasyItchPushChangelog
    {
        public const string ProjectChangelogAssetPath = "Assets/CHANGELOG.md";
        public const string BuildChangelogFileName = "CHANGELOG.md";
        private const string DefaultVersionNoteLine = "- Add release notes for this version here.";

        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public static void EnsureProjectChangelogExists()
        {
            var changelogPath = GetProjectChangelogPath();
            if (File.Exists(changelogPath))
            {
                return;
            }

            var directoryPath = Path.GetDirectoryName(changelogPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var initialVersion = EasyItchPushSettings.Instance.ResolvedVersion;
            File.WriteAllText(changelogPath, BuildDefaultChangelog(initialVersion), Utf8WithoutBom);
            AssetDatabase.ImportAsset(ProjectChangelogAssetPath, ImportAssetOptions.ForceSynchronousImport);
            EasyItchPushLog.Info($"Created project changelog at {ProjectChangelogAssetPath}");
        }

        public static string GetVersionChangelogMarkdownOrThrow(string expectedVersion)
        {
            if (TryGetVersionChangelogMarkdown(expectedVersion, out var markdown, out var errorMessage))
            {
                return markdown;
            }

            throw new InvalidDataException(errorMessage);
        }

        public static bool TryGetVersionChangelogMarkdown(
            string expectedVersion,
            out string markdown,
            out string errorMessage)
        {
            markdown = string.Empty;
            errorMessage = string.Empty;

            var changelogPath = GetProjectChangelogPath();
            if (!File.Exists(changelogPath))
            {
                errorMessage = $"Required changelog file was not found at {changelogPath}.";
                return false;
            }

            var changelogText = File.ReadAllText(changelogPath);
            if (!TryExtractVersionSection(changelogText, expectedVersion, out var versionSection))
            {
                errorMessage =
                    $"Changelog {ProjectChangelogAssetPath} does not contain the expected version header '## v{expectedVersion}'.";
                return false;
            }

            markdown = "# Changelog\n\n" + versionSection + Environment.NewLine;
            return true;
        }

        public static void EnsureVersionSectionExists(string expectedVersion)
        {
            EnsureProjectChangelogExists();

            var changelogPath = GetProjectChangelogPath();
            var changelogText = NormalizeLineEndings(File.ReadAllText(changelogPath));
            if (TryFindVersionSection(changelogText, expectedVersion, out _, out _, out _))
            {
                return;
            }

            var updatedText = AppendVersionSection(changelogText, BuildVersionSection(expectedVersion, DefaultVersionNoteLine));
            File.WriteAllText(changelogPath, updatedText, Utf8WithoutBom);
        }

        public static string GetVersionNotes(string expectedVersion)
        {
            EnsureVersionSectionExists(expectedVersion);

            var changelogText = NormalizeLineEndings(File.ReadAllText(GetProjectChangelogPath()));
            if (!TryFindVersionSection(changelogText, expectedVersion, out _, out var contentStartIndex, out var sectionEndIndex))
            {
                return string.Empty;
            }

            return changelogText.Substring(contentStartIndex, sectionEndIndex - contentStartIndex).Trim();
        }

        public static void SetVersionNotes(string expectedVersion, string notes)
        {
            EnsureProjectChangelogExists();

            var changelogPath = GetProjectChangelogPath();
            var changelogText = NormalizeLineEndings(File.ReadAllText(changelogPath));
            var replacementSection = BuildVersionSection(expectedVersion, notes);
            string updatedText;

            if (TryFindVersionSection(changelogText, expectedVersion, out var sectionStartIndex, out _, out var sectionEndIndex))
            {
                var prefix = changelogText.Substring(0, sectionStartIndex).TrimEnd();
                var suffix = changelogText.Substring(sectionEndIndex).TrimStart();
                updatedText = CombineSections(prefix, replacementSection, suffix);
            }
            else
            {
                updatedText = AppendVersionSection(changelogText, replacementSection);
            }

            File.WriteAllText(changelogPath, updatedText, Utf8WithoutBom);
        }

        public static void WriteVersionChangelogToBuildDirectory(string buildDirectory, string expectedVersion)
        {
            var changelogPath = GetBuildChangelogPath(buildDirectory);
            var changelogMarkdown = GetVersionChangelogMarkdownOrThrow(expectedVersion);
            File.WriteAllText(changelogPath, changelogMarkdown, Utf8WithoutBom);
        }

        public static void WriteVersionChangelogToArchive(string archivePath, string expectedVersion)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("Build archive was not found.", archivePath);
            }

            var changelogMarkdown = GetVersionChangelogMarkdownOrThrow(expectedVersion);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                DeleteExistingChangelogEntries(archive);

                var changelogEntry = archive.CreateEntry(BuildChangelogFileName, CompressionLevel.Optimal);
                using (var changelogStream = changelogEntry.Open())
                using (var changelogWriter = new StreamWriter(changelogStream, Utf8WithoutBom))
                {
                    changelogWriter.Write(changelogMarkdown);
                }
            }
        }

        public static bool IsBuildChangelogFile(string buildDirectory, string filePath)
        {
            var expectedPath = GetBuildChangelogPath(buildDirectory);
            return string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDefaultChangelog(string initialVersion)
        {
            return
                "# Changelog\n\n" +
                "Easy Itch Push uses this file as the single source of truth for release notes.\n\n" +
                "## How To Use\n\n" +
                "- Keep all release notes in this one file.\n" +
                "- Add one section per version using an exact header like `## v1.2.3` or `## v1.2.3-hotfix1`.\n" +
                "- The section header must match the version configured in `Project Settings > Easy Itch Push`.\n" +
                "- Edit the current version notes directly in the Easy Itch Push window or in this file.\n" +
                "- During build, Easy Itch Push copies only the current version section into the generated build `CHANGELOG.md`.\n\n" +
                BuildVersionSection(initialVersion, DefaultVersionNoteLine);
        }

        private static string GetProjectChangelogPath()
        {
            return Path.GetFullPath(ProjectChangelogAssetPath);
        }

        private static string GetBuildChangelogPath(string buildDirectory)
        {
            return Path.Combine(Path.GetFullPath(buildDirectory), BuildChangelogFileName);
        }

        private static void DeleteExistingChangelogEntries(ZipArchive archive)
        {
            for (var i = archive.Entries.Count - 1; i >= 0; i--)
            {
                var entry = archive.Entries[i];
                if (!string.Equals(entry.FullName, BuildChangelogFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entry.Delete();
            }
        }

        private static bool TryExtractVersionSection(string changelogText, string expectedVersion, out string versionSection)
        {
            versionSection = string.Empty;

            var normalizedText = NormalizeLineEndings(changelogText);
            if (!TryFindVersionSection(normalizedText, expectedVersion, out var startIndex, out _, out var nextHeaderIndex))
            {
                return false;
            }

            versionSection = normalizedText.Substring(startIndex, nextHeaderIndex - startIndex).Trim();
            return true;
        }

        private static bool TryFindVersionSection(
            string changelogText,
            string expectedVersion,
            out int sectionStartIndex,
            out int contentStartIndex,
            out int sectionEndIndex)
        {
            sectionStartIndex = -1;
            contentStartIndex = -1;
            sectionEndIndex = -1;

            var expectedHeader = $"## v{expectedVersion}";
            sectionStartIndex = FindExactHeaderLine(changelogText, expectedHeader);
            if (sectionStartIndex < 0)
            {
                return false;
            }

            var headerEndIndex = sectionStartIndex + expectedHeader.Length;
            contentStartIndex = headerEndIndex;
            if (contentStartIndex < changelogText.Length && changelogText[contentStartIndex] == '\n')
            {
                contentStartIndex++;
            }

            if (contentStartIndex < changelogText.Length && changelogText[contentStartIndex] == '\n')
            {
                contentStartIndex++;
            }

            sectionEndIndex = FindNextVersionHeaderLine(changelogText, headerEndIndex);
            if (sectionEndIndex < 0)
            {
                sectionEndIndex = changelogText.Length;
            }

            return true;
        }

        private static string BuildVersionSection(string version, string notes)
        {
            var normalizedNotes = NormalizeLineEndings(notes).Trim();
            if (string.IsNullOrWhiteSpace(normalizedNotes))
            {
                normalizedNotes = DefaultVersionNoteLine;
            }

            return $"## v{version}\n\n{normalizedNotes}\n";
        }

        private static string AppendVersionSection(string changelogText, string versionSection)
        {
            var normalized = changelogText.TrimEnd();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return versionSection.TrimEnd() + "\n";
            }

            return normalized + "\n\n" + versionSection.TrimEnd() + "\n";
        }

        private static string CombineSections(string prefix, string currentSection, string suffix)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                builder.Append(prefix.TrimEnd());
                builder.Append("\n\n");
            }

            builder.Append(currentSection.TrimEnd());

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                builder.Append("\n\n");
                builder.Append(suffix.TrimStart());
            }

            builder.Append('\n');
            return builder.ToString();
        }

        private static int FindExactHeaderLine(string text, string header)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var candidateIndex = text.IndexOf(header, searchIndex, StringComparison.Ordinal);
                if (candidateIndex < 0)
                {
                    return -1;
                }

                var isLineStart = candidateIndex == 0 || text[candidateIndex - 1] == '\n';
                var lineEndIndex = candidateIndex + header.Length;
                var isLineEnd = lineEndIndex == text.Length || text[lineEndIndex] == '\n';
                if (isLineStart && isLineEnd)
                {
                    return candidateIndex;
                }

                searchIndex = candidateIndex + header.Length;
            }

            return -1;
        }

        private static int FindNextVersionHeaderLine(string text, int searchIndex)
        {
            var lineStart = searchIndex <= 0 ? 0 : searchIndex;
            if (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                var currentLineEnd = text.IndexOf('\n', lineStart);
                if (currentLineEnd < 0)
                {
                    return -1;
                }

                lineStart = currentLineEnd + 1;
            }

            while (lineStart < text.Length)
            {
                var lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                var lineLength = lineEnd - lineStart;
                if (lineLength >= 4 &&
                    string.CompareOrdinal(text, lineStart, "## v", 0, 4) == 0)
                {
                    return lineStart;
                }

                lineStart = lineEnd + 1;
            }

            return -1;
        }

        private static string NormalizeLineEndings(string text)
        {
            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }
    }
}
