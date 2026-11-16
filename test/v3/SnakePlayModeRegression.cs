using System;
using System.IO;
using System.Reflection;
using SnakeClub;
using SnakeClub.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

namespace SnakeClub.Tests.Editor
{
    [InitializeOnLoad]
    public static class PlayModeRegression
    {
        private static int phase;
        private static double deadline;
        private static double animationDeadline;
        private static Vector3 headPosition;
        private static Vector3 bodyPoint;
        private static string error;

        static PlayModeRegression()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAKE_PLAY_TEST_STAMP")))
                EditorApplication.playModeStateChanged += Changed;
        }

        public static void Run()
        {
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                PlayerSettings.companyName = "Snake Club Automated Tests";
                PlayerSettings.productName = "Play Regression " + System.Diagnostics.Process.GetCurrentProcess().Id;
                new GameObject("Play regression").AddComponent<SnakeGameApp>();
                Directory.CreateDirectory("Assets/Scenes");
                EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlayRegression.unity");
                EditorApplication.isPlaying = true;
            }
            catch (Exception ex) { Fail(ex.ToString()); }
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            phase = 0; deadline = EditorApplication.timeSinceStartup + 1;
            Application.logMessageReceived += CaptureError;
            EditorApplication.update += Tick;
        }

        private static void CaptureError(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) error = condition + "\n" + stack;
        }

        private static Button ButtonNamed(SnakeGameApp app, string name)
        {
            foreach (var button in app.GetComponentsInChildren<Button>(true)) if (button.name == name) return button;
            throw new InvalidOperationException("Button missing: " + name);
        }

        private static void Click(Button button)
        {
            if (!button.isActiveAndEnabled || !button.interactable) throw new InvalidOperationException("Button is inactive: " + button.name);
            Canvas.ForceUpdateCanvases();
            var system = EventSystem.current;
            if (system == null) throw new InvalidOperationException("Active event system is missing");
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(system) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            system.RaycastAll(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button)
                throw new InvalidOperationException("Button is not reachable by pointer: " + button.name + (hits.Count > 0 ? "; hit " + hits[0].gameObject.name : "; no hit"));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static void CaptureTexture(RenderTexture source, string name)
        {
            string directory = Environment.GetEnvironmentVariable("SNAKE_ARTIFACT_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            var old = RenderTexture.active; RenderTexture.active = source;
            var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name), image.EncodeToPNG());
            RenderTexture.active = old; UnityEngine.Object.Destroy(image);
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < deadline) return;
            try
            {
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                var app = UnityEngine.Object.FindFirstObjectByType<SnakeGameApp>();
                if (app == null) throw new InvalidOperationException("Game app did not start");
                var avatar = app.GetComponent<HomeAvatar>();
                var screen = app.GetComponent<HomeScreen>();
                if (avatar == null || screen == null || avatar.Texture == null) throw new InvalidOperationException("Home did not initialize");
                var fullCharacter = avatar.transform.Find("Full character reference")?.GetComponent<SpriteRenderer>();
                if (fullCharacter == null || !fullCharacter.enabled || fullCharacter.sprite == null)
                    throw new InvalidOperationException("Full reference character missing from home preview");
                if (phase == 0)
                {
                    headPosition = fullCharacter.transform.position;
                    animationDeadline = EditorApplication.timeSinceStartup + 2.5;
                    int availableCount = SkinVisual.ReferenceAvailable ? SkinCatalog.All.Length : SkinCatalog.OwnedSkinCount;
                    for (int i = 0; i < availableCount; i++)
                        if (SkinVisual.Sprite(i) == null) throw new InvalidOperationException("Character sprite missing: " + SkinCatalog.Get(i).Id);
                    phase++; deadline = EditorApplication.timeSinceStartup + .5; return;
                }
                if (phase == 1)
                {
                    if (Vector3.Distance(headPosition, fullCharacter.transform.position) < .001f)
                    {
                        // A periodic idle can return to the same point at two
                        // sample times even though it is moving in between.
                        if (EditorApplication.timeSinceStartup < animationDeadline)
                        {
                            deadline = EditorApplication.timeSinceStartup + .2;
                            return;
                        }
                        throw new InvalidOperationException("Full character did not animate");
                    }
                    if (app.GetComponent<ArenaRenderer>().Visible) throw new InvalidOperationException("Arena camera should be idle behind the home screen");
                    Click(ButtonNamed(app, "Settings")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 2)
                {
                    Click(ButtonNamed(app, "Settings controls")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 3)
                {
                    Click(ButtonNamed(app, "Settings effects")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 4)
                {
                    if (PlayerPrefs.GetInt("snake.v031.mirrorControls", 0) != 1 || PlayerPrefs.GetInt("snake.v031.reducedEffects", 0) != 1)
                        throw new InvalidOperationException("Control or effects preference was not saved");
                    var apply = typeof(SnakeGameApp).GetMethod("ApplyPointer", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (apply == null || !(bool)apply.Invoke(app, new object[] { new Vector2(965, 1645) }))
                        throw new InvalidOperationException("Right-side joystick did not accept input");
                    var direction = (Point)typeof(SnakeGameApp).GetField("direction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                    if (direction.x <= 0) throw new InvalidOperationException("Right-side joystick reversed the input direction");
                    Click(ButtonNamed(app, "Settings close")); Click(ButtonNamed(app, "装扮")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 5)
                {
                    if (!SkinVisual.ReferenceAvailable)
                    {
                        CaptureTexture((RenderTexture)avatar.Texture, "character.png");
                        Click(ButtonNamed(app, "Skin back")); phase = 10; deadline = EditorApplication.timeSinceStartup + .2; return;
                    }
                    Click(ButtonNamed(app, "Next skins")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 6)
                {
                    Click(ButtonNamed(app, "Skin card 6")); Click(ButtonNamed(app, "Previous skins")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 7)
                {
                    Click(ButtonNamed(app, "Skin card 5")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 8)
                {
                    Click(ButtonNamed(app, "Equip skin")); phase++; deadline = EditorApplication.timeSinceStartup + .3; return;
                }
                if (phase == 9)
                {
                    if (avatar.Skin != 5 || !fullCharacter.enabled || fullCharacter.sprite == null || !fullCharacter.sprite.name.Contains("reference-raddy"))
                        throw new InvalidOperationException("Reference character preview did not equip");
                    CaptureTexture((RenderTexture)avatar.Texture, "character.png");
                    Click(ButtonNamed(app, "Skin back")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 10)
                {
                    Click(ButtonNamed(app, "Start hunt")); phase++; deadline = EditorApplication.timeSinceStartup + .5; return;
                }
                var field = typeof(SnakeGameApp).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
                var world = field?.GetValue(app) as ArenaState;
                if (phase == 11)
                {
                    if (world == null || world.tick < 1 || world.snakes.Count != 8)
                        throw new InvalidOperationException("Hunt did not start with its AI opponents");
                    if (!app.GetComponent<ArenaRenderer>().Visible) throw new InvalidOperationException("Arena camera did not resume for the hunt");
                    var fighter = app.transform.Find("Arena Runtime/Full character you")?.GetComponent<SpriteRenderer>();
                    string expected = SkinVisual.ReferenceAvailable ? "reference-raddy" : "lanlight";
                    if (fighter == null || !fighter.enabled || fighter.sprite == null || !fighter.sprite.name.Contains(expected))
                        throw new InvalidOperationException("Full character did not appear in the arena");
                    var trail = app.transform.Find("Arena Runtime/Character trail you 0")?.GetComponent<LineRenderer>();
                    var own = world.snakes.Find(s => s.id == "you");
                    if (trail == null || !trail.enabled || trail.positionCount != own.segments.Count - 2 ||
                        Mathf.Abs(trail.GetPosition(0).x - own.segments[2].x) > .01f || Mathf.Abs(trail.GetPosition(0).z - own.segments[2].y) > .01f)
                        throw new InvalidOperationException("Visible trail does not follow collidable segments");
                    var marker = app.transform.Find("Arena Runtime/Player Marker")?.GetComponent<LineRenderer>();
                    if (marker == null || !marker.enabled || Mathf.Abs(Vector3.Distance(marker.GetPosition(0), marker.GetPosition(24)) - 2 * Arena.CollisionRadius) > .02f)
                        throw new InvalidOperationException("Player marker does not show the head collision radius");
                    var otherMarker = app.transform.Find("Arena Runtime/Opponent collision " + world.snakes[1].id)?.GetComponent<LineRenderer>();
                    if (otherMarker == null || !otherMarker.enabled || Mathf.Abs(Vector3.Distance(otherMarker.GetPosition(0), otherMarker.GetPosition(16)) - 2 * Arena.CollisionRadius) > .02f)
                        throw new InvalidOperationException("Opponent marker does not show the head collision radius");
                    var renderer = app.GetComponent<ArenaRenderer>();
                    renderer.ReducedEffects = false;
                    world.captureTick = world.tick; world.capturePolygon = new List<Point> { new Point(1, 2), new Point(3, 2), new Point(2, 4) };
                    own.rebound = .8f; renderer.Draw(world, "you");
                    var flare = app.transform.Find("Arena Runtime/Capture closing point")?.GetComponent<LineRenderer>();
                    var ripple = app.transform.Find("Arena Runtime/Rebound ripple")?.GetComponent<LineRenderer>();
                    if (flare == null || !flare.enabled || ripple == null || !ripple.enabled)
                        throw new InvalidOperationException("Capture or rebound feedback did not follow the game event");
                    renderer.ReducedEffects = true; renderer.Draw(world, "you");
                    if (flare.enabled || ripple.enabled) throw new InvalidOperationException("Reduced effects did not suppress decorative rings");
                    own.rebound = 0; world.captureTick = -1; world.capturePolygon.Clear();
                    var boss = world.snakes[1]; boss.boss = true; boss.bossShield = 3; boss.bossWindup = 1; boss.bossAim = new Point(1, 0);
                    app.GetComponent<ArenaRenderer>().Draw(world, "you");
                    var warning = app.transform.Find("Arena Runtime/Boss attack warning")?.GetComponent<LineRenderer>();
                    if (warning == null || !warning.enabled) throw new InvalidOperationException("Boss windup is missing its direction warning");
                    boss.boss = false; boss.bossWindup = 0;
                    string artifacts = Environment.GetEnvironmentVariable("SNAKE_ARTIFACT_DIR");
                    if (!string.IsNullOrEmpty(artifacts))
                    {
                        var camera = app.transform.Find("Arena Runtime/Arena Camera").GetComponent<Camera>();
                        var target = new RenderTexture(720, 1280, 24);
                        var oldTarget = camera.targetTexture; camera.targetTexture = target;
                        app.GetComponent<ArenaRenderer>().Draw(world, "you"); camera.Render();
                        CaptureTexture(target, "arena.png"); camera.targetTexture = oldTarget; target.Release(); UnityEngine.Object.Destroy(target);
                    }
                    var floor = app.transform.Find("Arena Runtime/Arena Floor")?.GetComponent<Renderer>();
                    if (floor == null || floor.sharedMaterial.mainTexture != Resources.Load<Texture2D>("ArenaStage"))
                        throw new InvalidOperationException("The illustrated arena floor is not in use");
                    world.phase = "ended"; phase++; deadline = EditorApplication.timeSinceStartup + .4; return;
                }
                if (phase == 12)
                {
                    Click(ButtonNamed(app, "Result retry")); phase++; deadline = EditorApplication.timeSinceStartup + .5; return;
                }
                if (phase == 13)
                {
                    if (world == null || world.phase != "running" || world.tick < 1) throw new InvalidOperationException("Retry did not start another hunt");
                    world.phase = "ended"; phase++; deadline = EditorApplication.timeSinceStartup + .4; return;
                }
                if (phase == 14)
                {
                    Click(ButtonNamed(app, "Result menu")); phase++; deadline = EditorApplication.timeSinceStartup + .3; return;
                }
                if (!screen.Visible || !ButtonNamed(app, "Start hunt").isActiveAndEnabled)
                    throw new InvalidOperationException("Result menu did not return to the home screen");
                File.WriteAllText(Environment.GetEnvironmentVariable("SNAKE_PLAY_TEST_STAMP"), "passed\n");
                Debug.Log("PASS Unity Play Mode animates characters, saves settings and raycasts home, skin, hunt and result actions");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Fail(ex.ToString()); }
        }

        private static void Fail(string message)
        {
            Debug.LogError("FAIL Unity Play Mode: " + message);
            EditorApplication.Exit(1);
        }
    }
}
