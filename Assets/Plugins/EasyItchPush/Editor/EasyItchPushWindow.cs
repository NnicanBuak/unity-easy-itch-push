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
            window.minSize = new Vector2(620f, 460f);
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
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Easy Itch Push", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Publish releases, edit notes and review your publishing history.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(10);
            EasyItchPushSettingsGui.Draw(settings,
                new EasyItchPushSettingsGui.DrawOptions(showPushModeSelector: true, showActions: true));
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
