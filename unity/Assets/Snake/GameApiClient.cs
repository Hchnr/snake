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
        public bool experimentalFixedPve;
        public bool experimentalStrengthPve;
        public bool experimentalFixedPveServer;
        public bool experimentalStrengthPveServer;
        public bool experimentalLocalStrengthPveServer;
        public bool experimentalComparison;
        public bool ComparisonPreview => experimentalComparison && !allowDevLogin && !experimentalFixedPve && !experimentalFixedPveServer && !experimentalStrengthPveServer && !experimentalLocalStrengthPveServer &&
            string.IsNullOrWhiteSpace(apiUrl) && string.IsNullOrWhiteSpace(serverUrl);
        // Explicit development-only loopback preview; no platform identity or remote host.
        public bool FixedPveServerPreview
        {
            get
            {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return experimentalFixedPveServer && !experimentalStrengthPveServer && !experimentalLocalStrengthPveServer && allowDevLogin &&
                    string.IsNullOrWhiteSpace(serverUrl) && Uri.TryCreate(apiUrl, UriKind.Absolute, out var url) &&
                    url.Scheme == Uri.UriSchemeHttp && url.Host == "127.0.0.1" && string.IsNullOrEmpty(url.UserInfo) &&
                    url.AbsolutePath == "/" && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);
#endif
            }
        }
        // The server-backed strength experiment is independently opt-in and
        // restricted to the same loopback-only, non-Douyin development context.
        public bool StrengthPveServerPreview
        {
            get
            {
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return experimentalStrengthPveServer && !experimentalLocalStrengthPveServer && !experimentalFixedPveServer && !experimentalFixedPve &&
                    !experimentalStrengthPve && !experimentalComparison && allowDevLogin &&
                    string.IsNullOrWhiteSpace(serverUrl) && Uri.TryCreate(apiUrl, UriKind.Absolute, out var url) &&
                    url.Scheme == Uri.UriSchemeHttp && url.Host == "127.0.0.1" && string.IsNullOrEmpty(url.UserInfo) &&
                    url.AbsolutePath == "/" && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);
#endif
            }
        }
        // This separate v13 verifier is available only to an explicit local
        // development build; it must never become a Douyin/reward pathway.
        public bool LocalStrengthPveServerPreview
        {
            get
            {
#if SNAKE_DOUYIN_TTSDK && !UNITY_EDITOR
                return false;
#else
                return experimentalLocalStrengthPveServer && !experimentalStrengthPveServer && !experimentalFixedPveServer &&
                    !experimentalFixedPve && !experimentalStrengthPve && !experimentalComparison && allowDevLogin &&
                    string.IsNullOrWhiteSpace(serverUrl) && Uri.TryCreate(apiUrl, UriKind.Absolute, out var url) &&
                    url.Scheme == Uri.UriSchemeHttp && url.Host == "127.0.0.1" && string.IsNullOrEmpty(url.UserInfo) &&
                    url.AbsolutePath == "/" && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);
#endif
            }
        }
        // Preview is allowed only in a deliberately isolated offline build.
        public bool FixedPvePreview => experimentalFixedPve && !experimentalStrengthPveServer && !experimentalLocalStrengthPveServer && !allowDevLogin &&
            string.IsNullOrWhiteSpace(apiUrl) && string.IsNullOrWhiteSpace(serverUrl);
        public bool StrengthPvePreview => experimentalStrengthPve && FixedPvePreview && !experimentalFixedPveServer && !experimentalComparison;
        public bool StrengthRulesPreview => StrengthPvePreview || StrengthPveServerPreview || LocalStrengthPveServerPreview;
    }

    [Serializable] public sealed class LeaderboardRow
    {
        public int rank, score;
        public string playerId = "", name = "";
    }

    // firstRunCode is sent by the server as a mandatory discriminator because
    // Unity JsonUtility cannot reliably distinguish a missing bool from false.
    [Serializable] public sealed class LocalStrengthStartConfig
    {
        public const string CurrentVersion = "local-strength-eval-setup-1";
        public string version = "";
        public bool firstRun;
        public int firstRunCode;
        public bool Supported => version == CurrentVersion &&
            ((firstRunCode == 1 && firstRun) || (firstRunCode == 2 && !firstRun));
    }

    [Serializable] public sealed class ApiReply
    {
        public bool ok, verified, adEnabled, experimental;
        [NonSerialized] public long httpStatus;
        public string error = "", session = "", runId = "", token = "", date = "", mode = "", rules = "", id = "", shareToken = "";
        public int seed, score, rank, total, gap, coins, baseCoins, experience, fragments, questCoins, questExperience;
        public int ticks, kills, length, eventPoints, rewards;
        public string winner = "", stateDigest = "";
        public int trialSkin = -1, unlockedSkin = -1;
        public long serverTime, expiresAt;
        public PlayerProgress profile;
        public PveStartConfig startConfig;
        public StrengthEvalStartConfig strengthStartConfig;
        public LocalStrengthStartConfig localStrengthStartConfig;
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
        public int ChallengeRevision { get; private set; }
        public event Action<bool> PlatformVisibilityChanged;
        private void NotifyPlatformVisibility(bool visible) => PlatformVisibilityChanged?.Invoke(visible);
        private string session = "";
        private readonly SessionGeneration sessionGeneration = new SessionGeneration();
        private bool reachable;
#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
        private bool platformReady;
        private TTAppLifeCycle lifecycle;
        private RewardedAdAttempt activeAd;
        internal void TrackAd(RewardedAdAttempt attempt)
        {
            if (activeAd != null && !activeAd.Finished) throw new InvalidOperationException("An ad is already active");
            activeAd = attempt;
        }
        internal void ReleaseAd(RewardedAdAttempt attempt)
        {
            if (ReferenceEquals(activeAd, attempt)) activeAd = null;
        }
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
                        lifecycle = TT.GetAppLifeCycle();
                        if (lifecycle != null) { lifecycle.OnShow += OnPlatformShow; lifecycle.OnHide += OnPlatformHide; }
                    }
                    catch (Exception) { /* Cold launch still works without lifecycle callbacks. */ }
                    try
                    {
                        var launch = TT.GetLaunchOptionsSync();
                        if ((object)launch != null && launch.Query != null && launch.Query.TryGetValue("challenge", out var token)) AcceptChallenge(token);
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
        private void AcceptChallenge(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 256) return;
            LaunchChallengeToken = token;
            ChallengeRevision++;
        }

        private void OnPlatformShow(Dictionary<string, object> data)
        {
            NotifyPlatformVisibility(true);
            try
            {
                if (data == null || !data.TryGetValue("query", out var query) || query == null) return;
                if (query is IDictionary<string, object> objects && objects.TryGetValue("challenge", out var value))
                    AcceptChallenge(value as string);
                else if (query is IDictionary<string, string> strings && strings.TryGetValue("challenge", out var token))
                    AcceptChallenge(token);
                else if (query is JsonData json && json.IsObject && json.Keys.Contains("challenge"))
                    AcceptChallenge(json["challenge"].ToString());
            }
            catch (Exception) { /* Ignore malformed platform parameters. */ }
        }

        private void OnPlatformHide() => NotifyPlatformVisibility(false);

        private void OnDestroy()
        {
            var attempt = activeAd; activeAd = null;
            attempt?.Cancel();
            if (lifecycle != null) { lifecycle.OnShow -= OnPlatformShow; lifecycle.OnHide -= OnPlatformHide; }
        }

        private IEnumerator InitTimeout(Func<bool> resolved, Action timeout)
        {
            yield return new WaitForSecondsRealtime(10f);
            if (!resolved()) timeout();
        }

        private IEnumerator LoginCallbackTimeout(Action timeout)
        {
            yield return new WaitForSecondsRealtime(10f);
            timeout();
        }

        private void LoginDouyin(Action<ApiReply> done)
        {
            // Offline preview still initializes TTSDK for platform features,
            // but must not request an identity without a configured backend.
            if (string.IsNullOrWhiteSpace(Config.apiUrl)) { done(new ApiReply { error = "当前为离线练习" }); return; }
            int finished = 0;
            void FinishOnce(ApiReply reply)
            {
                if (System.Threading.Interlocked.CompareExchange(ref finished, 1, 0) == 0) done(reply);
            }
            PlatformLoginFlow.Start(
                (success, failure) => TT.Login((code, anonymousCode, isLogin) => success(code, anonymousCode), error => failure(), false),
                timeout => StartCoroutine(LoginCallbackTimeout(timeout)),
                (code, anonymousCode) => PostRaw("auth/douyin", new ApiRequest { code = code, anonymousCode = anonymousCode }, reply => FinishLogin(reply, FinishOnce)),
                error => FinishOnce(new ApiReply { error = error }));
        }
