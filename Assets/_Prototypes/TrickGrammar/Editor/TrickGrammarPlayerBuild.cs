using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prototypes.TrickGrammar.EditorTools
{
    /// <summary>
    /// PROTOTYPE. Builds the trick-grammar scene as a standalone macOS player, so the
    /// prototype can be launched as a plain app without opening the editor and pressing Play.
    ///
    /// Batchmode: -executeMethod Prototypes.TrickGrammar.EditorTools.TrickGrammarPlayerBuild.Build
    ///            -protoBuildPath /absolute/path/TrickGrammar.app
    /// </summary>
    public static class TrickGrammarPlayerBuild
    {
        private const string ScenePath = "Assets/_Prototypes/TrickGrammar/TrickGrammar.unity";

        [MenuItem("Prototypes/Build Trick Grammar Player (macOS)")]
        public static void Build()
        {
            string outputPath = ReadArg("-protoBuildPath") ?? "Builds/TrickGrammar.app";

            // Windowed, so the prototype doesn't take over the screen and is easy to quit.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Prototype] Player build {report.summary.result} -> {outputPath} " +
                      $"({report.summary.totalErrors} errors)");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }

        private static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
