using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnakeClub.Editor
{
    public static class SnakeBuilder
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        [InitializeOnLoadMethod]
        private static void SetupWhenReady()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists(ScenePath)) EnsureScene();
            };
        }

        private static void EnsureScene()
        {
            if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
            if (!Directory.Exists("Assets/Resources")) Directory.CreateDirectory("Assets/Resources");
            var fontImporter = AssetImporter.GetAtPath("Assets/Resources/NotoSansSC.ttf") as TrueTypeFontImporter;
            if (fontImporter != null && (fontImporter.fontTextureCase != FontTextureCase.Dynamic || !fontImporter.includeFontData))
            {
                fontImporter.fontTextureCase = FontTextureCase.Dynamic;
                fontImporter.includeFontData = true;
                fontImporter.SaveAndReimport();
            }
            if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/ArenaMaterial.mat") == null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.enableInstancing = true;
                AssetDatabase.CreateAsset(material, "Assets/Resources/ArenaMaterial.mat");
            }
            if (!File.Exists(ScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Snake Club");
                root.AddComponent<SnakeGameApp>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Snake Club";
            PlayerSettings.companyName = "Snake Club";
            PlayerSettings.bundleVersion = "0.0.2";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.runInBackground = true;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 0; settings.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.SaveAssets();
        }

        public static void BuildWebGL() => Build(BuildTarget.WebGL, "unity-webgl");
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "unity-mac/Snake Club.app");

        private static void Build(BuildTarget target, string defaultPath)
        {
            EnsureScene();
            string configured = Environment.GetEnvironmentVariable("SNAKE_BUILD_OUTPUT");
            string output = string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../dist/" + defaultPath))
                : Path.GetFullPath(configured);
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Start Unity with -buildTarget " + target + " before building.");
            var result = BuildPipeline.BuildPlayer(new[] { ScenePath }, output, target, BuildOptions.None);
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Unity build failed: " + result.summary.result);
            if (target == BuildTarget.WebGL && !File.Exists(Path.Combine(output, "index.html")))
                throw new InvalidOperationException("Unity reported success but did not produce a WebGL player.");
            if (target == BuildTarget.StandaloneOSX && !Directory.Exists(output))
                throw new InvalidOperationException("Unity reported success but did not produce a macOS player.");
            string stamp = Environment.GetEnvironmentVariable("SNAKE_BUILD_STAMP");
            if (!string.IsNullOrWhiteSpace(stamp)) File.WriteAllText(stamp, output);
            Debug.Log("Snake Club build complete: " + output);
        }
    }
}
