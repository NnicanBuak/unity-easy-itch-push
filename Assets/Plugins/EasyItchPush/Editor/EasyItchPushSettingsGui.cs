using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushSettingsGui
    {
        private const double SaveDebounceDelaySeconds = 0.3d;
        private const double ChangelogSaveDebounceDelaySeconds = 0.3d;
        private const float ChangelogTextAreaHeight = 180f;
        private const float ProfileDragHandleWidth = 42f;
        private const float ProfileDragHandleIconWidth = 20f;
        private const float ProfileToggleWidth = 18f;
        private const float ProfileRowSpacing = 4f;
        private static readonly string[] PushModeLabels = { "Release", "Test" };
        private static readonly GUIContent ProfileDragHandleFallbackIcon = EditorGUIUtility.IconContent("d_Grid.MoveTool");
        private static bool pendingSave;
        private static bool pendingChangelogSave;
        private static double lastEditTime;
        private static double lastChangelogEditTime;
        private static int dragSourceProfileIndex = -1;
        private static int dragInsertProfileIndex = -1;
        private static bool isDraggingProfileRow;
        private static Vector2 dragStartMousePosition;
        private static string cachedChangelogVersion = string.Empty;
        private static string cachedChangelogNotes = string.Empty;

        internal readonly struct DrawOptions
        {
            public readonly bool ShowPushModeSelector;
            public readonly bool ShowActions;

            public DrawOptions(bool showPushModeSelector, bool showActions = false)
            {
                ShowPushModeSelector = showPushModeSelector;
                ShowActions = showActions;
            }
        }

        private static void Defer(System.Action action) => EditorApplication.delayCall += () => action();

        private static int selectedTab;
        private static bool destinationsExpanded = true;
        private static bool platformsExpanded = true;
        private static bool cloudExpanded;
        private static bool buildOptionsExpanded;
        private static bool toolsExpanded;
        private static string expandedHistoryId = string.Empty;
        private static readonly string[] Tabs = { "Publish", "Changelog", "History", "Settings" };

        public static bool Draw(EasyItchPushSettings settings, DrawOptions options)
        {
            settings.SyncProfileMappingsWithBuildProfiles();
            EditorGUI.BeginChangeCheck();
            var reordered = false;

            using (new EditorGUILayout.HorizontalScope())
            {
                selectedTab = GUILayout.Toolbar(selectedTab, Tabs, GUILayout.Height(32));
            }

            EditorGUILayout.Space(14);
            switch (selectedTab)
            {
                case 0:
                    DrawPublishTab(settings, options);
                    break;
                case 1:
                    DrawChangelogTab(settings);
                    break;
                case 2:
                    DrawHistoryTab();
                    break;
                case 3:
                    reordered = DrawSettingsTab(settings);
                    break;
            }

            if (!EditorGUI.EndChangeCheck() && !reordered)
                return false;
            ScheduleSave();
            return true;
        }

        private static void DrawHeading(string title, string subtitle)
        {
            EditorGUILayout.LabelField(title, EditorStyles.largeLabel);
            if (!string.IsNullOrEmpty(subtitle))
                EditorGUILayout.LabelField(subtitle, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);
        }

        private static void DrawPublishTab(EasyItchPushSettings settings, DrawOptions options)
        {
            DrawHeading("Publish a build", "Choose the destination and version, then build and upload your selected profiles.");
            var wide = EditorGUIUtility.currentViewWidth >= 870f;
            if (wide)
                EditorGUILayout.BeginHorizontal();

            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(300)))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
                    if (options.ShowPushModeSelector)
                        DrawPushModeToolbar(settings);
                    EditorGUILayout.Space(6);
                    var mode = settings.PushMode;
                    EditorGUILayout.LabelField(settings.HasItchTarget(mode)
                        ? settings.GetItchGameTarget(mode) : "No itch.io destination configured", EditorStyles.boldLabel);
                    var url = settings.GetGamePageUrl(mode);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(url, EditorStyles.wordWrappedMiniLabel);
                            if (GUILayout.Button("Open page", GUILayout.Width(94)))
                                Application.OpenURL(url);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Configure your itch.io target under Settings.", MessageType.Warning);
                    }
                }

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Build profiles", EditorStyles.boldLabel);
                        if (GUILayout.Button("Configure", GUILayout.Width(92)))
                            selectedTab = 3;
                    }
                    var count = 0;
                    foreach (var mapping in settings.profileChannelMappings ?? System.Array.Empty<EasyItchPushProfileMapping>())
                    {
                        if (mapping == null || !mapping.IsEnabled(settings.PushMode))
                            continue;
                        count++;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(mapping.profileName, GUILayout.MinWidth(90));
                            GUILayout.FlexibleSpace();
                            var location = mapping.buildLocation == EasyItchPushBuildLocation.GitHubActions
                                ? "GitHub Actions" : "Local";
                            EditorGUILayout.LabelField(location, EditorStyles.miniLabel, GUILayout.Width(110));
                            EditorGUILayout.LabelField(settings.GetChannelForProfileNoSync(mapping.profileName),
                                EditorStyles.miniLabel, GUILayout.Width(115));
                        }
                    }
                    if (count == 0)
                        EditorGUILayout.HelpBox("No profiles enabled for this destination. Configure them under Settings.", MessageType.Warning);
                }

                if (options.ShowActions)
                {
                    EditorGUILayout.Space(12);
                    var oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.44f, 0.68f, 0.96f);
                    if (GUILayout.Button("Build & Publish (" + settings.GetPushModeLabel(settings.PushMode) + ")",
                            GUILayout.Height(46)))
                        Defer(EasyItchPushActions.BuildAllProfilesAndPush);
                    GUI.backgroundColor = oldColor;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Build only", GUILayout.Height(30)))
                            Defer(EasyItchPushActions.BuildAllProfiles);
                        if (GUILayout.Button("Publish existing", GUILayout.Height(30)))
                            Defer(EasyItchPushActions.PushExistingBuilds);
                    }
                }
            }

            if (wide)
                GUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(wide ? GUILayout.Width(340) : GUILayout.ExpandWidth(true)))
            {
                DrawVersionPanel(settings);

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Release notes", EditorStyles.boldLabel);
                        if (GUILayout.Button("Edit", GUILayout.Width(72)))
                            selectedTab = 1;
                    }
                    LoadCachedChangelogForVersion(settings.ResolvedVersion, false);
                    var notes = cachedChangelogNotes ?? string.Empty;
                    notes = notes.Trim();
                    if (notes.Length > 220)
                        notes = notes.Substring(0, 220).TrimEnd() + "...";
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(notes) ? "No release notes yet." : notes,
                        EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(36));
                }

                EditorGUILayout.Space(8);
                DrawLatestReleaseSummary(settings);

                if (options.ShowActions)
                {
                    EditorGUILayout.Space(8);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Update archives"))
                            Defer(EasyItchPushActions.UpdateExistingBuildChangelogs);
                        if (GUILayout.Button("Open builds folder"))
                            Defer(EasyItchPushActions.OpenBuildFolder);
                    }

                    if (!string.IsNullOrEmpty(EasyItchPushGitHub.LastRunUrl))
                    {
                        EditorGUILayout.Space(6);
                        EditorGUILayout.LabelField(EasyItchPushGitHub.Status, EditorStyles.wordWrappedMiniLabel);
                        if (GUILayout.Button("Open cloud build log"))
                            Application.OpenURL(EasyItchPushGitHub.LastRunUrl);
                    }
                }
            }

            if (wide)
                EditorGUILayout.EndHorizontal();
        }

        private static void DrawVersionPanel(EasyItchPushSettings settings)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Version", EditorStyles.boldLabel);
                var versionBeforeEdit = settings.CurrentVersion;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField("Major", EditorStyles.miniLabel);
                        settings.versionMajor = Mathf.Max(0, EditorGUILayout.IntField(settings.versionMajor));
                    }
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField("Minor", EditorStyles.miniLabel);
                        settings.versionMinor = Mathf.Max(0, EditorGUILayout.IntField(settings.versionMinor));
                    }
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField("Patch", EditorStyles.miniLabel);
                        settings.versionPatch = Mathf.Max(0, EditorGUILayout.IntField(settings.versionPatch));
                    }
                }

                settings.versionIsHotfix = EditorGUILayout.ToggleLeft("Hotfix", settings.versionIsHotfix);
                if (settings.versionIsHotfix)
                    settings.versionHotfixNumber = Mathf.Max(1,
                        EditorGUILayout.IntField("Hotfix number", settings.versionHotfixNumber));
                EditorGUILayout.LabelField("Current: " + settings.ResolvedVersionWithPrefix, EditorStyles.miniBoldLabel);

                var didVersionChange = DidVersionChange(versionBeforeEdit, settings.CurrentVersion);
                if (didVersionChange)
                    PersistVersionChange(settings);
            }
        }

        private static void DrawLatestReleaseSummary(EasyItchPushSettings settings)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Latest publication", EditorStyles.boldLabel);
                    if (GUILayout.Button("History", GUILayout.Width(74)))
                        selectedTab = 2;
                }
                var recent = EasyItchPushReleaseHistory.Latest;
                if (recent == null)
                {
                    EditorGUILayout.LabelField("No recorded publications yet.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }
                EditorGUILayout.LabelField(recent.version + "   ·   " + recent.pushMode,
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(FormatPublishedDate(recent.publishedAtUtc),
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(recent.completed ? "Published to all channels" :
                        "Partial upload: " + recent.publishedCount + "/" + recent.expectedCount,
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawChangelogTab(EasyItchPushSettings settings)
        {
            DrawHeading("Release notes", "Edit the changelog for " + settings.ResolvedVersionWithPrefix +
                ". Previous published notes are preserved in History.");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawCurrentVersionChangelog(settings, false);
            }
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Back to Publish", GUILayout.Width(145)))
                {
                    FlushPendingChangelogSave();
                    selectedTab = 0;
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open CHANGELOG.md", GUILayout.Width(160)))
                    EditorUtility.RevealInFinder(EasyItchPushChangelog.ProjectChangelogAssetPath);
            }
        }

        private static void DrawHistoryTab()
        {
            DrawHeading("Release history", "Each successful or partially completed Butler publication is saved with its original release notes.");
            var entries = EasyItchPushReleaseHistory.GetRecords();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(entries.Count + " recorded publication(s)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                    EasyItchPushReleaseHistory.GetRecords(true);
            }
            EditorGUILayout.Space(8);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No publication history has been recorded yet. Your next upload through Easy Itch Push will appear here. Existing itch.io releases are not imported automatically.",
                    MessageType.Info);
                return;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var expanded = expandedHistoryId == entry.id;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var title = entry.version + "   ·   " + entry.pushMode;
                        var updated = EditorGUILayout.Foldout(expanded, title, true);
                        if (updated != expanded)
                            expandedHistoryId = updated ? entry.id : string.Empty;
                        GUILayout.FlexibleSpace();
                        var state = entry.completed ? "Published" :
                            "Partial (" + entry.publishedCount + "/" + entry.expectedCount + ")";
                        EditorGUILayout.LabelField(state, EditorStyles.miniBoldLabel, GUILayout.Width(112));
                    }
                    EditorGUILayout.LabelField(FormatPublishedDate(entry.publishedAtUtc) + "   ·   " +
                        entry.itchTarget, EditorStyles.wordWrappedMiniLabel);
                    if (expanded)
                    {
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);
                        EditorGUILayout.SelectableLabel(entry.channels ?? string.Empty,
                            EditorStyles.wordWrappedLabel, GUILayout.MinHeight(20));
                        EditorGUILayout.LabelField("Archived files", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(entry.archiveNames ?? string.Empty, EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.Space(6);
                        EditorGUILayout.LabelField("Published changelog (snapshot)", EditorStyles.boldLabel);
                        var notes = string.IsNullOrWhiteSpace(entry.changelog) ? "No notes were available at publication time." : entry.changelog;
                        EditorGUILayout.TextArea(notes, GUILayout.MinHeight(100));
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Copy changelog", GUILayout.Width(130)))
                                EditorGUIUtility.systemCopyBuffer = notes;
                            if (!string.IsNullOrWhiteSpace(entry.gamePageUrl) &&
                                GUILayout.Button("Open itch.io page", GUILayout.Width(150)))
                                Application.OpenURL(entry.gamePageUrl);
                        }
                    }
                }
                EditorGUILayout.Space(4);
            }
        }

        private static string FormatPublishedDate(string utc)
        {
            if (System.DateTime.TryParse(utc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return utc ?? "Unknown date";
        }

        private static bool DrawSettingsTab(EasyItchPushSettings settings)
        {
            DrawHeading("Project setup", "Configure where the game is built, and the tools used for publication.");
            var reordered = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                destinationsExpanded = EditorGUILayout.Foldout(destinationsExpanded,
                    "itch.io destinations", true, EditorStyles.foldoutHeader);
                if (destinationsExpanded)
                {
                    DrawPushModeToolbar(settings);
                    EditorGUILayout.Space(6);
                    DrawActiveTargetFields(settings);
                }
            }
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                platformsExpanded = EditorGUILayout.Foldout(platformsExpanded,
                    "Build profiles & channels", true, EditorStyles.foldoutHeader);
                if (platformsExpanded)
                {
                    EditorGUILayout.Space(6);
                    reordered = DrawProfileMappings(settings);
                    DrawProfileValidation(settings);
                }
            }
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                cloudExpanded = EditorGUILayout.Foldout(cloudExpanded,
                    "GitHub Actions (macOS)", true, EditorStyles.foldoutHeader);
                if (cloudExpanded)
                {
                    EditorGUILayout.HelpBox(
                        "Requires GitHub CLI (gh auth login), a committed project and GameCI licensing secrets. The workflow is generated in your game project.",
                        MessageType.None);
                    settings.githubRepository = EditorGUILayout.TextField("Repository", settings.githubRepository);
                    settings.githubBranch = EditorGUILayout.TextField("Branch", settings.githubBranch);
                    settings.githubWorkflow = EditorGUILayout.TextField("Workflow", settings.githubWorkflow);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Generate workflow"))
                            Defer(EasyItchPushGitHub.InstallWorkflow);
                        if (GUILayout.Button("Install GitHub CLI"))
                            Application.OpenURL("https://cli.github.com/");
                        if (GUILayout.Button("Check login"))
                            Defer(() => EasyItchPushProcess.Run("gh", "auth status"));
                    }
                    EditorGUILayout.LabelField("Authenticate in a terminal: gh auth login",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                buildOptionsExpanded = EditorGUILayout.Foldout(buildOptionsExpanded,
                    "Advanced build options", true, EditorStyles.foldoutHeader);
                if (buildOptionsExpanded)
                {
                    settings.outputRoot = EditorGUILayout.TextField("Output folder", settings.outputRoot);
                    settings.developmentBuild = EditorGUILayout.Toggle("Development build", settings.developmentBuild);
                    settings.allowDebugging = EditorGUILayout.Toggle("Allow debugging", settings.allowDebugging);
                    settings.strictMode = EditorGUILayout.Toggle("Strict mode", settings.strictMode);
                    settings.detailedBuildReport = EditorGUILayout.Toggle("Detailed report", settings.detailedBuildReport);
                    settings.cleanBuildDirectory = EditorGUILayout.Toggle("Clean before build", settings.cleanBuildDirectory);
                    settings.compressWithLz4HC = EditorGUILayout.Toggle("LZ4HC compression", settings.compressWithLz4HC);
                }
            }
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                toolsExpanded = EditorGUILayout.Foldout(toolsExpanded,
                    "Publishing tools", true, EditorStyles.foldoutHeader);
                if (toolsExpanded)
                {
                    var executable = EasyItchPushButler.ResolveExecutable(settings);
                    EditorGUILayout.LabelField("Butler", string.IsNullOrEmpty(executable) ? "Not installed" : "Ready");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Install / Update Butler"))
                            Defer(EasyItchPushActions.InstallOrUpgradeButler);
                        if (GUILayout.Button("itch.io login"))
                            Defer(EasyItchPushActions.Login);
                        if (GUILayout.Button("Check status"))
                            Defer(EasyItchPushActions.Status);
                    }
                    settings.openGamePageOnSuccess = EditorGUILayout.Toggle("Open page after push", settings.openGamePageOnSuccess);
                    settings.playSoundOnComplete = EditorGUILayout.Toggle("Completion sound", settings.playSoundOnComplete);
                }
            }
            return reordered;
        }

        public static void EnsureSettingsAreSynchronized(EasyItchPushSettings settings)
        {
            EasyItchPushChangelog.EnsureProjectChangelogExists();
            FlushPendingSave(settings);
            FlushPendingChangelogSave();

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

        private static bool DrawProfileMappings(EasyItchPushSettings settings)
        {
            EditorGUILayout.LabelField("Build Profiles", EditorStyles.boldLabel);
            if (settings.profileChannelMappings == null || settings.profileChannelMappings.Length == 0)
            {
                EditorGUILayout.HelpBox("Create Unity Build Profiles first. Selected profiles here will be built and pushed.", MessageType.Info);
                return false;
            }

            EditorGUILayout.LabelField("Select platforms and where each one is built. Drag to reorder.", EditorStyles.wordWrappedMiniLabel);

            var macOsProfileNames = GetMacOsProfileNames();
            var shouldWarnAboutMacOsMonoFallback = Application.platform != RuntimePlatform.OSXEditor;
            var pushMode = settings.PushMode;
            var didReorder = false;
            var currentEvent = Event.current;
            Rect? firstRowRect = null;
            Rect? lastRowRect = null;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(ProfileDragHandleWidth + ProfileToggleWidth + (ProfileRowSpacing * 2f));
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

                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                if (!firstRowRect.HasValue)
                {
                    firstRowRect = rowRect;
                }
                lastRowRect = rowRect;

                var fieldWidth = rowRect.width - ProfileDragHandleWidth - ProfileToggleWidth - (ProfileRowSpacing * 3f);
                var profileWidth = Mathf.Max(120f, fieldWidth * 0.45f);
                var channelWidth = Mathf.Max(120f, fieldWidth - profileWidth);
                var handleRect = new Rect(rowRect.x, rowRect.y, ProfileDragHandleWidth, rowRect.height);
                var toggleRect = new Rect(handleRect.xMax + ProfileRowSpacing, rowRect.y, ProfileToggleWidth, rowRect.height);
                var profileRect = new Rect(toggleRect.xMax + ProfileRowSpacing, rowRect.y, profileWidth, rowRect.height);
                var channelRect = new Rect(profileRect.xMax + ProfileRowSpacing, rowRect.y, channelWidth, rowRect.height);

                EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);

                if (isDraggingProfileRow && currentEvent.type == EventType.Repaint && dragSourceProfileIndex == i)
                {
                    EditorGUI.DrawRect(rowRect, new Color(0.35f, 0.55f, 0.85f, 0.12f));
                }

                DrawProfileInsertMarker(rowRect, i);

                DrawProfileDragHandle(handleRect);

                var isEnabled = mapping.IsEnabled(pushMode);
                var updatedIsEnabled = EditorGUI.Toggle(toggleRect, isEnabled);
                if (updatedIsEnabled != isEnabled)
                {
                    mapping.SetEnabled(pushMode, updatedIsEnabled);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.TextField(profileRect, mapping.profileName);
                }

                mapping.channel = EditorGUI.TextField(channelRect, mapping.channel);
                if (macOsProfileNames.Contains(mapping.profileName))
                {
                    EditorGUI.indentLevel++;
                    var location = (int)mapping.buildLocation;
                    var updated = EditorGUILayout.Popup("Build on", location,
                        new[] { "This computer", "GitHub Actions (macOS)" });
                    if (updated != location)
                        mapping.buildLocation = (EasyItchPushBuildLocation)updated;
                    EditorGUI.indentLevel--;
                }

                if (currentEvent.type == EventType.MouseDown &&
                    currentEvent.button == 0 &&
                    handleRect.Contains(currentEvent.mousePosition))
                {
                    dragSourceProfileIndex = i;
                    dragInsertProfileIndex = i;
                    isDraggingProfileRow = false;
                    dragStartMousePosition = currentEvent.mousePosition;
                    currentEvent.Use();
                }

                if (dragSourceProfileIndex == i &&
                    currentEvent.type == EventType.MouseDrag &&
                    currentEvent.button == 0 &&
                    (currentEvent.mousePosition - dragStartMousePosition).sqrMagnitude > 16f)
                {
                    isDraggingProfileRow = true;
                    currentEvent.Use();
                }

                if (isDraggingProfileRow)
                {
                    if (rowRect.Contains(currentEvent.mousePosition))
                    {
                        dragInsertProfileIndex = currentEvent.mousePosition.y <= rowRect.center.y ? i : i + 1;
                    }
                    else if (firstRowRect.HasValue && currentEvent.mousePosition.y < firstRowRect.Value.yMin)
                    {
                        dragInsertProfileIndex = 0;
                    }
                }

                if (mapping.IsEnabled(pushMode) &&
                    shouldWarnAboutMacOsMonoFallback &&
                    macOsProfileNames.Contains(mapping.profileName) &&
                    mapping.buildLocation == EasyItchPushBuildLocation.Local)
                {
                    EditorGUILayout.HelpBox(
                        "This macOS profile will be built with Mono on the current editor host. IL2CPP for macOS requires a macOS machine.",
                        MessageType.Warning);
                }
            }

            if (isDraggingProfileRow)
            {
                if (lastRowRect.HasValue && currentEvent.mousePosition.y > lastRowRect.Value.yMax)
                {
                    dragInsertProfileIndex = settings.profileChannelMappings.Length;
                }

                if (lastRowRect.HasValue)
                {
                    DrawTrailingProfileInsertMarker(lastRowRect.Value, settings.profileChannelMappings.Length);
                }
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && dragSourceProfileIndex >= 0)
            {
                if (isDraggingProfileRow)
                {
                    var targetIndex = Mathf.Clamp(dragInsertProfileIndex, 0, settings.profileChannelMappings.Length);
                    if (targetIndex > dragSourceProfileIndex)
                    {
                        targetIndex--;
                    }

                    if (targetIndex >= 0 &&
                        targetIndex < settings.profileChannelMappings.Length &&
                        settings.MoveProfileMapping(dragSourceProfileIndex, targetIndex))
                    {
                        didReorder = true;
                        GUI.changed = true;
                    }
                }

                ResetProfileDragState();
                currentEvent.Use();
            }

            if (currentEvent.type == EventType.Ignore || currentEvent.type == EventType.MouseLeaveWindow)
            {
                ResetProfileDragState();
            }

            return didReorder;
        }

        private static void DrawProfileInsertMarker(Rect rowRect, int rowIndex)
        {
            if (!isDraggingProfileRow || dragInsertProfileIndex != rowIndex)
            {
                return;
            }

            var markerRect = new Rect(rowRect.x, rowRect.y - 2f, rowRect.width, 2f);
            EditorGUI.DrawRect(markerRect, new Color(0.35f, 0.7f, 1f, 1f));
        }

        private static void DrawTrailingProfileInsertMarker(Rect lastRowRect, int rowCount)
        {
            if (!isDraggingProfileRow || dragInsertProfileIndex != rowCount)
            {
                return;
            }

            var markerRect = new Rect(lastRowRect.x, lastRowRect.yMax + 1f, lastRowRect.width, 2f);
            EditorGUI.DrawRect(markerRect, new Color(0.35f, 0.7f, 1f, 1f));
        }

        private static void ResetProfileDragState()
        {
            dragSourceProfileIndex = -1;
            dragInsertProfileIndex = -1;
            isDraggingProfileRow = false;
        }

        private static void DrawProfileDragHandle(Rect handleRect)
        {
            var dragHandleStyle = GUI.skin.FindStyle("RL DragHandle");
            var dragHandleIconRect = new Rect(
                handleRect.x + ((handleRect.width - ProfileDragHandleIconWidth) * 0.5f),
                handleRect.y,
                ProfileDragHandleIconWidth,
                handleRect.height);

            if (dragHandleStyle != null)
            {
                GUI.Label(dragHandleIconRect, GUIContent.none, dragHandleStyle);
                return;
            }

            GUI.Box(dragHandleIconRect, ProfileDragHandleFallbackIcon, EditorStyles.miniButton);
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
            var didFlushAnything = false;

            if (pendingChangelogSave &&
                EditorApplication.timeSinceStartup - lastChangelogEditTime >= ChangelogSaveDebounceDelaySeconds)
            {
                FlushPendingChangelogSave();
                didFlushAnything = true;
            }

            if (!pendingSave)
            {
                return didFlushAnything;
            }

            if (EditorApplication.timeSinceStartup - lastEditTime < SaveDebounceDelaySeconds)
            {
                return didFlushAnything;
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
            settings.SaveSettings();
            settings.SyncVersionToPlayerSettings();
            settings.SaveSettings();
        }

        public static void FlushPendingChangelogSave()
        {
            if (!pendingChangelogSave || string.IsNullOrWhiteSpace(cachedChangelogVersion))
            {
                return;
            }

            pendingChangelogSave = false;
            EasyItchPushChangelog.SetVersionNotes(cachedChangelogVersion, cachedChangelogNotes);
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

        private static void PersistVersionChange(EasyItchPushSettings settings)
        {
            FlushPendingChangelogSave();
            settings.SaveSettings();
            settings.SyncVersionToPlayerSettings();
            settings.SaveSettings();
            EasyItchPushChangelog.EnsureVersionSectionExists(settings.ResolvedVersion);
            LoadCachedChangelogForVersion(settings.ResolvedVersion, forceReload: true);
        }

        private static void DrawCurrentVersionChangelog(EasyItchPushSettings settings, bool didVersionChange)
        {
            var version = settings.ResolvedVersion;
            LoadCachedChangelogForVersion(version, forceReload: didVersionChange);

            EditorGUILayout.LabelField("Changelog", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Edits update the `## v{version}` section in Assets/CHANGELOG.md.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            var updatedNotes = EditorGUILayout.TextArea(cachedChangelogNotes, GUILayout.MinHeight(ChangelogTextAreaHeight));
            if (EditorGUI.EndChangeCheck())
            {
                cachedChangelogNotes = updatedNotes;
                pendingChangelogSave = true;
                lastChangelogEditTime = EditorApplication.timeSinceStartup;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Changelog"))
                {
                    FlushPendingChangelogSave();
                }

                if (GUILayout.Button("Reload Changelog"))
                {
                    pendingChangelogSave = false;
                    LoadCachedChangelogForVersion(version, forceReload: true);
                }
            }

            if (pendingChangelogSave && string.Equals(cachedChangelogVersion, version, System.StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox("Changelog changes will be saved automatically after a short idle.", MessageType.Info);
            }
        }

        private static void LoadCachedChangelogForVersion(string version, bool forceReload)
        {
            if (!forceReload &&
                string.Equals(cachedChangelogVersion, version, System.StringComparison.Ordinal))
            {
                return;
            }

            EasyItchPushChangelog.EnsureVersionSectionExists(version);
            cachedChangelogVersion = version;
            cachedChangelogNotes = EasyItchPushChangelog.GetVersionNotes(version);
        }
    }
}
