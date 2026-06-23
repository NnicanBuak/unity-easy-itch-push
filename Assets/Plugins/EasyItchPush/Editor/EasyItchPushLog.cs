using System;
using System.IO;
using UnityEngine;

namespace EasyItchPush.Editor
{
    internal static class EasyItchPushLog
    {
        private const string Prefix = "[Easy Itch Push]";
        private const string LogDirectory = "logs/EasyItchPush";
        private static readonly object FileLock = new object();
        private static bool fileLoggingFailed;

        public static void Info(string message)
        {
            WriteFile("INFO", message);
            Debug.Log($"{Prefix} {message}");
        }

        public static void Warning(string message)
        {
            WriteFile("WARN", message);
            Debug.LogWarning($"{Prefix} {message}");
        }

        public static void Error(string message)
        {
            WriteFile("ERROR", message);
            Debug.LogError($"{Prefix} {message}");
        }

        public static string CurrentLogPath
        {
            get
            {
                return Path.GetFullPath(Path.Combine(LogDirectory, $"EasyItchPush-{DateTime.Now:yyyy-MM-dd}.log"));
            }
        }

        private static void WriteFile(string level, string message)
        {
            if (fileLoggingFailed)
            {
                return;
            }

            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(CurrentLogPath, FormatLine(level, message) + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                fileLoggingFailed = true;
                Debug.LogWarning($"{Prefix} File logging disabled: {ex.Message}");
            }
        }

        private static string FormatLine(string level, string message)
        {
            return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        }
    }
}