#endif

        private void FinishLogin(ApiReply reply, Action<ApiReply> done)
        {
            if (!reply.ok || string.IsNullOrEmpty(reply.session)) { done(reply); return; }
            session = reply.session;
            sessionGeneration.Advance();
            // A fresh login also permits recovery from a restored server backup.
            Profile = null;
            Apply(reply);
            Get("bootstrap", done);
        }

        public void Get(string path, Action<ApiReply> done) => StartCoroutine(Request("GET", path, null, done));
        public void Post(string path, ApiRequest body, Action<ApiReply> done) => StartCoroutine(Request("POST", path, body ?? new ApiRequest(), done));
        private void PostRaw(string path, ApiRequest body, Action<ApiReply> done) => StartCoroutine(Request("POST", path, body, done));

        private IEnumerator Request(string method, string path, ApiRequest body, Action<ApiReply> done)
        {
            if (string.IsNullOrWhiteSpace(Config.apiUrl)) { done(new ApiReply { error = "对战服务尚未配置" }); yield break; }
            long requestGeneration = sessionGeneration.Capture();
            string url = Config.apiUrl.TrimEnd('/') + "/api/v3/" + path;
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("X-Snake-Catalog", "2");
                request.timeout = 10;
                if (method == "POST")
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrEmpty(session)) request.SetRequestHeader("Authorization", "Bearer " + session);
                yield return request.SendWebRequest();
                if (!sessionGeneration.IsCurrent(requestGeneration))
                {
                    // Still complete the caller so loading and retry state can recover.
                    done(new ApiReply { error = "登录状态已变化，请重试" });
                    yield break;
                }
                reachable = request.responseCode > 0;
                ApiReply reply = null;
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
                    try { reply = JsonUtility.FromJson<ApiReply>(request.downloadHandler.text); } catch (Exception) { }
                if (reply == null) reply = new ApiReply { error = "网络不可用，当前为离线练习" };
                reply.httpStatus = request.responseCode;
                if (!reply.ok && string.IsNullOrEmpty(reply.error)) reply.error = "网络请求失败";
                if (request.responseCode == 401) { session = ""; Profile = null; reachable = false; sessionGeneration.Advance(); }
                Apply(reply);
                done(reply);
            }
        }

        private void Apply(ApiReply reply)
        {
            // JsonUtility may materialize an empty nested object when a JSON
            // response does not contain a profile (for example, match/start).
            bool hasProfile = reply.profile != null && !string.IsNullOrEmpty(reply.profile.playerId);
            bool acceptProfile = ProfileSnapshot.ShouldAccept(Profile, reply.profile);
            if (acceptProfile) Profile = reply.profile;
            // Responses can arrive out of order, including across Beijing midnight.
            bool freshTime = reply.serverTime > 0 && reply.serverTime >= ServerTime;
            if (!string.IsNullOrEmpty(reply.date) && (freshTime || (reply.serverTime <= 0 && (string.IsNullOrEmpty(Date) || string.CompareOrdinal(reply.date, Date) >= 0)))) Date = reply.date;
            // Leaderboards have no seed; stale responses must not restore yesterday's seed.
            if (reply.mode != "pve" && reply.mode != "fixed-eval" && !string.IsNullOrEmpty(reply.date) && reply.date == Date && (reply.serverTime <= 0 || freshTime) &&
                (reply.seed != 0 || hasProfile || !string.IsNullOrEmpty(reply.runId))) Seed = reply.seed;
            if (freshTime) { ServerTime = reply.serverTime; ServerTimeReceivedAt = Time.realtimeSinceStartup; }
            if (!string.IsNullOrEmpty(reply.date) && acceptProfile) AdEnabled = reply.adEnabled;
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
            RewardedAdFlow.Start(
                () => new DouyinRewardedAd(TT.CreateRewardedVideoAd(new CreateRewardedVideoAdParam { AdUnitId = api.Config.rewardedAdUnitId })),
                api.TrackAd, api.ReleaseAd, done);
#else
            done(false, "广告暂不可用");
#endif
        }

#if SNAKE_DOUYIN_TTSDK && UNITY_WEBGL && !UNITY_EDITOR
        private sealed class DouyinRewardedAd : IRewardedAdAdapter
        {
            private readonly TTRewardedVideoAd ad;
            private RewardedAdClosedDelegate closed;
            private AdErrorDelegate error;
            public DouyinRewardedAd(TTRewardedVideoAd ad) { this.ad = ad ?? throw new InvalidOperationException("No rewarded ad instance"); }
            public void SubscribeClose(Action<bool> callback) { closed = (ended, count) => callback(ended); ad.OnClose += closed; }
            public void SubscribeError(Action callback) { error = (code, message) => callback(); ad.OnError += error; }
            public void UnsubscribeClose() { if (closed != null) ad.OnClose -= closed; }
            public void UnsubscribeError() { if (error != null) ad.OnError -= error; }
            public void Show() => ad.Show();
            public void Destroy() => ad.Destroy();
        }
#endif

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
