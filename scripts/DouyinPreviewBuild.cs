// Copied into an isolated Unity project by build-douyin-preview.py.
// This file lives outside Assets so the repository does not depend on TTSDK.
#if !SNAKE_DOUYIN_TTSDK
#error Douyin preview must compile with SNAKE_DOUYIN_TTSDK enabled.
#endif
using System;
using System.IO;
using System.Threading.Tasks;
using TTSDK.Tool;
using UnityEditor;
using UnityEngine;

public static class DouyinPreviewBuild
{
    private static Task<string> build;

    public static void Run()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("SNAKE_DOUYIN_REFERENCE_PREVIEW") == "1")
            {
                const string source = "Assets/Editor/ReferenceSkins";
                const string staged = "Assets/Resources/ReferenceSkins";
                if (!File.Exists(source + "/wenda.png") || Directory.Exists(staged))
                    throw new InvalidOperationException("Reference preview assets are missing or already staged");
                if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Resources", "ReferenceSkins")))
                    throw new InvalidOperationException("Could not create reference staging directory");
                foreach (string file in Directory.GetFiles(source, "*.png"))
                    if (!AssetDatabase.CopyAsset(file.Replace('\\', '/'), staged + "/" + Path.GetFileName(file)))
                        throw new InvalidOperationException("Could not stage " + Path.GetFileName(file));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                SnakeClub.Editor.SnakeBuilder.StageOptionalReferenceLayers(staged);
            }
            var settings = StarkBuilderSettings.Instance;
            settings.appId = Environment.GetEnvironmentVariable("SNAKE_DOUYIN_PREVIEW_APPID");
            settings.OutputDir = Environment.GetEnvironmentVariable("SNAKE_DOUYIN_PREVIEW_OUTPUT");
            settings.orientation = (StarkBuilderSettings.Orientation)0; // Portrait.
            settings.isOldBuildFormat = false;
            settings.needCompress = true;
            settings.profiling = false;
            settings.Save();
            AssetDatabase.SaveAssets();
            Debug.Log("DOUYIN_PREVIEW_BUILD_START");
            build = TTSDK.Tool.API.BuildManager.Build(Framework.Wasm, false);
            if (build == null) throw new InvalidOperationException("TTSDK did not start the build");
            EditorApplication.update += Poll;
        }
        catch (Exception ex) { Debug.LogError("DOUYIN_PREVIEW_BUILD_FAILED " + ex); EditorApplication.Exit(1); }
    }

    private static void Poll()
    {
        if (!build.IsCompleted) return;
        EditorApplication.update -= Poll;
        try
        {
            string result = build.GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(result)) throw new InvalidOperationException("TTSDK returned no artifact path");
            File.WriteAllText(Environment.GetEnvironmentVariable("SNAKE_DOUYIN_PREVIEW_STAMP"), result);
            Debug.Log("DOUYIN_PREVIEW_BUILD_COMPLETE " + result);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogError("DOUYIN_PREVIEW_BUILD_FAILED " + ex); EditorApplication.Exit(1); }
    }
}
