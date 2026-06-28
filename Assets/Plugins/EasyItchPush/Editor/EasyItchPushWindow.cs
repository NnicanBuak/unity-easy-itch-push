using System;
using UnityEditor;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal sealed class EasyItchPushWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        [MenuItem("Tools/Easy Itch Push/Open Window", priority = -10)]
        public static void Open()
        {
            var window = GetWindow<EasyItchPushWindow>("Easy Itch Push");
            window.minSize = new Vector2(420f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            EasyItchPushSettingsGui.EnsureSettingsAreSynchronized(EasyItchPushSettings.Instance);
        }

        private void OnFocus()
        {
            EasyItchPushSettingsGui.EnsureSettingsAreSynchronized(EasyItchPushSettings.Instance);
        }

        private void OnDisable()
        {
            EasyItchPushSettingsGui.FlushPendingSave(EasyItchPushSettings.Instance);
            EasyItchPushSettingsGui.FlushPendingChangelogSave();
        }

        private void OnGUI()
        {
            var settings = EasyItchPushSettings.Instance;
            EasyItchPushSettingsGui.FlushPendingSaveIfIdle(settings);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Easy Itch Push", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Build locally, authenticate with Butler, and push matching versioned platform archives to itch.io.", MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Push Mode", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EasyItchPushSettingsGui.DrawPushModeToolbar(settings);
            if (EditorGUI.EndChangeCheck())
            {
                EasyItchPushSettingsGui.ScheduleSave();
            }

            var modeLabel = settings.GetPushModeLabel(settings.PushMode);
            EditorGUILayout.HelpBox(EasyItchPushSettingsGui.BuildActivePushSummary(settings), MessageType.Info);

            EditorGUILayout.Space(8f);
            if (GUILayout.Button($"Build All + Push ({modeLabel})", GUILayout.Height(52f)))
            {
                RunDelayed(EasyItchPushActions.BuildAllProfilesAndPush);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build All Profiles", GUILayout.Height(32f)))
                {
                    RunDelayed(EasyItchPushActions.BuildAllProfiles);
                }

                if (GUILayout.Button($"Push Existing ({modeLabel})", GUILayout.Height(32f)))
                {
                    RunDelayed(EasyItchPushActions.PushExistingBuilds);
                }
            }

            if (GUILayout.Button("Update Existing Build Changelogs", GUILayout.Height(28f)))
            {
                RunDelayed(EasyItchPushActions.UpdateExistingBuildChangelogs);
            }

            if (GUILayout.Button("Open Build Folder", GUILayout.Height(28f)))
            {
                RunDelayed(EasyItchPushActions.OpenBuildFolder);
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Install / Upgrade Butler"))
                {
                    RunDelayed(EasyItchPushActions.InstallOrUpgradeButler);
                }

                if (GUILayout.Button("Login"))
                {
                    RunDelayed(EasyItchPushActions.Login);
                }

                if (GUILayout.Button($"Status ({modeLabel})"))
                {
                    RunDelayed(EasyItchPushActions.Status);
                }
            }

            EditorGUILayout.Space(12f);
            EasyItchPushSettingsGui.Draw(settings, new EasyItchPushSettingsGui.DrawOptions(showPushModeSelector: false));

            EditorGUILayout.EndScrollView();
        }

        private static void RunDelayed(Action action)
        {
            EasyItchPushSettingsGui.FlushPendingSave(EasyItchPushSettings.Instance);
            EasyItchPushSettingsGui.FlushPendingChangelogSave();
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    EasyItchPushLog.Error(ex.ToString());
                    EditorUtility.DisplayDialog("Easy Itch Push failed", ex.Message, "OK");
                }
            };
        }
    }
}
