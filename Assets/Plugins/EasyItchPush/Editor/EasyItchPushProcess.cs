using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;

namespace EasyItchPush.Editor
{
    internal sealed class EasyItchPushProcessResult
    {
        public int ExitCode;
        public string Output = string.Empty;
        public string Error = string.Empty;
        public bool Succeeded => ExitCode == 0;
    }

    internal static class EasyItchPushProcess
    {
        public static EasyItchPushProcessResult Run(string fileName, string arguments, string workingDirectory = "", string progressTitle = "")
        {
            var output = new StringBuilder();
            var error = new StringBuilder();

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            EasyItchPushLog.Info($"> {fileName} {arguments}");

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (string.IsNullOrEmpty(args.Data))
                    {
                        return;
                    }

                    output.AppendLine(args.Data);
                    EasyItchPushLog.Info(args.Data);
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (string.IsNullOrEmpty(args.Data))
                    {
                        return;
                    }

                    error.AppendLine(args.Data);
                    EasyItchPushLog.Warning(args.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.HasExited)
                {
                    if (!string.IsNullOrEmpty(progressTitle))
                    {
                        EditorUtility.DisplayProgressBar(progressTitle, "Waiting for external process...", 0.5f);
                    }

                    process.WaitForExit(100);
                }

                process.WaitForExit();

                if (!string.IsNullOrEmpty(progressTitle))
                {
                    EditorUtility.ClearProgressBar();
                }

                return new EasyItchPushProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = output.ToString(),
                    Error = error.ToString()
                };
            }
        }

        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
