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
            PlayerSettings.bundleVersion = "0.0.3.2";
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
            // Reference artwork stays under Editor and is excluded from normal
            // player builds. Explicit opt-in is for internal review builds only.
            bool includeReferences = Environment.GetEnvironmentVariable("SNAKE_INCLUDE_REFERENCE_ASSETS") == "1";
            const string staged = "Assets/Resources/ReferenceSkins";
            if (includeReferences && !Directory.Exists("Assets/Editor/ReferenceSkins"))
                throw new InvalidOperationException("Internal reference images are not present locally; normal builds do not require them.");
            if (Directory.Exists(staged))
                throw new InvalidOperationException("Refusing to build with an existing reference staging directory: " + staged);
            BuildReport result;
            bool stagedCreated = false;
            try
            {
                if (includeReferences)
                {
                    if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Resources", "ReferenceSkins")))
                        throw new InvalidOperationException("Could not create the internal reference staging directory.");
                    stagedCreated = true;
                    foreach (string file in Directory.GetFiles("Assets/Editor/ReferenceSkins", "*.png"))
                    {
                        string source = file.Replace('\\', '/');
                        if (!AssetDatabase.CopyAsset(source, staged + "/" + Path.GetFileName(file)))
                            throw new InvalidOperationException("Could not stage reference image: " + source);
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                result = BuildPipeline.BuildPlayer(new[] { ScenePath }, output, target, BuildOptions.None);
            }
            finally
            {
                if (stagedCreated && AssetDatabase.IsValidFolder(staged)) AssetDatabase.DeleteAsset(staged);
            }
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Unity build failed: " + result.summary.result);
            if (!includeReferences)
                foreach (var archive in result.packedAssets)
                    foreach (var asset in archive.contents)
                        if (!string.IsNullOrEmpty(asset.sourceAssetPath) &&
                            (asset.sourceAssetPath.StartsWith("Assets/Editor/ReferenceSkins/", StringComparison.OrdinalIgnoreCase) ||
                             asset.sourceAssetPath.StartsWith("Assets/Resources/ReferenceSkins/", StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("Reference-only asset entered a normal player build: " + asset.sourceAssetPath);
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
