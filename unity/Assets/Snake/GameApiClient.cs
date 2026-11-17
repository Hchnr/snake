using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SnakeClub.Core;
using UnityEngine;
using UnityEngine.Networking;
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
#endif

namespace SnakeClub
{
    [Serializable] public sealed class GameConfiguration
    {
        public string serverUrl = "ws://127.0.0.1:2567/ws";
        public string apiUrl = "http://127.0.0.1:2567";
        public string rewardedAdUnitId = "";
        public bool allowDevLogin;
        public bool allowReferenceSkinPreview;
    }

    [Serializable] public sealed class LeaderboardRow
    {
        public int rank, score;
        public string playerId = "", name = "";
    }

    [Serializable] public sealed class ApiReply
    {
        public bool ok, verified, adEnabled;
        public string error = "", session = "", runId = "", token = "", date = "", rules = "", id = "", shareToken = "";
        public int seed, score, rank, total, gap, coins, baseCoins, experience, fragments, questCoins, questExperience;
        public int trialSkin = -1, unlockedSkin = -1;
        public long serverTime, expiresAt;
        public PlayerProgress profile;
        public List<LeaderboardRow> rows;
    }

    [Serializable] public sealed class ApiRequest
    {
        public string code, anonymousCode, deviceId, runId, token, inputs, id, sourceId, kind;
        public int index, seconds, score, actions;
    }

    public sealed class GameApiClient : MonoBehaviour
    {
        public GameConfiguration Config { get; private set; } = new GameConfiguration();
        public PlayerProgress Profile { get; private set; }
        public bool Connected => Profile != null && !string.IsNullOrEmpty(session) && reachable;
        public bool AdEnabled { get; private set; }
        public string Date { get; private set; } = "";
        public int Seed { get; private set; }
        public long ServerTime { get; private set; }
        public float ServerTimeReceivedAt { get; private set; }
        public string LaunchChallengeToken { get; private set; } = "";
        private string session = "";
        private bool reachable;
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
        private bool platformReady;
#endif

        public void Configure(GameConfiguration config) { if (config != null) Config = config; }

        public void Login(Action<ApiReply> done)
        {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
            if (platformReady) { LoginDouyin(done); return; }
            bool resolved = false;
            try
            {
                TT.InitSDK((code, env) =>
                {
                    if (resolved) return;
                    resolved = true;
                    if (code != 0) { done(new ApiReply { error = "抖音环境初始化失败" }); return; }
                    platformReady = true;
                    try
                    {
                        var launch = TT.GetLaunchOptionsSync();
                        if ((object)launch != null && launch.Query != null && launch.Query.TryGetValue("challenge", out var token)) LaunchChallengeToken = token ?? "";
                    }
                    catch (Exception) { /* Login can proceed without a share query. */ }
                    LoginDouyin(done);
                });
                if (!resolved) StartCoroutine(InitTimeout(() => resolved, () =>
                {
                    resolved = true; done(new ApiReply { error = "抖音环境初始化超时" });
                }));
            }
            catch (Exception)
            {
                if (!resolved) { resolved = true; done(new ApiReply { error = "抖音 SDK 初始化失败" }); }
            }
#else
            if (!Config.allowDevLogin) { done(new ApiReply { error = "当前为离线练习" }); return; }
            string device = PlayerPrefs.GetString("snake.v3.dev.device", "");
            if (string.IsNullOrEmpty(device)) { device = Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("snake.v3.dev.device", device); PlayerPrefs.Save(); }
            PostRaw("auth/dev", new ApiRequest { deviceId = device }, reply => FinishLogin(reply, done));
#endif
        }

#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator InitTimeout(Func<bool> resolved, Action timeout)
        {
            yield return new WaitForSecondsRealtime(10f);
            if (!resolved()) timeout();
        }

        private void LoginDouyin(Action<ApiReply> done)
        {
            TT.Login((code, anonymousCode, isLogin) => PostRaw("auth/douyin", new ApiRequest { code = code, anonymousCode = anonymousCode }, reply => FinishLogin(reply, done)),
                error => done(new ApiReply { error = "抖音登录失败" }), false);
        }
#endif

        private void FinishLogin(ApiReply reply, Action<ApiReply> done)
        {
            if (!reply.ok || string.IsNullOrEmpty(reply.session)) { done(reply); return; }
            session = reply.session;
            Apply(reply);
            Get("bootstrap", done);
        }

