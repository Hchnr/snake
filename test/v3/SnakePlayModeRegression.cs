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
                var head = avatar.transform.Find("Portrait Snake/Expressive head");
                var body = avatar.transform.Find("Portrait Snake/Smooth body")?.GetComponent<MeshFilter>()?.sharedMesh;
                if (head == null || body == null || body.vertexCount == 0) throw new InvalidOperationException("Character geometry missing");
                if (phase == 0)
                {
                    headPosition = head.localPosition; bodyPoint = body.vertices[40];
                    phase++; deadline = EditorApplication.timeSinceStartup + .5; return;
                }
                if (phase == 1)
                {
                    if (Vector3.Distance(headPosition, head.localPosition) < .001f || Vector3.Distance(bodyPoint, body.vertices[40]) < .001f)
                        throw new InvalidOperationException("Character head or body did not animate");
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
                    Click(ButtonNamed(app, "Skin card 4")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 6)
                {
                    Click(ButtonNamed(app, "Equip skin")); phase++; deadline = EditorApplication.timeSinceStartup + .3; return;
                }
                if (phase == 7)
                {
                    if (avatar.Skin != 4 || avatar.transform.Find("Portrait Snake/Expressive head/Character details/Cat ear") == null)
                        throw new InvalidOperationException("Reference character preview did not equip");
                    Click(ButtonNamed(app, "Skin back")); phase++; deadline = EditorApplication.timeSinceStartup + .2; return;
                }
                if (phase == 8)
                {
                    Click(ButtonNamed(app, "Start hunt")); phase++; deadline = EditorApplication.timeSinceStartup + .5; return;
                }
                var field = typeof(SnakeGameApp).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
                var world = field?.GetValue(app) as ArenaState;
                if (phase == 9)
                {
                    if (world == null || world.tick < 1 || world.snakes.Count != 8)
                        throw new InvalidOperationException("Hunt did not start with its AI opponents");
                    if (!app.GetComponent<ArenaRenderer>().Visible) throw new InvalidOperationException("Arena camera did not resume for the hunt");
                    var floor = app.transform.Find("Arena Runtime/Arena Floor")?.GetComponent<Renderer>();
                    if (floor == null || floor.sharedMaterial.mainTexture != Resources.Load<Texture2D>("ArenaFloor"))
                        throw new InvalidOperationException("The illustrated arena floor is not in use");
                    world.phase = "ended"; phase++; deadline = EditorApplication.timeSinceStartup + .4; return;
                }
                if (phase == 10)
                {
                    Click(ButtonNamed(app, "Result retry")); phase++; deadline = EditorApplication.timeSinceStartup + .5; return;
                }
                if (phase == 11)
                {
                    if (world == null || world.phase != "running" || world.tick < 1) throw new InvalidOperationException("Retry did not start another hunt");
                    world.phase = "ended"; phase++; deadline = EditorApplication.timeSinceStartup + .4; return;
                }
                if (phase == 12)
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
