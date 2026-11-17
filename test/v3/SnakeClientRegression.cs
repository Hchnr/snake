using System;
using System.IO;
using System.Reflection;
using SnakeClub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SnakeClub.Core;

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
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"rules\":\"daily-2\",\"rank\":1,\"total\":1}");
                Require(client.Profile.playerId == "test-player" && client.Seed == 123 && client.AdEnabled, "leaderboard response cleared account state or daily seed");
                Apply("{\"ok\":true,\"runId\":\"run-zero\",\"date\":\"2026-11-16\",\"seed\":0,\"rules\":\"daily-2\"}");
                Require(client.Seed == 0, "a valid zero seed was not accepted from run start");
                Apply("{\"ok\":true,\"date\":\"2026-11-17\",\"seed\":42,\"serverTime\":2000,\"profile\":{\"playerId\":\"test-player\"}}");
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"seed\":99,\"serverTime\":1000,\"runId\":\"late\"}");
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"rank\":1}");
                Require(client.Date == "2026-11-17" && client.Seed == 42 && client.ServerTime == 2000, "stale replies rolled back the server day or seed");
                Debug.Log("PASS Unity JSON replies preserve account, daily seed and ad availability");

                const string path = "Assets/Resources/MysticCave.png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var background = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Require(importer != null && importer.textureShape == TextureImporterShape.Texture2D && importer.npotScale == TextureImporterNPOTScale.None, "home background importer must preserve a 2D image and its aspect ratio");
                Require(background != null && background.width == 1024 && background.height == 1536, "home background texture failed to load at original dimensions");
                Debug.Log("PASS Unity imports the home background as a 1024x1536 2D texture");

                const string gardenPath = "Assets/Resources/HomeGarden.png";
                var gardenImporter = AssetImporter.GetAtPath(gardenPath) as TextureImporter;
                var garden = AssetDatabase.LoadAssetAtPath<Texture2D>(gardenPath);
                Require(gardenImporter != null && gardenImporter.textureShape == TextureImporterShape.Texture2D && gardenImporter.npotScale == TextureImporterNPOTScale.None,
                    "new garden must import as an undistorted 2D texture");
                Require(garden != null && garden.width == 1024 && garden.height == 1536, "new home background did not load");
                Debug.Log("PASS Unity imports the new illustrated home background");

                const string floorPath = "Assets/Resources/ArenaFloor.png";
                var floorImporter = AssetImporter.GetAtPath(floorPath) as TextureImporter;
                var floor = AssetDatabase.LoadAssetAtPath<Texture2D>(floorPath);
                Require(floorImporter != null && floorImporter.npotScale == TextureImporterNPOTScale.ToNearest && floorImporter.wrapMode == TextureWrapMode.Repeat,
                    "arena floor must be repeatable and imported as a power-of-two texture");
                Require(floor != null && floor.width == 1024 && floor.height == 1024, "arena floor did not import at its bounded size");
                Debug.Log("PASS Unity imports the repeatable arena floor texture");

                foreach (string characterPath in new[] { "Assets/Resources/Characters/lanlight.png", "Assets/Resources/Characters/ember.png" })
                {
                    var characterImporter = AssetImporter.GetAtPath(characterPath) as TextureImporter;
                    Require(characterImporter != null && characterImporter.npotScale == TextureImporterNPOTScale.None &&
                        characterImporter.alphaIsTransparency && !characterImporter.mipmapEnabled && characterImporter.maxTextureSize == 1024,
                        "character sprite import must preserve proportions, transparency and bounded size: " + characterPath);
                }
                Debug.Log("PASS Unity preserves character sprite proportions and transparent edges");

                int selected = -1, equipped = -1, started = 0, retried = 0;
                var screen = owner.AddComponent<HomeScreen>();
                var font = Resources.Load<Font>("NotoSansSC");
                Require(font != null, "UI font is missing");
                screen.Setup(font, () => started++, () => { }, () => { }, () => { }, () => { }, () => { },
                    index => selected = index, index => equipped = index, () => { });
                screen.SetupResults(() => retried++, () => { }, () => { }, () => { });
                screen.Visible = true; screen.SetPage(true);
                screen.SetState(new PlayerProgress(), null, "", false, false, true, 0, 4, true, false);
                Button FindButton(string name)
                {
                    foreach (var button in owner.GetComponentsInChildren<Button>(true)) if (button.name == name) return button;
                    throw new Exception("UI button not found: " + name);
                }
                FindButton("Skin card 4").onClick.Invoke();
                FindButton("Equip skin").onClick.Invoke();
                Require(selected == 4 && equipped == 4, "skin selection or local preview action is not wired");
                FindButton("Skin preview toggle").onClick.Invoke();
                Require(screen.ArenaPreview, "arena preview toggle did not activate");
                screen.SetArenaPreview(background);
                RawImage FindRaw(string name)
                {
                    foreach (var image in owner.GetComponentsInChildren<RawImage>(true)) if (image.name == name) return image;
                    throw new Exception("UI image not found: " + name);
                }
                Require(FindRaw("Skin arena preview").enabled && !FindRaw("Skin character").enabled, "arena preview did not replace the portrait");
                screen.SetArenaPreview(null);
                Require(!FindRaw("Skin arena preview").enabled && FindRaw("Skin character").enabled, "missing preview texture did not fall back to portrait");
                Require(selected == 4 && equipped == 4, "arena preview changed the selection or ownership action");
                screen.SetPage(false); Require(!screen.ArenaPreview, "arena preview remained active after leaving the wardrobe");
                FindButton("Start hunt").onClick.Invoke();
                Require(started == 1, "main hunt action is not wired");
                screen.SetPage(false, true); screen.SetResult("挑战结束", "得分 100", "成绩已验证", false, true, 0);
                FindButton("Result retry").onClick.Invoke();
                Require(retried == 1 && FindButton("Result share").gameObject.activeSelf, "result actions are not wired");
                Debug.Log("PASS Unity home, character preview and result actions are wired");
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