        public void Get(string path, Action<ApiReply> done) => StartCoroutine(Request("GET", path, null, done));
        public void Post(string path, ApiRequest body, Action<ApiReply> done) => StartCoroutine(Request("POST", path, body ?? new ApiRequest(), done));
        private void PostRaw(string path, ApiRequest body, Action<ApiReply> done) => StartCoroutine(Request("POST", path, body, done));

        private IEnumerator Request(string method, string path, ApiRequest body, Action<ApiReply> done)
        {
            if (string.IsNullOrWhiteSpace(Config.apiUrl)) { done(new ApiReply { error = "对战服务尚未配置" }); yield break; }
            string url = Config.apiUrl.TrimEnd('/') + "/api/v3/" + path;
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 10;
                if (method == "POST")
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrEmpty(session)) request.SetRequestHeader("Authorization", "Bearer " + session);
                yield return request.SendWebRequest();
                reachable = request.responseCode > 0;
                ApiReply reply = null;
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
                    try { reply = JsonUtility.FromJson<ApiReply>(request.downloadHandler.text); } catch (Exception) { }
                if (reply == null) reply = new ApiReply { error = "网络不可用，当前为离线练习" };
                if (!reply.ok && string.IsNullOrEmpty(reply.error)) reply.error = "网络请求失败";
                if (request.responseCode == 401) { session = ""; Profile = null; reachable = false; }
                Apply(reply);
                done(reply);
            }
        }

        private void Apply(ApiReply reply)
        {
            // JsonUtility may materialize an empty nested object when a JSON
            // response does not contain a profile (for example, match/start).
            bool hasProfile = reply.profile != null && !string.IsNullOrEmpty(reply.profile.playerId);
            if (hasProfile) Profile = reply.profile;
            if (!string.IsNullOrEmpty(reply.date)) Date = reply.date;
            // A versioned leaderboard also contains date and rules but no seed.
            // Only bootstrap and run-start replies may replace a seed with zero.
            if (reply.seed != 0 || (!string.IsNullOrEmpty(reply.date) && (hasProfile || !string.IsNullOrEmpty(reply.runId)))) Seed = reply.seed;
            if (reply.serverTime > 0) { ServerTime = reply.serverTime; ServerTimeReceivedAt = Time.realtimeSinceStartup; }
            if (!string.IsNullOrEmpty(reply.date) && hasProfile) AdEnabled = reply.adEnabled;
        }

        public long EstimatedServerTime => ServerTime + (long)((Time.realtimeSinceStartup - ServerTimeReceivedAt) * 1000);
    }

    public static class GamePlatform
    {
        public static bool SupportsHaptics
        {
            get
            {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static void VibrateShort()
        {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
            // An unsupported device or host must not interrupt a match.
            try { TT.VibrateShort(new VibrateShortParam()); }
            catch (Exception) { }
#endif
        }

        public static bool HasRewardedAds(GameApiClient api)
        {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
            return api.AdEnabled && !string.IsNullOrEmpty(api.Config.rewardedAdUnitId);
#else
            return false;
#endif
        }

        public static void ShowRewarded(GameApiClient api, Action<bool, string> done)
        {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
            if (!HasRewardedAds(api)) { done(false, "广告暂不可用"); return; }
            var ad = TT.CreateRewardedVideoAd(new CreateRewardedVideoAdParam { AdUnitId = api.Config.rewardedAdUnitId });
            bool finished = false;
            ad.OnClose += (ended, count) => { if (finished) return; finished = true; ad.Destroy(); done(ended, ended ? "" : "已取消观看"); };
            ad.OnError += (code, message) => { if (finished) return; finished = true; ad.Destroy(); done(false, "广告暂不可用"); };
            ad.Show();
#else
            done(false, "广告暂不可用");
#endif
        }

        public static void ShareChallenge(string token, int score, Action<string> done)
        {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
            var share = new JsonData();
            share["channel"] = "invite"; share["title"] = "今日挑战 " + score + " 分，来比一比"; share["query"] = "challenge=" + Uri.EscapeDataString(token);
            TT.ShareAppMessage(share, result => done(""), error => done("分享暂不可用"), () => done("已取消分享"));
#else
            done("当前平台暂不支持分享");
#endif
        }
    }
}
