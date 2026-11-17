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
                var config = new GameConfiguration { experimentalFixedPve = true, serverUrl = "", apiUrl = "", allowDevLogin = false };
                Require(config.FixedPvePreview, "offline preview gate rejected isolated configuration");
                config.apiUrl = "https://example.invalid"; Require(!config.FixedPvePreview, "preview allowed API backend");
                config.apiUrl = ""; config.serverUrl = "wss://example.invalid"; Require(!config.FixedPvePreview, "preview allowed room backend");
                config.serverUrl = ""; config.allowDevLogin = true; Require(!config.FixedPvePreview, "preview allowed developer authentication");
                config.allowDevLogin = false; config.experimentalFixedPve = false; Require(!config.FixedPvePreview, "preview enabled without explicit flag");
                Debug.Log("PASS Unity fixed PvE preview requires explicit offline configuration");
                var local = new GameConfiguration { experimentalFixedPveServer = true, serverUrl = "", apiUrl = "http://127.0.0.1:2568", allowDevLogin = true };
                Require(local.FixedPveServerPreview, "local verifier configuration rejected");
                local.apiUrl = "http://127.0.0.1.example.com:2568"; Require(!local.FixedPveServerPreview, "remote host allowed as loopback");
                local.apiUrl = "http://127.0.0.1:2568/path"; Require(!local.FixedPveServerPreview, "API path allowed in local verifier configuration");
                local.apiUrl = "http://127.0.0.1:2568"; local.allowDevLogin = false; Require(!local.FixedPveServerPreview, "local verifier allowed without development login");
                local.allowDevLogin = true; local.serverUrl = "ws://127.0.0.1:2567/ws"; Require(!local.FixedPveServerPreview, "local verifier allowed room backend");
                Debug.Log("PASS Unity fixed PvE server preview requires explicit loopback configuration");
                TestFixedPvePreviewFlow();
                TestGameplayComparison();
                TestPracticeTutorialAccess();
                TestDepthPracticeIsolation();
                TestBackgroundSimulation();
                TestProtectionWarningVisual();
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
                Apply("{\"ok\":true,\"runId\":\"pve\",\"mode\":\"pve\",\"date\":\"2026-11-16\",\"seed\":789}");
                Require(client.Seed == 0, "PvE seed overwrote daily challenge seed");
                Apply("{\"ok\":true,\"runId\":\"fixed\",\"mode\":\"fixed-eval\",\"date\":\"2026-11-16\",\"seed\":654}");
                Require(client.Seed == 0, "experimental PvE seed overwrote daily challenge seed");
                Apply("{\"ok\":true,\"date\":\"2026-11-17\",\"seed\":42,\"serverTime\":2000,\"profile\":{\"playerId\":\"test-player\"}}");
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"seed\":99,\"serverTime\":1000,\"runId\":\"late\"}");
                Apply("{\"ok\":true,\"date\":\"2026-11-16\",\"rank\":1}");
                Require(client.Date == "2026-11-17" && client.Seed == 42 && client.ServerTime == 2000, "stale replies rolled back the server day or seed");
                Apply("{\"ok\":true,\"profile\":{\"playerId\":\"test-player\",\"revision\":2,\"coins\":100}}");
                Apply("{\"ok\":true,\"profile\":{\"playerId\":\"test-player\",\"revision\":1,\"coins\":35}}");
                Require(client.Profile.coins == 100 && client.Profile.revision == 2, "stale profile response rolled back rewards");
                Debug.Log("PASS Unity JSON replies preserve account, daily seed and ad availability");

                var homeMusic = Resources.Load<AudioClip>("Music/Home");
                var battleMusic = Resources.Load<AudioClip>("Music/Battle");
                Require(homeMusic != null && battleMusic != null && homeMusic != battleMusic, "prebuilt music resources failed to load");
                Require(homeMusic.channels == 1 && battleMusic.channels == 1 && Mathf.Abs(homeMusic.length - 8f) < .01f && Mathf.Abs(battleMusic.length - 8f) < .01f,
                    "music import changed channel count or duration");
                var homeImporter = AssetImporter.GetAtPath("Assets/Resources/Music/Home.wav") as AudioImporter;
                var battleImporter = AssetImporter.GetAtPath("Assets/Resources/Music/Battle.wav") as AudioImporter;
                Require(homeImporter != null && battleImporter != null && homeImporter.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad &&
                    battleImporter.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad, "music import is not configured for loop playback");
                Debug.Log("PASS Unity imports both prebuilt music loops");

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
                screen.SetPage(false, true); screen.SetResult("挑战结束", "得分 100", "撞上对手轨迹，被淘汰", "成绩已验证 · 金币 +35", false, true, 0);
                Text FindText(string name)
                {
                    foreach (var text in owner.GetComponentsInChildren<Text>(true)) if (text.name == name) return text;
                    throw new Exception("UI text not found: " + name);
                }
                var fresh = new PlayerProgress { playerId = "fresh" };
                screen.SetState(fresh, null, "", false, true, true, 0, 0, false, false, friendChallenge: true, tutorialReady: false);
                Require(FindText("Primary label").text.Contains("先完成"), "fresh account did not show the practice requirement");
                screen.SetState(fresh, null, "", false, true, true, 0, 0, false, false, friendChallenge: true, tutorialReady: true);
                Require(FindText("Primary label").text.Contains("接受好友挑战"), "practice completion did not update the challenge entry");
                Require(FindText("Result outcome").text == "撞上对手轨迹，被淘汰" && FindText("Result details").text == "成绩已验证 · 金币 +35",
                    "result outcome and reward status were not presented independently");
                screen.SetResult("围猎结束", "最长 16 · 淘汰 0", "本局已出局", "离线练习 · 不计账号奖励", false, false, 0);
                Require(FindText("Result details").text.Contains("不计账号奖励") && !FindButton("Result share").gameObject.activeSelf,
                    "practice results retained verified rewards or sharing");
                screen.SetResult("挑战结束", "得分 100", "", "正在结算…", false, true, 0);
                FindButton("Result retry").onClick.Invoke();
                Require(retried == 1 && FindButton("Result share").gameObject.activeSelf, "result actions are not wired");
                Debug.Log("PASS Unity home, character preview and result actions are wired");
                string stamp = Environment.GetEnvironmentVariable("SNAKE_UNITY_TEST_STAMP");
                if (!string.IsNullOrEmpty(stamp)) File.WriteAllText(stamp, "passed\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static void TestBackgroundSimulation()
        {
            var root = new GameObject("Background simulation regression");
            try
            {
                var app = root.AddComponent<SnakeGameApp>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(SnakeGameApp);
                FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new Exception("Missing field: " + name);
                void Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(app, args);
                Field("api").SetValue(app, root.AddComponent<GameApiClient>());
                Field("arenaRenderer").SetValue(app, root.AddComponent<ArenaRenderer>());
                Field("connection").SetValue(app, root.AddComponent<RoomConnection>());
                var feedback = root.AddComponent<ArenaFeedback>();
                typeof(ArenaFeedback).GetMethod("Initialize", flags).Invoke(feedback, null);
                Field("feedback").SetValue(app, feedback);
                Call("BeginPve", new object[] { null });
                var arena = (Arena)Field("arena").GetValue(app);
                Field("platformVisible").SetValue(app, false); Field("paused").SetValue(app, false); Field("stepTimer").SetValue(app, .19f);
                Call("Update");
                Require(arena.State.tick == 0 && ((System.Collections.Generic.List<byte>)Field("runInputs").GetValue(app)).Count == 0,
                    "ordinary PvE advanced while platform was hidden");
                Field("platformVisible").SetValue(app, true); Call("BeginDaily", new object[] { null });
                var daily = (DailyMatch)Field("daily").GetValue(app);
                Field("adShowing").SetValue(app, true); Field("stepTimer").SetValue(app, .19f);
                Call("Update");
                Require(daily.State.tick == 0 && ((System.Collections.Generic.List<byte>)Field("runInputs").GetValue(app)).Count == 0,
                    "daily challenge advanced while ad was displayed");
                Debug.Log("PASS Unity ordinary and daily simulations stop when suspended");

                Field("adShowing").SetValue(app, false); Call("BeginPve", new object[] { null });
                arena = (Arena)Field("arena").GetValue(app);
                var own = arena.Find("you"); var opponent = arena.Find("bot-0");
                own.shield = opponent.shield = 0; own.angle = opponent.angle = 0;
                own.segments[0] = opponent.segments[0] = new Point(0, 0);
                Field("stepTimer").SetValue(app, .19f); Call("Update");
                Require(!own.alive && arena.State.phase == "ended" && arena.State.tick == 1 &&
                    ((System.Collections.Generic.List<byte>)Field("runInputs").GetValue(app)).Count == 2,
                    "local round advanced or recorded inputs after elimination during catch-up");
                Require(!(bool)type.GetMethod("CanControlPlayer", flags).Invoke(app, null), "eliminated player remained controllable");
                Call("BeginPve", new object[] { null }); arena = (Arena)Field("arena").GetValue(app);
                arena.Find("you").alive = false; Field("stepTimer").SetValue(app, .19f); Call("Update");
                Require(arena.State.phase == "ended" && arena.State.tick == 0 &&
                    ((System.Collections.Generic.List<byte>)Field("runInputs").GetValue(app)).Count == 0,
                    "an already eliminated local player advanced or recorded an input");
                Debug.Log("PASS Unity local matches stop on the elimination step");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestDepthPracticeIsolation()
        {
            var root = new GameObject("Depth practice isolation regression");
            try
            {
                var app = root.AddComponent<SnakeGameApp>();
                var api = root.AddComponent<GameApiClient>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(SnakeGameApp);
                FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new Exception("Missing field: " + name);
                void Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(app, args);
                var profile = new PlayerProgress { playerId = "depth-account", completedMatches = 3, coins = 123 };
                typeof(GameApiClient).GetProperty("Profile").SetValue(api, profile);
                typeof(GameApiClient).GetField("session", flags).SetValue(api, "test-session");
                typeof(GameApiClient).GetField("reachable", flags).SetValue(api, true);
                Field("api").SetValue(app, api);
                Field("arenaRenderer").SetValue(app, root.AddComponent<ArenaRenderer>());
                Field("connection").SetValue(app, root.AddComponent<RoomConnection>());
                string pending = PlayerPrefs.GetString("snake.v3.pending", "");
                Call("StartDepth");
                Require((bool)Field("depthMode").GetValue(app) && Field("activeRun").GetValue(app) == null &&
                    !(bool)Field("busy").GetValue(app), "connected depth requested an ordinary PvE run");
                Call("SelectDepthBonus", 2);
                Require((int)Field("depthRoom").GetValue(app) == 2 && Field("activeRun").GetValue(app) == null,
                    "next depth room requested an ordinary PvE run");
                var depthArena = (Arena)Field("arena").GetValue(app);
                CombatBonuses Bonuses(Arena arena, string id)
                {
                    var controls = (System.Collections.IDictionary)typeof(Arena).GetField("controls", flags).GetValue(arena);
                    var control = controls[id];
                    return (CombatBonuses)control.GetType().GetField("bonuses", flags | BindingFlags.Public).GetValue(control);
                }
                Require(depthArena.Find("you").shield == Arena.SpawnShieldSeconds && depthArena.Find("bot-0").shield == Arena.SpawnShieldSeconds &&
                    Bonuses(depthArena, "you").boostCostPenalty == .25f && Bonuses(depthArena, "bot-0").boostCostPenalty == 0,
                    "growth choice did not apply the cost only to the player or altered spawn protection");
                Call("SelectDepthBonus", 2);
                depthArena = (Arena)Field("arena").GetValue(app);
                Require((int)Field("depthRoom").GetValue(app) == 3 && depthArena.Find("you").shield == Arena.SpawnShieldSeconds && Bonuses(depthArena, "you").boostCostPenalty == .5f,
                    "stacked growth choice lost its cost or altered spawn protection");
                Call("StartDepth");
                depthArena = (Arena)Field("arena").GetValue(app);
                Require(depthArena.Find("you").shield == Arena.SpawnShieldSeconds && Bonuses(depthArena, "you").boostCostPenalty == 0, "growth tradeoff leaked into a new depth run");
                // Guard against a stale or accidentally supplied ordinary run as well.
                Field("activeRun").SetValue(app, new ApiReply { runId = "stale", token = "stale" });
                Field("activePlayerId").SetValue(app, profile.playerId);
                Field("controlActions").SetValue(app, 3);
                var practice = (PlayerProgress)Field("practiceProfile").GetValue(app);
                int completed = practice.completedMatches;
                Call("RecordResult");
                Require(PlayerPrefs.GetString("snake.v3.pending", "") == pending && profile.coins == 123 &&
                    profile.completedMatches == 3 && practice.completedMatches == completed,
                    "depth result modified progress or queued an ordinary settlement");
                Require(((string)Field("resultNote").GetValue(app)).Contains("不计账号奖励"), "depth reward status was not explained");
                Debug.Log("PASS Unity depth rooms remain isolated practice even with a connected account");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestPracticeTutorialAccess()
        {
            var root = new GameObject("Practice account regression");
            try
            {
                var app = root.AddComponent<SnakeGameApp>();
                var api = root.AddComponent<GameApiClient>();
                var renderer = root.AddComponent<ArenaRenderer>();
                var connection = root.AddComponent<RoomConnection>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(SnakeGameApp);
                FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new Exception("Missing field: " + name);
                void Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(app, args);
                bool Ready() => (bool)type.GetProperty("TutorialReady", flags).GetValue(app);
                var apiType = typeof(GameApiClient);
                var profileProperty = apiType.GetProperty("Profile");
                var accountA = new PlayerProgress { playerId = "account-a", coins = 123 };
                var accountB = new PlayerProgress { playerId = "account-b" };
                profileProperty.SetValue(api, accountA);
                apiType.GetField("session", flags).SetValue(api, "test-session");
                apiType.GetField("reachable", flags).SetValue(api, true);
                Field("api").SetValue(app, api); Field("arenaRenderer").SetValue(app, renderer); Field("connection").SetValue(app, connection);
                string pendingBefore = PlayerPrefs.GetString("snake.v3.pending", "");
                Require(!Ready(), "fresh account skipped practice");
                Call("BeginPve", new object[] { null }); Call("RecordResult");
                Require(!Ready(), "practice without input unlocked the challenge");
                Call("BeginPve", new object[] { null }); Field("controlActions").SetValue(app, 1);
                profileProperty.SetValue(api, accountB);
                Call("RecordResult");
                Require(!Ready(), "practice from a different account unlocked this account");
                profileProperty.SetValue(api, accountA);
                Require(Ready(), "completed practice did not unlock the challenge entry for its account");
                Require(!accountA.tutorialComplete && accountA.completedMatches == 0 && accountA.coins == 123 &&
                    PlayerPrefs.GetString("snake.v3.pending", "") == pendingBefore, "practice changed account rewards or created a settlement");
                apiType.GetField("reachable", flags).SetValue(api, false);
                profileProperty.SetValue(api, accountB);
                Require(Ready(), "offline practice progression was lost");
                apiType.GetField("reachable", flags).SetValue(api, true);
                Require(!Ready(), "offline practice or another account unlocked a fresh account");
                Debug.Log("PASS Unity practice completion unlocks only its account challenge entry");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestProtectionWarningVisual()
        {
            var root = new GameObject("Protection warning regression");
            try
            {
                var renderer = root.AddComponent<ArenaRenderer>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ArenaRenderer).GetMethod("Initialize", flags).Invoke(renderer, null);
                var state = new ArenaState { phase = "running" };
                var me = new SnakeState { id = "you", shield = 2, segments = new System.Collections.Generic.List<Point> { new Point(0, 0), new Point(-1, 0), new Point(-2, 0) } };
                var other = new SnakeState { id = "other", bot = true, shield = 2, segments = new System.Collections.Generic.List<Point> { new Point(3, 0), new Point(4, 0), new Point(5, 0) } };
                state.snakes.Add(me); state.snakes.Add(other);
                var type = typeof(ArenaRenderer);
                var rings = (System.Collections.Generic.Dictionary<string, LineRenderer>)type.GetField("protectionMarkers", flags).GetValue(renderer);
                var arrow = (LineRenderer)type.GetField("dangerArrow", flags).GetValue(renderer);
                renderer.Draw(state, "you");
                Require(rings["you"].enabled && rings["you"].startColor.b > rings["you"].startColor.r && !arrow.enabled, "normal protection presentation was incorrect");
                me.shield = other.shield = .5f; renderer.ReducedEffects = true; renderer.Draw(state, "you");
                Require(rings["you"].enabled && rings["you"].startColor.r > rings["you"].startColor.b && arrow.enabled,
                    "expiring protection or nearby threat was not visible with reduced effects");
                me.shield = other.shield = 0; renderer.Draw(state, "you");
                Require(!rings["you"].enabled && arrow.enabled, "protection did not transition to current danger");
                state.phase = "ended"; renderer.Draw(state, "you"); Require(!arrow.enabled, "finished match kept the danger arrow");
                Debug.Log("PASS Unity protection expiry stays legible with reduced effects");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestGameplayComparison()
        {
            var root = new GameObject("Offline comparison regression");
            try
            {
                var app = root.AddComponent<SnakeGameApp>();
                var api = root.AddComponent<GameApiClient>();
                var config = new GameConfiguration { experimentalComparison = true, apiUrl = "", serverUrl = "", allowDevLogin = false };
                Require(config.ComparisonPreview, "offline comparison was not enabled");
                config.apiUrl = "https://example.invalid"; Require(!config.ComparisonPreview, "comparison allowed a backend");
                config.apiUrl = ""; config.allowDevLogin = true; Require(!config.ComparisonPreview, "comparison allowed development login");
                config.allowDevLogin = false; config.experimentalFixedPve = true; Require(!config.ComparisonPreview, "comparison allowed overlapping modes");
                config.experimentalFixedPve = false; api.Configure(config);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(SnakeGameApp);
                FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new Exception("Missing field: " + name);
                void Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(app, args);
                Field("api").SetValue(app, api); Field("arenaRenderer").SetValue(app, root.AddComponent<ArenaRenderer>());
                Field("connection").SetValue(app, root.AddComponent<RoomConnection>());
                string pending = PlayerPrefs.GetString("snake.v3.pending", "");
                var profile = (PlayerProgress)Field("practiceProfile").GetValue(app);
                Call("StartMain"); Require((string)Field("frontPage").GetValue(app) == "comparison", "main entry did not open comparison");
                for (int variant = 0; variant < 4; variant++)
                {
                    Call("BeginComparison", variant);
                    var daily = (DailyMatch)Field("daily").GetValue(app);
                    bool capture = (bool)typeof(DailyMatch).GetField("captures", flags).GetValue(daily);
                    Require((int)Field("comparisonVariant").GetValue(app) == variant && capture == (variant == 1 || variant == 3) && Field("activeRun").GetValue(app) == null,
                        "comparison variant was not isolated or had incorrect capture rules");
                    foreach (var snake in daily.State.snakes)
                        Require(Mathf.Abs(snake.shield - (variant == 3 ? 4f : 3f)) < .001f, "opening shield was asymmetric or used by another variant");
                    Field("controlActions").SetValue(app, 1); Call("RecordResult");
                    Require(profile.completedMatches == 0 && !profile.tutorialComplete && PlayerPrefs.GetString("snake.v3.pending", "") == pending,
                        "comparison modified account or practice progression");
                    Require(((string)Field("resultNote").GetValue(app)).Contains("不计账号奖励"), "comparison reward state missing");
                    Call("BackToMenu");
                    Require((string)Field("frontPage").GetValue(app) == "comparison", "comparison did not return to selection");
                }
                Debug.Log("PASS Unity offline comparison is isolated and selectable");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestFixedPvePreviewFlow()
        {
            var root = new GameObject("Offline fixed PvE regression");
            try
            {
                var app = root.AddComponent<SnakeGameApp>();
                var api = root.AddComponent<GameApiClient>();
                api.Configure(new GameConfiguration { experimentalFixedPve = true, apiUrl = "", serverUrl = "", allowDevLogin = false });
                var renderer = root.AddComponent<ArenaRenderer>();
                var connection = root.AddComponent<RoomConnection>();
                var type = typeof(SnakeGameApp);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new Exception("Missing field: " + name);
                void Call(string name, params object[] args)
                {
                    var method = type.GetMethod(name, flags) ?? throw new Exception("Missing method: " + name);
                    method.Invoke(app, args);
                }
                Field("api").SetValue(app, api); Field("arenaRenderer").SetValue(app, renderer); Field("connection").SetValue(app, connection);
                string pendingBefore = PlayerPrefs.GetString("snake.v3.pending", "");
                Call("StartMain");
                var world = Field("fixedPve").GetValue(app) as PveFixedWorld;
                Require(world != null && (bool)Field("inGame").GetValue(app) && Field("activeRun").GetValue(app) == null, "main button did not start isolated fixed PvE");
                var state = Field("state").GetValue(app) as ArenaState;
                Require(state != null && state.phase == "running" && state.snakes.Count == 8, "preview did not project playable state");
                var rendererFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                // Editor batch tests do not invoke MonoBehaviour.Awake on AddComponent.
                typeof(ArenaRenderer).GetMethod("Initialize", rendererFlags).Invoke(renderer, null);
                renderer.Draw(state, "you");
                var characters = (System.Collections.Generic.Dictionary<string, SpriteRenderer>)typeof(ArenaRenderer).GetField("characters", rendererFlags).GetValue(renderer);
                Require(characters["you"].sortingOrder > characters["bot-1"].sortingOrder, "controlled character is behind opponent sprites");
                renderer.Draw(state, "bot-1");
                Require(characters["bot-1"].sortingOrder > characters["you"].sortingOrder, "sprite priority did not follow a changed player identity");
                renderer.Draw(state, "you");
                var protections = (System.Collections.Generic.Dictionary<string, LineRenderer>)typeof(ArenaRenderer).GetField("protectionMarkers", rendererFlags).GetValue(renderer);
                var marker = (LineRenderer)typeof(ArenaRenderer).GetField("marker", rendererFlags).GetValue(renderer);
                var head = state.snakes[0].segments[0];
                var marked = marker.GetPosition(0);
                Require(marker.enabled && Mathf.Abs(marked.x - head.x - Arena.CollisionRadius) < .001f && Mathf.Abs(marked.z - head.y) < .001f,
                    "player collision marker did not match the real head and collision radius");
                Require(protections.ContainsKey("you") && protections["you"].enabled && protections.ContainsKey("bot-1") && protections["bot-1"].enabled,
                    "protected player or opponent lacked a protection marker");
                state.snakes[0].shield = 0; state.snakes[1].shield = 0;
                renderer.Draw(state, "you");
                Require(!protections["you"].enabled && !protections["bot-1"].enabled, "expired protection remained visible");
                state.snakes[0].shield = 4; state.snakes[1].shield = 3;
                Field("direction").SetValue(app, new Point(0, 1));
                Field("pointerBoost").SetValue(app, true);
                Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 4 && state.tick == 4 && world.Fighters[0].motion.Boosting, "preview did not advance using input");
                Require((bool)Field("tutorialBoosted").GetValue(app), "actual boost was not observed for the tutorial");
                var inputs = (System.Collections.Generic.List<byte>)Field("runInputs").GetValue(app);
                Require(inputs.Count == 8 && inputs[0] == 64 && inputs[1] == 1 && inputs[6] == 64 && inputs[7] == 1,
                    "preview did not record the actual per-tick controls");
                Field("paused").SetValue(app, true); Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 4 && inputs.Count == 8, "paused preview advanced or recorded input");
                Field("paused").SetValue(app, false);
                var visibilityMethod = type.GetMethod("OnPlatformVisibilityChanged", flags);
                var visibilityHandler = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), app, visibilityMethod);
                api.PlatformVisibilityChanged += visibilityHandler;
                var notify = typeof(GameApiClient).GetMethod("NotifyPlatformVisibility", flags);
                notify.Invoke(api, new object[] { false });
                Require(!(bool)Field("platformVisible").GetValue(app) && (bool)Field("paused").GetValue(app) && !(bool)Field("pointerBoost").GetValue(app),
                    "platform hide did not suspend the offline game and input");
                Field("paused").SetValue(app, false); Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 4 && inputs.Count == 8, "platform-hidden preview advanced or recorded input");
                Field("paused").SetValue(app, true);
                notify.Invoke(api, new object[] { true });
                Require((bool)Field("paused").GetValue(app), "platform show resumed the game without player action");
                Field("paused").SetValue(app, false);
                Field("adShowing").SetValue(app, true); Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 4 && inputs.Count == 8, "preview advanced during an ad");
                Field("adShowing").SetValue(app, false); Field("appPaused").SetValue(app, true); Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 4 && inputs.Count == 8, "application-paused preview advanced");
                Field("appPaused").SetValue(app, false); Field("appFocused").SetValue(app, false); Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 4 && inputs.Count == 8, "unfocused preview advanced");
                Field("appFocused").SetValue(app, true); Call("AdvanceFixedPve", .2f);
                Require(world.Tick == 8 && inputs.Count == 16, "visible preview failed to resume after suspension");
                api.PlatformVisibilityChanged -= visibilityHandler;
                Field("controlActions").SetValue(app, 1);
                var profile = (PlayerProgress)Field("practiceProfile").GetValue(app);
                int previousMatches = profile.completedMatches;
                Call("RecordResult");
                Field("homeScreen").SetValue(app, root.AddComponent<HomeScreen>());
                state.phase = "ended";
                var overlay = type.GetProperty("ResultOverlayVisible", flags) ?? throw new Exception("Missing result overlay policy");
                Require((bool)overlay.GetValue(app), "offline result should suppress the game HUD");
                Field("status").SetValue(app, "error"); Require(!(bool)overlay.GetValue(app), "error overlay was suppressed");
                Field("status").SetValue(app, ""); Field("online").SetValue(app, true); Require(!(bool)overlay.GetValue(app), "online result was suppressed");
                Field("online").SetValue(app, false); Field("depthMode").SetValue(app, true); Require(!(bool)overlay.GetValue(app), "depth result was suppressed");
                Field("depthMode").SetValue(app, false);
                Require(PlayerPrefs.GetString("snake.v3.pending", "") == pendingBefore && profile.completedMatches == previousMatches,
                    "preview modified settlement queue or practice progression");
                Require(((string)Field("resultNote").GetValue(app)).Contains("试玩"), "preview result was not labelled");
                Call("BackToMenu");
                Require(Field("fixedPve").GetValue(app) == null && !(bool)Field("inGame").GetValue(app), "preview did not return to menu");
                api.Configure(new GameConfiguration { experimentalFixedPveServer = true, apiUrl = "http://127.0.0.1:2568", serverUrl = "", allowDevLogin = true });
                typeof(GameApiClient).GetProperty("Profile").SetValue(api, new PlayerProgress { playerId = "local-eval" });
                var snapshot = new PveStartConfig { version = PveStartConfig.CurrentVersion, firstRun = true, skin = 2,
                    talents = new[] { 1, 2, 1, 0, 1, 0, 0, 0, 0 } };
                var run = new ApiReply { ok = true, runId = "run", token = "token", seed = 12345,
                    mode = "fixed-eval", rules = PveFixedRecording.ExperimentalRules, startConfig = snapshot };
                Call("BeginFixedPveWithRun", run);
                var actual = (PveFixedWorld)Field("fixedPve").GetValue(app);
                var expected = new PveFixedWorld(run.seed, snapshot.firstRun, snapshot.MatchProfile());
                Require(actual.Fighters[0].motion.X == expected.Fighters[0].motion.X && actual.Fighters[0].motion.Y == expected.Fighters[0].motion.Y &&
                    actual.Fighters[0].shieldTicks == expected.Fighters[0].shieldTicks && (string)Field("activePlayerId").GetValue(app) == "local-eval" &&
                    renderer.SelectedSkin == 2, "local verifier did not start from the server seed and snapshot");
                Call("BackToMenu");
                Debug.Log("PASS Unity offline fixed PvE start, input, pause and isolated settlement");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
