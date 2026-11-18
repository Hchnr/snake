using System;
using System.Collections;
using System.IO;
using System.Reflection;
using SnakeClub.Core;
using UnityEditor;
using UnityEngine;
using WeChatWASM;

namespace SnakeClub.Tests.Editor
{
    public static class WeChatLifecycleRegression
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        public static void Run()
        {
            try
            {
                WX.Reset();
                var root = new GameObject("WeChat lifecycle fixture");
                var client = root.AddComponent<GameApiClient>();
                Call(client, "Awake"); Call(client, "Awake");
                Require(WX.InitCalls == 1 && !client.PlatformGameReady && client.PlatformGameStatus.Contains("初始化中"), "initialization was not gated or started more than once");
                var config = new GameConfiguration { allowDevLogin = true, experimentalFixedPveServer = true, apiUrl = "http://127.0.0.1:2568", serverUrl = "" };
                client.Configure(config);
                Require(!config.FixedPveServerPreview, "WeChat allowed local verifier configuration");
                config.experimentalFixedPveServer = false; config.experimentalStrengthPveServer = true;
                Require(!config.StrengthPveServerPreview, "WeChat allowed strength verifier configuration");
                ApiReply reply = null; client.Login(value => reply = value);
                Require(reply != null && !reply.ok && reply.error.Contains("微信账号"), "WeChat fell back to development login");
                var app = root.AddComponent<SnakeGameApp>(); Set(app, "api", client);
                Call(app, "StartMain");
                Require(!(bool)Get(app, "inGame") && ((string)Get(app, "frontNotice")).Contains("初始化中"), "game started before SDK initialization");
                WX.Complete(200); WX.Complete(200);
                Require(client.PlatformGameReady && WX.ShowCount == 1 && WX.HideCount == 1, "success did not bind lifecycle exactly once");
                var handler = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), app, typeof(SnakeGameApp).GetMethod("OnPlatformVisibilityChanged", Flags));
                client.PlatformVisibilityChanged += handler;
                Set(app, "inGame", true); Set(app, "online", false); Set(app, "paused", false); Set(app, "pointerBoost", true);
                var drag = (DragSteering)Get(app, "dragSteering"); drag.Begin(5, new Point(10, 10));
                WX.Hide();
                Require(!(bool)Get(app, "platformVisible") && (bool)Get(app, "paused") && !(bool)Get(app, "pointerBoost") && !drag.Active,
                    "hide did not suspend and clear the active input");
                WX.Show();
                Require((bool)Get(app, "platformVisible") && (bool)Get(app, "paused"), "show unexpectedly resumed the match");
                Call(client, "OnDestroy");
                Require(!client.PlatformGameReady && WX.ShowCount == 0 && WX.HideCount == 0, "lifecycle listeners were not removed");
                WX.Complete(200); Require(!client.PlatformGameReady, "late initialization revived a disposed client");
                UnityEngine.Object.DestroyImmediate(root);

                WX.Reset(); root = new GameObject("WeChat failure fixture"); client = root.AddComponent<GameApiClient>(); Call(client, "Awake");
                WX.Complete(0); WX.Complete(200);
                Require(!client.PlatformGameReady && client.PlatformGameStatus.Contains("失败") && WX.ShowCount == 0, "failure or duplicate init was accepted");
                UnityEngine.Object.DestroyImmediate(root);

                WX.Reset(); root = new GameObject("WeChat timeout fixture"); client = root.AddComponent<GameApiClient>(); Call(client, "Awake");
                var timeout = (IEnumerator)typeof(GameApiClient).GetMethod("WeChatInitTimeout", Flags).Invoke(client, null);
                Require(timeout.MoveNext() && !timeout.MoveNext(), "timeout fixture did not complete");
                WX.Complete(200); Require(!client.PlatformGameReady && WX.ShowCount == 0, "late callback bypassed timeout");
                UnityEngine.Object.DestroyImmediate(root);

                WX.Reset(); root = new GameObject("WeChat partial registration fixture"); client = root.AddComponent<GameApiClient>(); Call(client, "Awake");
                WX.ThrowOnHide = true; WX.Complete(200);
                Require(!client.PlatformGameReady && WX.ShowCount == 0 && WX.HideCount == 0, "partial registration was not rolled back");
                UnityEngine.Object.DestroyImmediate(root);
                File.WriteAllText(Environment.GetEnvironmentVariable("SNAKE_WECHAT_TEST_STAMP"), "passed\n");
                Debug.Log("PASS Unity simulated WeChat initialization, visibility and cleanup");
            }
            catch (Exception error) { Debug.LogError("WECHAT_LIFECYCLE_TEST_FAILED " + error); EditorApplication.Exit(1); }
        }
    }
}
