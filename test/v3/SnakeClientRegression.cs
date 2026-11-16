using System;
using System.IO;
using System.Reflection;
using SnakeClub;
using UnityEditor;
using UnityEngine;

namespace SnakeClub.Tests.Editor
{
    public static class ClientRegression
    {
        public static void Run()
        {
            var owner = new GameObject("Client regression test");
            try
            {
                var client = owner.AddComponent<GameApiClient>();
                var apply = typeof(GameApiClient).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
                if (apply == null) throw new Exception("Client response handler not found.");
                void Apply(string json) => apply.Invoke(client, new object[] { JsonUtility.FromJson<ApiReply>(json) });
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"seed\":123,\"adEnabled\":true,\"profile\":{\"playerId\":\"test-player\",\"coins\":35}}");
                Require(client.Profile != null && client.Profile.playerId == "test-player" && client.Profile.coins == 35 && client.AdEnabled, "bootstrap did not load the account");
                Apply("{\"ok\":true,\"runId\":\"run-1\",\"date\":\"2026-11-16\",\"seed\":123,\"rules\":\"daily-1\"}");
                Require(client.Profile != null && client.Profile.playerId == "test-player" && client.Profile.coins == 35 && client.AdEnabled, "starting a match cleared the account or ad state");
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"rank\":1,\"total\":1}");
                Require(client.Profile.playerId == "test-player" && client.Seed == 123 && client.AdEnabled, "leaderboard response cleared account state or daily seed");
                Debug.Log("PASS Unity JSON replies preserve account, daily seed and ad availability");

                const string path = "Assets/Resources/MysticCave.png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var background = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Require(importer != null && importer.textureShape == TextureImporterShape.Texture2D && importer.npotScale == TextureImporterNPOTScale.None, "home background importer must preserve a 2D image and its aspect ratio");
                Require(background != null && background.width == 1024 && background.height == 1536, "home background texture failed to load at original dimensions");
                Debug.Log("PASS Unity imports the home background as a 1024x1536 2D texture");
                string stamp = Environment.GetEnvironmentVariable("SNAKE_UNITY_TEST_STAMP");
                if (!string.IsNullOrEmpty(stamp)) File.WriteAllText(stamp, "passed\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
