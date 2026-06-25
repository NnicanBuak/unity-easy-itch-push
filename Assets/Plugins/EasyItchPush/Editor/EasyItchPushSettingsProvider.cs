using System.Collections.Generic;
using UnityEditor;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Easy Itch Push", SettingsScope.Project)
            {
                label = "Easy Itch Push",
                keywords = new HashSet<string>
                {
                    "itch",
                    "itch.io",
                    "butler",
                    "build",
                    "deploy",
                    "push"
                },
                activateHandler = (_, __) => EasyItchPushSettingsGui.EnsureSettingsAreSynchronized(EasyItchPushSettings.Instance),
                deactivateHandler = () =>
                {
                    EasyItchPushSettingsGui.FlushPendingSave(EasyItchPushSettings.Instance);
                    EasyItchPushSettingsGui.FlushPendingChangelogSave();
                },
                guiHandler = _ =>
                {
                    var settings = EasyItchPushSettings.Instance;
                    EasyItchPushSettingsGui.FlushPendingSaveIfIdle(settings);
                    EasyItchPushSettingsGui.Draw(settings, new EasyItchPushSettingsGui.DrawOptions(showPushModeSelector: true));
                }
            };
        }
    }
}
