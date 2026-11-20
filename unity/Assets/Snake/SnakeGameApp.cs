using System;
using System.Collections.Generic;
using SnakeClub.Core;
using UnityEngine;

namespace SnakeClub
{
    public sealed partial class SnakeGameApp : MonoBehaviour
    {
        [Serializable] private sealed class Request
        {
            public string type;
            public string room;
            public string mode;
            public string name;
            public float x;
            public float y;
            public bool boost;
        }
        [Serializable] private sealed class Reply
        {
            public string type = "";
            public string error = "";
            public string room = "";
            public string mode = "";
            public string playerId = "";
            public bool fruitsIncluded = false;
            public ArenaState state = null;
        }

        private ArenaRenderer arenaRenderer;
        private ArenaFeedback feedback;
        private RoomConnection connection;
        private Arena arena;
        private PveFixedWorld fixedPve;
        private ArenaState state;
        private string playerId = "";
        private string nameInput = "玩家";
        private string serverInput = "ws://127.0.0.1:2567/ws";
        private string roomInput = "";
        private string room = "";
        private string mode = "pvp";
        private string pendingType = "";
        private string status = "";
        private bool inGame;
        private bool online;
        private bool onlineMenu;
        private bool paused;
        private bool appFocused = true, appPaused, platformVisible = true;
        private bool SimulationSuspended => paused || appPaused || !appFocused || !platformVisible || adShowing;
        private bool pointerBoost;
        private bool reducedEffects;
        private int viewPreference;
        private readonly DragSteering dragSteering = new DragSteering();
        private readonly MatchHighlights matchHighlights = new MatchHighlights();
        private readonly NameplateLayout nameplates = new NameplateLayout();
        private readonly MinimapOcclusion minimapOcclusion = new MinimapOcclusion();
        private ArenaState minimapWorld;
        private int minimapFrame = -1;
        private string opponentEdgeId = "";
        private ArenaState weakEdgeWorld;
        private int weakEdgeFrame = -1, weakEdgeSegment = -1;
        private string weakEdgeId = "";
        private bool weakEdgeAvailable;
        private WeakBodyEdgeTarget weakEdgeTarget;
        private Func<Point, Point> weakEdgeProjection;
        private float weakProjectionScale, weakProjectionLeft, weakProjectionTop, weakProjectionHeight;
        private struct NameplateEntry { public Rect box; public Vector2 anchor; public Color color; public string label; }
        private readonly List<NameplateEntry> nameplateEntries = new List<NameplateEntry>(8);
        private readonly List<EdgeObstacle> edgeObstacles = new List<EdgeObstacle>(9);
        private readonly List<Vector2> occludedRecoveryMarkers = new List<Vector2>(PveFixedWorld.RecoverySupplyCount);
        private readonly List<Rect> recoveryObstacles = new List<Rect>(12);
        private static readonly Rect GameTopBar = new Rect(48, 50, 984, 188);
        private static readonly Rect GameMiniMap = new Rect(790, 285, 240, 240);
        private static readonly Rect GamePauseButton = new Rect(55, 285, 215, 76);
        private static readonly Rect StrengthGuidancePanel = new Rect(100, 1790, 880, 100);
        private const float RecoveryMarkerHalfSize = 23f;
        private readonly EventOutcomeNotice eventOutcome = new EventOutcomeNotice();
        private readonly EliminationFeedback eliminationFeedback = new EliminationFeedback();
        private readonly KillFeedback killFeedback = new KillFeedback();
        private readonly StrengthProtectionNotice strengthProtectionNotice = new StrengthProtectionNotice();
        private readonly List<int> activeTouchIds = new List<int>();
        private int suppressPointerUpFrame = -1;
        private bool suppressPendingDragUp;
        private bool firstRun, tutorialHints = true, hasSteered, tutorialClosedLoop, tutorialRebounded, tutorialBoosted;
        private bool depthMode, depthWon;
        private int depthRoom;
        private int depthShield, depthRebound, depthGrowth;
        private int controlActions;
        private Point direction = new Point(1, 0);
        private float stepTimer;
        private float sendTimer;
        private Texture2D rounded;
        private GUIStyle panelStyle;
        private Font uiFont;
        [NonSerialized] private bool initializedInDomain;

        private void Awake()
        {
            initializedInDomain = true;
            Application.targetFrameRate = 60;
            arenaRenderer = gameObject.AddComponent<ArenaRenderer>();
            feedback = gameObject.AddComponent<ArenaFeedback>();
            connection = gameObject.AddComponent<RoomConnection>();
            connection.OnEvent = HandleSocket;
            api = gameObject.AddComponent<GameApiClient>();
            api.PlatformVisibilityChanged += OnPlatformVisibilityChanged;
            LoadConfiguration();
            LoadPreferences();
            uiFont = Resources.Load<Font>("NotoSansSC");
            rounded = CreateRoundTexture();
            SetupHomeScreen();
            Demo();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || initializedInDomain) return;
            initializedInDomain = true;
            arenaRenderer = GetComponent<ArenaRenderer>();
            feedback = GetComponent<ArenaFeedback>();
            connection = GetComponent<RoomConnection>();
            api = GetComponent<GameApiClient>();
            if (arenaRenderer == null) arenaRenderer = gameObject.AddComponent<ArenaRenderer>();
            if (feedback == null) feedback = gameObject.AddComponent<ArenaFeedback>();
            if (connection == null) connection = gameObject.AddComponent<RoomConnection>();
            if (api == null) api = gameObject.AddComponent<GameApiClient>();
            api.PlatformVisibilityChanged -= OnPlatformVisibilityChanged;
            api.PlatformVisibilityChanged += OnPlatformVisibilityChanged;
            connection.OnEvent = HandleSocket;
            LoadConfiguration();
            LoadPreferences();
            uiFont = Resources.Load<Font>("NotoSansSC");
            rounded = CreateRoundTexture(); panelStyle = null;
            homeAvatar = GetComponent<HomeAvatar>();
            SetupHomeScreen();
            inGame = false; online = false; paused = false; daily = null; activeRun = null;
            connecting = false;
            frontPage = "home";
            Demo();
            ConnectProfile();
        }

        private void LoadConfiguration()
        {
            TextAsset configAsset = Resources.Load<TextAsset>("GameConfig");
            if (configAsset != null)
            {
                try
                {
                    GameConfiguration config = JsonUtility.FromJson<GameConfiguration>(configAsset.text);
                    api.Configure(config);
                    if (config != null && !string.IsNullOrWhiteSpace(config.serverUrl)) serverInput = config.serverUrl;
                }
                catch (Exception) { Debug.LogWarning("GameConfig.json could not be read."); }
            }
        }

        private void Start() => ConnectProfile();

        private void Demo()
        {
            arena = new Arena(24, nearbyOpponents: true, enableCaptures: true);
            for (int i = 0; i < 8; i++) arena.AddSnake("demo-" + i, "AI " + (i + 1), true);
            arena.Start();
            state = arena.State;
            playerId = "";
            stepTimer = 0;
        }

        private void BeginPve(ApiReply run)
        {
            nameplates.ClearHistory(); opponentEdgeId = "";
            dragSteering.Reset();
            fixedPve = null;
            connection.Close();
            online = false;
            inGame = true;
            paused = false;
            status = "";
            room = "";
            playerId = "you";
            var startConfig = run != null && run.startConfig != null && run.startConfig.Supported ? run.startConfig : null;
            firstRun = startConfig != null ? startConfig.firstRun : !CurrentProfile.tutorialComplete;
            int arenaSeed = run != null ? run.seed : unchecked((int)DateTime.UtcNow.Ticks);
            string playerName = string.IsNullOrWhiteSpace(nameInput) ? "玩家" : nameInput.Trim();
            if (!depthMode) arena = Arena.CreateStandardPve(arenaSeed, firstRun, playerName);
            else
            {
                arena = new Arena(arenaSeed, roundSeconds: 55f, nearbyOpponents: true, enableCaptures: true);
                arena.AddSnake(playerId, playerName, false);
                for (int i = 0; i < 7; i++) arena.AddSnake("bot-" + i, "AI " + (i + 1), true);
                if (depthRoom == 2) arena.ConfigureStar(5f, playerId);
                else arena.DisableStar();
                if (depthRoom == 3)
                {
                    arena.SetStartingLength("bot-0", 20);
                    arena.ConfigureBoss("bot-0");
                    arena.State.eventKind = "bounty"; arena.State.eventStatus = "active"; arena.State.eventTarget = "bot-0";
                }
                arena.Start();
            }
            state = arena.State;
            matchHighlights.Reset(); matchHighlights.Observe(state, playerId); eventOutcome.Reset(); eliminationFeedback.Reset(); eliminationFeedback.Observe(state, playerId); killFeedback.Reset(); killFeedback.Observe(state, playerId);
            stepTimer = 0;
            direction = new Point(1, 0);
            controlActions = 0; hasSteered = false; tutorialClosedLoop = false; tutorialRebounded = false; tutorialBoosted = false;
            daily = null;
            runInputs.Clear();
            activeRun = run;
            activePlayerId = api.Profile != null ? api.Profile.playerId : "";
            activePracticeAccount = run == null && !depthMode && api.Connected ? api.Profile.playerId : "";
            lastRewardId = "";
            resultRecorded = false;
            int skin = startConfig != null ? startConfig.skin : run != null && run.trialSkin > 0 ? run.trialSkin : CurrentProfile.selectedSkin;
            var matchProfile = startConfig != null ? startConfig.MatchProfile() : new PlayerProgress { selectedSkin = skin, talents = CurrentProfile.talents };
            var bonuses = Progression.Bonuses(matchProfile);
            if (depthMode)
            {
                bonuses.captureShieldSeconds = Mathf.Min(4f, depthShield * 2f);
                bonuses.reboundFreeSeconds = Mathf.Min(6f, depthRebound * 3f);
                bonuses.bonusGrowthPerKill = Mathf.Min(6, depthGrowth * 3);
                bonuses.boostCostPenalty = DepthChallenge.GrowthBoostPenalty(depthGrowth);
            }
            arena.SetBonuses(playerId, bonuses);
            arenaRenderer.SelectedSkin = SkinCatalog.IsReference(EffectiveSkin) || (run == null && OfflineCosmeticPreview && offlineVisualSkin >= 0) ? EffectiveSkin : skin;
        }

        private void BeginFixedPve() => BeginFixedPveWithRun(null);

        private void BeginFixedPveWithRun(ApiReply run) => BeginFixedPveCore(run, false);

        private void BeginDefaultStrengthPractice() => BeginFixedPveCore(null, true);

        private void BeginFixedPveCore(ApiReply run, bool defaultStrength)
        {
            defaultStrengthPractice = defaultStrength;
            nameplates.ClearHistory(); opponentEdgeId = "";
            dragSteering.Reset(); connection.Close();
            online = false; inGame = true; paused = false; status = ""; room = "";
            playerId = "you"; arena = null; daily = null; depthMode = false;
            bool strengthRun = run != null && run.mode == "strength-eval";
            bool localStrengthRun = run != null && run.mode == "strength-local-eval";
            firstRun = run == null ? ((defaultStrength || api.Config.StrengthPvePreview || api.Config.LocalStrengthPveServerPreview) ? !strengthPracticeTutorialComplete : !practiceProfile.tutorialComplete) :
                localStrengthRun ? run.localStrengthStartConfig.firstRun : strengthRun ? run.strengthStartConfig.firstRun : run.startConfig.firstRun;
            var profile = run != null && !strengthRun && !localStrengthRun ? run.startConfig.MatchProfile() : practiceProfile;
            bool localStrength = localStrengthRun || (run == null && (defaultStrength || api.Config.StrengthRulesPreview));
            fixedPve = new PveFixedWorld(run != null ? run.seed : unchecked((int)DateTime.UtcNow.Ticks), firstRun, profile,
                strengthRun || localStrength, strengthRun ? run.strengthStartConfig : null,
                localAdvantage: localStrength, localOpeningFood: localStrength, localOpeningEncounter: localStrength,
                localRecoverySupply: localStrength, localSafeRecovery: localStrength, localAlignedStart: localStrength,
                localCappedStart: localStrength, localStableBlocked: localStrength, localRecoveryPriority: localStrength,
                localSqueezeEscape: localStrength, localMultiBite: localStrength, localHeadTieAfterBite: localStrength, localOpeningBoost: localStrength,
                localFreeBoost: localStrength && !localStrengthRun, localOpeningDefense: localStrength && !localStrengthRun,
                localOpeningFruit: localStrength && !localStrengthRun);
            state = fixedPve.RenderState();
            strengthProtectionNotice.Reset(); strengthProtectionNotice.Observe(fixedPve, state, playerId);
            matchHighlights.Reset(); matchHighlights.Observe(state, playerId);
            eventOutcome.Reset(); eliminationFeedback.Reset(); eliminationFeedback.Observe(state, playerId);
            killFeedback.Reset(); killFeedback.Observe(state, playerId);
            stepTimer = 0; direction = new Point(1, 0); pointerBoost = false;
            controlActions = 0; hasSteered = false; tutorialClosedLoop = false; tutorialRebounded = false; tutorialBoosted = false;
            activeRun = run; activePlayerId = run != null && api.Profile != null ? api.Profile.playerId : "";
            activePracticeAccount = ""; lastRewardId = ""; resultRecorded = false; runInputs.Clear();
            verifiedDaily = false; verifiedShareToken = "";
            resultNote = fixedPve.StrengthPreview ? "强弱吞食试玩 · 不计账号奖励或排行" : "整数规则试玩 · 不计账号奖励或排行";
            arenaRenderer.SelectedSkin = run != null && !strengthRun ? run.startConfig.skin : EffectiveSkin;
        }

        private void StartOnline(string action)
        {
            nameplates.ClearHistory(); opponentEdgeId = "";
            dragSteering.Reset();
            string address = serverInput.Trim();
            if (!address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                status = "对战服务尚未配置";
                return;
            }
            if (action == "join" && string.IsNullOrWhiteSpace(roomInput)) { status = "请先填写房间号"; return; }
            online = true;
            inGame = true;
            paused = false;
            depthMode = false;
            arena = null; fixedPve = null;
            state = null;
            killFeedback.Reset();
            playerId = "";
            room = "";
            pendingType = action;
            status = "正在连接房间服务…";
            connection.Open(address);
        }

        private void HandleSocket(string eventType, string data)
        {
            if (!inGame || !online) return;
            if (eventType == "open")
            {
                status = "正在进入房间…";
                Send(new Request { type = pendingType, mode = mode, room = roomInput.Trim().ToUpperInvariant(), name = nameInput });
            }
            else if (eventType == "message")
            {
                Reply reply;
                try { reply = JsonUtility.FromJson<Reply>(data); }
                catch (Exception) { status = "服务器返回了无法读取的数据"; return; }
                if (reply == null) return;
                if (reply.type == "joined")
                {
                    playerId = reply.playerId;
                    room = reply.room;
                    status = "";
                }
                else if (reply.type == "state" && reply.state != null)
                {
                    if (!reply.fruitsIncluded)
                        reply.state.fruits = state != null && state.fruits != null ? state.fruits : new List<FruitState>();
                    state = reply.state;
                }
                else if (reply.type == "error") status = reply.error;
            }
            else if (eventType == "error" || eventType == "close")
            {
                Debug.LogWarning("Room connection: " + data);
                status = "连接已断开，请检查网络后重试";
            }
        }

        private void Send(Request request) => connection.Send(JsonUtility.ToJson(request));

        private void BackToMenu()
        {
            nameplates.ClearHistory(); opponentEdgeId = "";
            dragSteering.Reset();
            connection.Close();
            inGame = false;
            online = false;
            onlineMenu = false;
            paused = false;
            depthMode = false;
            status = "";
            daily = null;
            fixedPve = null;
            defaultStrengthPractice = false;
            activeRun = null;
            frontPage = api.Config.ComparisonPreview && comparisonVariant >= 0 ? "comparison" : "home";
            comparisonVariant = -1;
            arenaRenderer.SelectedSkin = EffectiveSkin;
            Demo();
        }

        private void Update()
        {
            PumpIncomingFriend();
            PumpPending();
            PumpCaveSync();
            bool interaction = Input.anyKeyDown || Input.GetMouseButtonDown(0);
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) interaction = true;
            feedback.UpdateMusic(inGame && state != null && state.phase == "running", appFocused && !appPaused && platformVisible, paused, adShowing, interaction);
            if (inGame && !SimulationSuspended)
            {
                feedback.RebaselineAfterSuspend(state, playerId);
                killFeedback.Observe(state, playerId, fixedPve != null && fixedPve.StrengthPreview);
            }
            bool homeVisible = !inGame && (frontPage == "home" || frontPage == "skins") && !developerRooms;
            bool caveVisible = !inGame && frontPage == "caves" && !developerRooms;
            bool wardrobePreview = homeVisible && frontPage == "skins" && homeScreen != null && homeScreen.ArenaPreview;
            bool renderWardrobePreview = wardrobePreview && appFocused && !appPaused && platformVisible && !adShowing;
            bool resultVisible = inGame && !online && !depthMode && state != null && state.phase == "ended" && string.IsNullOrEmpty(status);
            bool resultWon = resultVisible && (fixedPve != null && fixedPve.StrengthPreview ?
                StrengthMatchOutcome.IsVictory(state, playerId) : MatchOutcome.IsVictory(state, playerId));
            if (homeScreen != null)
            {
                homeScreen.Visible = homeVisible || resultVisible;
                if (homeVisible || resultVisible)
                {
                    homeScreen.SetPage(frontPage == "skins", resultVisible);
                    bool trial = !DefaultStrengthMain && CurrentProfile.tutorialComplete && !CurrentProfile.dailyTrialUsed && GamePlatform.HasRewardedAds(api);
                    homeScreen.SetState(CurrentProfile, homeAvatar != null ? homeAvatar.Texture : null, frontNotice, busy, api.Connected,
                        feedback.Enabled, EffectiveSkin, previewSkin, ReferencePreviewEnabled, trial,
                        !string.IsNullOrEmpty(friendDate) && friendDate == api.Date, reducedEffects, feedback.HapticsEnabled, GamePlatform.SupportsHaptics, feedback.MusicEnabled, feedback.MusicVolume, feedback.EffectsVolume, friendScore, friendRules, friendDate, tutorialHints, api.Config.FixedPvePreview || api.Config.FixedPveServerPreview, TutorialReady, api.Config.StrengthRulesPreview || DefaultStrengthMain, api.Config.StrengthPveServerPreview, OfflineCosmeticPreview, viewPreference);
                    if (resultVisible)
                    {
                        SnakeState own = state.snakes.Find(s => s.id == playerId);
                        string title = comparisonVariant >= 0 && api.Config.ComparisonPreview ? "对照 " + (char)('A' + comparisonVariant) + " · 结束" : daily != null ? "挑战结束" : fixedPve != null && fixedPve.StrengthPreview ? (StrengthMatchOutcome.IsSharedFirst(state, playerId) ? "并列第一" : resultWon ? "吞食胜利" : "吞食结束") : state.winner == playerId ? "围猎胜利" : "围猎结束";
                        string stats = comparisonVariant >= 0 && api.Config.ComparisonPreview ?
                            "最长 " + daily.PeakLength + "   ·   淘汰 " + (own != null ? own.kills : 0) :
                            daily != null ? "挑战得分 " + daily.Score + "   ·   淘汰 " + (own != null ? own.kills : 0) + "\n最长 " + daily.PeakLength :
                            own == null ? "" : fixedPve != null && fixedPve.StrengthPreview ? "当前长度 " + own.segments.Count + "   ·   淘汰 " + own.kills + "\n最长 " + matchHighlights.PeakLength : "最长 " + matchHighlights.PeakLength + "   ·   淘汰 " + own.kills;
                        if (own != null && (state.eventStatus == "active" || state.eventStatus == "ended") &&
                            (state.eventKind == "bounty" || state.eventKind == "star" || state.eventKind == "fruit"))
                            stats += "   ·   事件点 " + own.starPoints;
                        bool share = daily != null && activeRun != null && api.Connected && verifiedDaily &&
                            activeRun.date == api.Date && DailyMatch.SupportsFriendChallenge(activeRun.rules) && !string.IsNullOrEmpty(verifiedShareToken);
                        int adCoins = !string.IsNullOrEmpty(lastRewardId) && GamePlatform.HasRewardedAds(api) ? lastRewardCoins : 0;
                        string highlight = fixedPve != null && fixedPve.StrengthPreview ? StrengthMatchSummary.Text(fixedPve) :
                            matchHighlights.BestCapture > 0 ? "最佳围猎 · 一次圈住 " + matchHighlights.BestCapture + " 个对手" : "";
                        string outcome = fixedPve != null && fixedPve.StrengthPreview ? StrengthMatchOutcome.Explain(state, playerId) : MatchOutcome.Explain(state, playerId);
                        homeScreen.SetResult(title, stats, outcome, resultNote, busy, share, adCoins, highlight, resultWon);
                    }
                }
            }
            if (homeAvatar != null)
            {
                homeAvatar.Visible = (homeVisible || resultVisible || caveVisible) && appFocused && !appPaused && platformVisible && !adShowing &&
                    (!renderWardrobePreview || arenaRenderer.PreviewTexture == null);
                homeAvatar.ReducedEffects = reducedEffects;
                homeAvatar.SetResult(resultVisible, resultWon);
                if (homeAvatar.Visible) homeAvatar.Skin = homeVisible && frontPage == "skins" ? previewSkin :
                    resultVisible && activeRun != null && activeRun.trialSkin > 0 && SkinCatalog.IsOwnedSkin(EffectiveSkin) ? activeRun.trialSkin : EffectiveSkin;
            }
            if (inGame && !SimulationSuspended && CanControlPlayer()) ReadControls();
            else { dragSteering.Reset(); pointerBoost = false; }
            if (daily != null && inGame && !SimulationSuspended)
            {
                stepTimer += Mathf.Min(Time.deltaTime, .2f);
                while (stepTimer >= .05f && !daily.Finished)
                {
                    byte heading = EncodeDirection(direction);
                    daily.Step(heading, pointerBoost);
                    matchHighlights.Observe(daily.State, playerId); ObserveTutorial(daily.State); eventOutcome.Observe(daily.State, playerId); eliminationFeedback.Observe(daily.State, playerId); killFeedback.Observe(daily.State, playerId);
                    runInputs.Add(heading); runInputs.Add(pointerBoost ? (byte)1 : (byte)0);
                    stepTimer -= .05f;
                }
                state = daily.State;
            }
            AdvanceFixedPve(Time.deltaTime);
            if (arena != null && !SimulationSuspended && (!homeVisible || renderWardrobePreview))
            {
                stepTimer += Mathf.Min(Time.deltaTime, .2f);
                while (stepTimer >= .05f && arena.State.phase == "running")
                {
                    if (inGame && !online && LocalPveRound.EndIfPlayerEliminated(arena, playerId)) break;
                    if (inGame)
                    {
                        if (!depthMode && arena.State.phase == "running")
                        {
                            byte heading = EncodeDirection(direction);
                            arena.SetInput(playerId, PveRecording.Direction(heading), pointerBoost);
                            runInputs.Add(heading); runInputs.Add(pointerBoost ? (byte)1 : (byte)0);
                        }
                        else arena.SetInput(playerId, direction, pointerBoost);
                    }
                    arena.Tick(.05f);
                    if (inGame) { matchHighlights.Observe(arena.State, playerId); ObserveTutorial(arena.State); eventOutcome.Observe(arena.State, playerId); eliminationFeedback.Observe(arena.State, playerId); killFeedback.Observe(arena.State, playerId); }
                    if (inGame && depthMode && arena.State.phase == "running" &&
                        DepthChallenge.Evaluate(depthRoom, arena.State, playerId) != DepthChallenge.RoomStatus.InProgress) arena.EndRound();
                    if (inGame && !online) LocalPveRound.EndIfPlayerEliminated(arena, playerId);
                    stepTimer -= .05f;
                }
                if (!inGame && (arena.State.phase == "ended" || (wardrobePreview && (arena.Find("demo-0") == null || !arena.Find("demo-0").alive)))) Demo();
                state = arena.State;
                if (inGame && depthMode && state.phase == "ended") depthWon = DepthGoalReached(state);
            }
            if (inGame && !SimulationSuspended && state != null && state.phase == "running")
            {
                if (online && !string.IsNullOrEmpty(playerId))
                {
                    sendTimer += Time.deltaTime;
                    if (sendTimer >= .1f)
                    {
                        sendTimer = 0;
                        Send(new Request { type = "input", x = direction.x, y = direction.y, boost = pointerBoost });
                    }
                }
            }
            if (inGame && !online && state != null && state.phase == "ended" && !resultRecorded) RecordResult();
            if (inGame && !SimulationSuspended) feedback.Observe(state, playerId, fixedPve);
            arenaRenderer.SetPreviewMode(renderWardrobePreview);
            if (wardrobePreview) arenaRenderer.SelectedSkin = previewSkin;
            arenaRenderer.Visible = appFocused && !appPaused && platformVisible && !adShowing &&
                (!homeVisible || (renderWardrobePreview && arenaRenderer.PreviewTexture != null));
            if (homeScreen != null) homeScreen.SetArenaPreview(renderWardrobePreview ? arenaRenderer.PreviewTexture : null);
            arenaRenderer.ReducedEffects = reducedEffects;
            arenaRenderer.LegacyDangerWarning = fixedPve == null || !fixedPve.StrengthPreview;
            arenaRenderer.StrengthBodyVisual = fixedPve != null && fixedPve.StrengthPreview;
            arenaRenderer.OpeningBoostVisual = fixedPve != null && fixedPve.OpeningBoostRemaining > 0;
            arenaRenderer.ViewPreference = viewPreference;
            arenaRenderer.MotionPaused = SimulationSuspended;
            if (arenaRenderer.Visible) arenaRenderer.Draw(state, wardrobePreview ? "demo-0" : playerId);
        }

        private void AdvanceFixedPve(float deltaTime)
        {
            if (fixedPve == null || !inGame || SimulationSuspended) return;
            stepTimer += Mathf.Min(Mathf.Max(0, deltaTime), .2f);
            while (stepTimer >= .05f && !fixedPve.Finished)
            {
                byte heading = EncodeDirection(direction);
                bool recordedSteering = fixedPve.LocalAlignedStart && hasSteered;
                fixedPve.Step(heading, pointerBoost, recordedSteering);
                runInputs.Add(heading); runInputs.Add((byte)((pointerBoost ? 1 : 0) | (recordedSteering ? 2 : 0)));
                state = fixedPve.RenderState();
                strengthProtectionNotice.Observe(fixedPve, state, playerId);
                matchHighlights.Observe(state, playerId); ObserveTutorial(state); eventOutcome.Observe(state, playerId); eliminationFeedback.Observe(state, playerId); killFeedback.Observe(state, playerId, fixedPve.StrengthPreview);
                stepTimer -= .05f;
            }
        }

        private bool CanControlPlayer()
        {
            if (state == null || state.phase != "running" || string.IsNullOrEmpty(playerId)) return false;
            var player = state.snakes.Find(s => s.id == playerId);
            return player != null && player.alive;
        }

        private void ReadControls()
        {
            float x = 0, y = 0;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x--;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x++;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y++;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y--;
            pointerBoost = Input.GetKey(KeyCode.Space);
            bool acted = x != 0 || y != 0 || pointerBoost;
            if (x != 0 || y != 0) { direction = new Point(x, y).Normalized; hasSteered = true; }

            activeTouchIds.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    if (touch.phase == TouchPhase.Ended)
                    {
                        Vector2 last = ToVirtual(touch.position);
                        if (dragSteering.Move(touch.fingerId, new Point(last.x, last.y), out Point next)) { direction = next; acted = true; hasSteered = true; }
                    }
                    if (dragSteering.End(touch.fingerId)) { suppressPointerUpFrame = Time.frameCount; suppressPendingDragUp = true; }
                }
                else activeTouchIds.Add(touch.fingerId);
            }
            dragSteering.ReconcileTouches(activeTouchIds);
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) continue;
                Vector2 position = ToVirtual(touch.position);
                Point point = new Point(position.x, position.y);
                if (!dragSteering.Active) dragSteering.Begin(touch.fingerId, point);
                else if (dragSteering.Move(touch.fingerId, point, out Point next)) { direction = next; acted = true; hasSteered = true; }
            }
            // Holding a second finger adds an optional boost without reserving a screen area.
            if (activeTouchIds.Count > 1) { pointerBoost = true; acted = true; }
            if (Input.touchCount == 0)
            {
                Vector2 position = ToVirtual(Input.mousePosition);
                Point point = new Point(position.x, position.y);
                if (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0) && dragSteering.IsTracking(-1)) dragSteering.Reset();
                if (Input.GetMouseButtonDown(0)) dragSteering.Begin(-1, point);
                if (Input.GetMouseButton(0) && dragSteering.Move(-1, point, out Point next)) { direction = next; acted = true; hasSteered = true; }
                if (Input.GetMouseButtonUp(0))
                {
                    if (dragSteering.Move(-1, point, out Point last)) { direction = last; acted = true; hasSteered = true; }
                    if (dragSteering.End(-1)) { suppressPointerUpFrame = Time.frameCount; suppressPendingDragUp = true; }
                }
            }
            if (acted) controlActions++;
        }

        private void ObserveTutorial(ArenaState world)
        {
            if (!firstRun || world == null) return;
            if (world.captureTick >= 0 && world.captureOwner == playerId) tutorialClosedLoop = true;
            var own = world.snakes.Find(s => s.id == playerId);
            if (own != null && own.rebound > 0) tutorialRebounded = true;
            if (own != null && own.boosting) tutorialBoosted = true;
        }

        private static void SafeUiMetrics(out float scale, out float left, out float top)
        {
            SafeUiMetrics(Screen.safeArea, Screen.width, Screen.height, out scale, out left, out top);
        }

        private static void SafeUiMetrics(Rect safe, float width, float height, out float scale, out float left, out float top)
        {
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, width, height);
            scale = Mathf.Min(safe.width / 1080f, safe.height / 1920f);
            left = safe.xMin + (safe.width - 1080 * scale) * .5f;
            top = height - safe.yMax + (safe.height - 1920 * scale) * .5f;
        }

        internal static WeakBodyViewport WeakBodySafeViewport(Rect safe, float width, float height,
            out float scale, out float left, out float top)
        {
            scale = left = top = 0;
            if (float.IsNaN(width) || float.IsNaN(height) || float.IsInfinity(width) || float.IsInfinity(height) || width <= 0 || height <= 0)
                return default;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, width, height);
            SafeUiMetrics(safe, width, height, out scale, out left, out top);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) return default;
            return new WeakBodyViewport((safe.xMin - left) / scale, (height - safe.yMax - top) / scale,
                (safe.xMax - left) / scale, (height - safe.yMin - top) / scale);
        }

        // Virtual IMGUI top-left coordinates to Unity bottom-left pixels.
        internal static Rect GameUiScreenRect(Rect rect, Rect safe, float width, float height)
        {
            SafeUiMetrics(safe, width, height, out float scale, out float left, out float top);
            return new Rect(left + rect.x * scale, height - top - (rect.y + rect.height) * scale,
                rect.width * scale, rect.height * scale);
        }

        // Projected sprite bounds use bottom-left screen pixels; nameplate
        // layout uses top-left virtual UI units, including the safe-area inset.
        internal static Rect NameplateVirtualBounds(Rect pixels, Rect safe, float width, float height)
        {
            SafeUiMetrics(safe, width, height, out float scale, out float left, out float top);
            return new Rect((pixels.xMin - left) / scale, (height - pixels.yMax - top) / scale,
                pixels.width / scale, pixels.height / scale);
        }

        internal static bool ReservePlayerNameplateBounds(NameplateLayout layout, ArenaRenderer renderer,
            ArenaState world, string playerId, Rect safe, float width, float height)
        {
            if (layout == null || renderer == null || world == null || width <= 0 || height <= 0) return false;
            var own = world.snakes.Find(s => s.id == playerId && s.alive && s.segments != null && s.segments.Count > 0);
            if (own == null || (!renderer.TryCharacterScreenBounds(playerId, out Rect bounds) &&
                !renderer.TryFallbackHeadScreenBounds(own.segments[0], out bounds))) return false;
            Rect area = NameplateVirtualBounds(bounds, safe, width, height);
            layout.Reserve(area.x, area.y, area.width, area.height);
            return true;
        }

        internal static Rect RecoveryMarkerSafeArea(Rect safe, float width, float height)
        {
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, width, height);
            SafeUiMetrics(safe, width, height, out float scale, out _, out _);
            float padding = RecoveryMarkerHalfSize * scale;
            return Rect.MinMaxRect(safe.xMin + padding, safe.yMin + padding, safe.xMax - padding, safe.yMax - padding);
        }

        private static void AddRecoveryObstacle(List<Rect> obstacles, Rect area, float padding)
        {
            obstacles.Add(Rect.MinMaxRect(area.xMin - padding, area.yMin - padding, area.xMax + padding, area.yMax + padding));
        }

        internal static void PrepareRecoveryHudObstacles(List<Rect> obstacles, Rect safe, float width, float height)
        {
            SafeUiMetrics(safe, width, height, out float scale, out _, out _);
            float padding = RecoveryMarkerHalfSize * scale;
            obstacles.Clear();
            AddRecoveryObstacle(obstacles, GameUiScreenRect(GameTopBar, safe, width, height), padding);
            AddRecoveryObstacle(obstacles, GameUiScreenRect(GameMiniMap, safe, width, height), padding);
            AddRecoveryObstacle(obstacles, GameUiScreenRect(GamePauseButton, safe, width, height), padding);
        }

        // The contact panel exists only while its real notice is displayed.
        // Use its shared layout so the HUD and recovery guidance cannot drift.
        internal static void AppendRecoveryContactObstacle(List<Rect> obstacles, bool visible, EdgeObstacle area,
            Rect safe, float width, float height)
        {
            if (!visible) return;
            SafeUiMetrics(safe, width, height, out float scale, out _, out _);
            var rect = new Rect(area.x, area.y, area.width, area.height);
            AddRecoveryObstacle(obstacles, GameUiScreenRect(rect, safe, width, height), RecoveryMarkerHalfSize * scale);
        }

        internal static void PrioritizeSqueezeGuidance(string squeeze, ref string protection, ref string recovery)
        {
            if (!string.IsNullOrEmpty(squeeze)) { protection = ""; recovery = squeeze; }
        }

        internal static void ResolveStrengthGuidance(string protection, string contact, string capture, string recovery, bool intro, bool parallelExpiry,
            out bool showProtection, out bool showRecovery, out bool showIntro)
        {
            showProtection = !string.IsNullOrEmpty(protection) && (parallelExpiry
                ? string.IsNullOrEmpty(recovery)
                : string.IsNullOrEmpty(contact) && string.IsNullOrEmpty(capture));
            showRecovery = !showProtection && !string.IsNullOrEmpty(recovery);
            showIntro = !showProtection && !showRecovery && intro;
        }

        internal static void AppendRecoveryHeadlineObstacle(List<Rect> obstacles, bool visible, Rect headline, Rect safe, float width, float height)
        {
            if (!visible) return;
            SafeUiMetrics(safe, width, height, out float scale, out _, out _);
            AddRecoveryObstacle(obstacles, GameUiScreenRect(headline, safe, width, height), RecoveryMarkerHalfSize * scale);
        }

        internal static void AppendRecoveryGuidanceObstacle(List<Rect> obstacles, bool visible, Rect safe, float width, float height)
        {
            if (!visible) return;
            SafeUiMetrics(safe, width, height, out float scale, out _, out _);
            AddRecoveryObstacle(obstacles, GameUiScreenRect(StrengthGuidancePanel, safe, width, height), RecoveryMarkerHalfSize * scale);
        }

        internal static void AppendRecoveryCharacterObstacles(List<Rect> obstacles, ArenaRenderer renderer,
            ArenaState world, string playerId, float padding)
        {
            foreach (var other in world.snakes)
            {
                if (other == null || !other.alive || other.id == playerId || other.segments == null || other.segments.Count == 0) continue;
                if (renderer.TryCharacterScreenBounds(other.id, out Rect otherBounds) ||
                    renderer.TryFallbackHeadScreenBounds(other.segments[0], out otherBounds)) AddRecoveryObstacle(obstacles, otherBounds, padding);
            }
        }

        private Vector2 ToVirtual(Vector2 screen)
        {
            SafeUiMetrics(out float scale, out float left, out float top);
            return new Vector2((screen.x - left) / scale, ((Screen.height - screen.y) - top) / scale);
        }

        private void OnApplicationPause(bool isPaused)
        {
            appPaused = isPaused;
            if (isPaused) { caveSync.Invalidate(); feedback?.SuspendAudio(); SuspendInput(); }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            appFocused = hasFocus;
            if (!hasFocus) { caveSync.Invalidate(); feedback?.SuspendAudio(); SuspendInput(); }
        }

        private void OnPlatformVisibilityChanged(bool visible)
        {
            platformVisible = visible;
            if (!visible) { caveSync.Invalidate(); feedback?.SuspendAudio(); SuspendInput(); }
        }

        private void OnDestroy()
        {
            if (api != null) api.PlatformVisibilityChanged -= OnPlatformVisibilityChanged;
        }

        private void SuspendInput()
        {
            dragSteering.Reset(); pointerBoost = false;
            killFeedback.Suspend();
            if (inGame && !online) paused = true;
        }

        private bool ResultOverlayVisible => homeScreen != null && inGame && !online && !depthMode &&
            state != null && state.phase == "ended" && string.IsNullOrEmpty(status);

        private void OnGUI()
        {
            if (rounded == null || ResultOverlayVisible) return;
            if (inGame && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp))
            {
                int activeTouches = 0;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    TouchPhase phase = Input.GetTouch(i).phase;
                    if (phase != TouchPhase.Ended && phase != TouchPhase.Canceled) activeTouches++;
                }
                bool mouseUp = Event.current.type == EventType.MouseUp;
                if (!mouseUp && activeTouches <= 1 && !dragSteering.Moved) suppressPendingDragUp = false;
                bool dragReleased = suppressPointerUpFrame == Time.frameCount || suppressPendingDragUp || dragSteering.Moved;
                if (DragSteering.SuppressGuiPointer(mouseUp, dragReleased, activeTouches))
                {
                    // UI release may arrive before or after the input frame.
                    // Consume it and clear any captured IMGUI button.
                    if (mouseUp) suppressPendingDragUp = false;
                    GUIUtility.hotControl = 0;
                    Event.current.Use();
                }
            }
            SafeUiMetrics(out float scale, out float left, out float top);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(left, top, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            if (uiFont != null) GUI.skin.font = uiFont;
            if (!inGame) DrawFront(); else DrawGame();
            GUI.matrix = old;
        }

        private void DrawLegacyMenu()
        {
            Panel(new Rect(58, 180, 964, 1485), new Color(.035f, .065f, .12f, .93f));
            Label(new Rect(105, 235, 860, 50), "S N A K E   C L U B", 28, new Color(.36f, .95f, .78f), TextAnchor.MiddleCenter, true);
            Label(new Rect(105, 310, 860, 115), "蛇蛇竞技场", 78, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(145, 435, 790, 90), "大地图 · 真对手 · 随时反击", 34, new Color(.70f, .79f, .88f), TextAnchor.MiddleCenter);
            if (!onlineMenu)
            {
                Label(new Rect(135, 595, 810, 55), "选择对战方式", 38, Color.white, TextAnchor.MiddleLeft, true);
                if (Button(new Rect(130, 690, 820, 155), "单人挑战   /   PvE", new Color(.10f, .60f, .49f))) RequestPve();
                Label(new Rect(160, 850, 760, 50), "离线对抗 AI，随时开始", 27, new Color(.62f, .74f, .82f), TextAnchor.MiddleLeft);
                if (Button(new Rect(130, 950, 820, 155), "真人房间   /   PvP", new Color(.20f, .31f, .60f))) { mode = "pvp"; onlineMenu = true; status = ""; }
                Label(new Rect(160, 1110, 760, 50), "通过房间号邀请真人对战", 27, new Color(.62f, .74f, .82f), TextAnchor.MiddleLeft);
                if (Button(new Rect(130, 1210, 820, 155), "混合对战   /   MIX", new Color(.48f, .28f, .61f))) { mode = "mixed"; onlineMenu = true; status = ""; }
                Label(new Rect(160, 1370, 760, 50), "真人与 AI 在同一场竞技", 27, new Color(.62f, .74f, .82f), TextAnchor.MiddleLeft);
                Label(new Rect(130, 1540, 820, 60), "吃果实成长 · 击败对手 · 撞墙反弹", 27, new Color(.70f, .80f, .86f), TextAnchor.MiddleCenter);
            }
            else
            {
                Label(new Rect(130, 575, 820, 70), mode == "pvp" ? "真人房间" : "混合对战", 44, Color.white, TextAnchor.MiddleLeft, true);
                Label(new Rect(130, 680, 820, 45), "玩家昵称", 29, new Color(.70f, .80f, .86f), TextAnchor.MiddleLeft);
                nameInput = TextField(new Rect(130, 735, 820, 85), nameInput, 16);
                Label(new Rect(130, 870, 820, 45), "房间号（加入时填写）", 29, new Color(.70f, .80f, .86f), TextAnchor.MiddleLeft);
                roomInput = TextField(new Rect(130, 925, 820, 85), roomInput, 12);
                Label(new Rect(130, 1065, 820, 100), "创建房间后，将房间号分享给朋友即可加入。", 28, new Color(.60f, .72f, .79f), TextAnchor.MiddleLeft);
                if (Button(new Rect(130, 1280, 390, 120), "创建房间", new Color(.10f, .60f, .49f))) StartOnline("create");
                if (Button(new Rect(560, 1280, 390, 120), "加入房间", new Color(.20f, .36f, .67f))) StartOnline("join");
                if (Button(new Rect(130, 1450, 820, 90), "返回", new Color(.15f, .22f, .31f))) { onlineMenu = false; status = ""; }
                if (!string.IsNullOrEmpty(status)) Label(new Rect(130, 1550, 820, 70), status, 25, new Color(1f, .66f, .55f), TextAnchor.MiddleCenter);
            }
        }

        // During escape, keep the kill announcement away from the crowded
        // top combat/nameplate area and above the escape guidance panel.
        internal static Rect HeadlineNoticeArea(bool escapeActive, bool killVisible)
            => escapeActive && killVisible ? new Rect(250, 1160, 580, 110) : new Rect(250, 430, 580, 110);

        internal static bool EscapeHeadlineActive(PveFixedWorld world, ArenaState state)
            => world != null && world.LocalSqueezeEscape && !world.Finished && state != null &&
                state.phase == "running" && world.SqueezeEscapeTicks > 0;

        private void DrawGame()
        {
            // Keep this first: the other controls and labels vary during a
            // match, and changing their IMGUI order can invalidate the
            // pause button's captured control between pointer down and up.
            if (Button(GamePauseButton, online ? "退出" : (paused ? "继续" : "暂停"), new Color(.10f, .17f, .25f, .92f)))
            {
                if (online) { BackToMenu(); return; }
                else { paused = !paused; if (paused) feedback.SuspendAudio(); }
            }
            string strengthNotice = StrengthContactNotice.Text(fixedPve, state != null ? state.tick : -1, out bool strengthWarning);
            bool showContactNotice = !string.IsNullOrEmpty(strengthNotice);
            bool captureVisible = false;
            if (state != null && state.captureTick >= 0 && state.tick >= state.captureTick && state.tick - state.captureTick < 12 &&
                state.capturePolygon != null && state.capturePolygon.Count > 0)
            {
                Vector3 point = arenaRenderer.HeadToScreen(state.capturePolygon[0]);
                Vector2 projected = ToVirtual(point);
                captureVisible = point.z > 0 && projected.x >= 0 && projected.x <= 1080 && projected.y >= 0 && projected.y <= 1920;
            }
            string captureText = CaptureNotice.Text(state, playerId, captureVisible, fixedPve != null && fixedPve.StrengthPreview);
            string protectionText = strengthProtectionNotice.Text(fixedPve, state, playerId, SimulationSuspended);
            string recoveryText = RecoverySupplyNotice.Text(fixedPve, state != null ? state.tick : -1, SimulationSuspended);
            string squeezeText = StrengthSqueezeEscapeNotice.Text(fixedPve, state, playerId, SimulationSuspended);
            PrioritizeSqueezeGuidance(squeezeText, ref protectionText, ref recoveryText);
            bool parallelExpiry = fixedPve != null && fixedPve.LocalOpeningBoost && strengthProtectionNotice.IsExpiryActive(fixedPve, state, playerId, SimulationSuspended);
            ResolveStrengthGuidance(protectionText, strengthNotice, captureText, recoveryText, StrengthIntroVisible(), parallelExpiry,
                out bool showProtection, out bool showRecovery, out bool showStrengthIntro);
            bool showGuidance = showProtection || showRecovery || showStrengthIntro;
            ContactNoticeLayout.TryGetArea(showContactNotice, false, out var contactNoticeArea);
            string killMessage = killFeedback.Current(state);
            bool showKillNotice = !string.IsNullOrEmpty(killMessage);
            bool escapeActive = EscapeHeadlineActive(fixedPve, state);
            Rect headlineArea = HeadlineNoticeArea(escapeActive, showKillNotice);
            string levelNotice = !paused && state != null ? StrengthLevelNotice.Text(fixedPve, state.tick) : "";
            bool showLevelNotice = !string.IsNullOrEmpty(levelNotice) && !showKillNotice;
            // Draw first so labels, prompts and controls always cover markers.
            DrawOccludedRecoveryMarkers(showContactNotice, contactNoticeArea, showGuidance,
                showKillNotice || showLevelNotice, headlineArea);
            Panel(GameTopBar, new Color(.035f, .065f, .12f, .90f));
            SnakeState me = state == null ? null : state.snakes.Find(s => s.id == playerId);
            Label(new Rect(88, 70, 330, 58), online ? (mode == "pvp" ? "真人对战" : "混合对战") : fixedPve != null ? (fixedPve.StrengthPreview ? "强弱吞食试玩" : "整数规则试玩") : comparisonVariant >= 0 && api.Config.ComparisonPreview ? "对照方案 " + (char)('A' + comparisonVariant) : depthMode ? "秘境深潜 " + depthRoom + "/3" : daily != null ? "每日挑战" : "单人挑战", 32, Color.white, TextAnchor.MiddleLeft, true);
            if (online && !string.IsNullOrEmpty(room)) Label(new Rect(88, 135, 410, 52), "房间  " + room, 28, new Color(.42f, .94f, .80f), TextAnchor.MiddleLeft);
            else if (fixedPve != null && fixedPve.LocalOpeningBoost && state != null && (state.phase == "ended" || (me != null && !me.alive)))
                Label(new Rect(88, 135, 580, 52), state.phase == "ended" ? "本局已结束" : "已淘汰 · 观战中", 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleLeft);
            else if (fixedPve != null && fixedPve.TryGetStrengthLevelProgress(playerId, out int matchLevel, out int matchXp, out int nextXp))
            {
                string opening = OpeningBoostNotice.Text(fixedPve);
                string progress = nextXp > 0 ? matchXp + "/" + nextXp : "已满级";
                string detail = activeRun != null && activeRun.mode == "strength-eval" ? "底力 " + fixedPve.StrengthBasePower + " · 硬度 " + (fixedPve.StrengthHardness / 10f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "% · 升级 " + (nextXp > 0 ? progress : "满级") : !string.IsNullOrEmpty(opening) ? opening + " · 升级 " + progress : (fixedPve.LocalStrengthAdvantage ? "玩家攻防优势 · " : "试玩 · ") + "升级 " + progress;
                bool freeBoostReady = fixedPve.LocalFreeBoost && fixedPve.FreeBoostRemaining > 0;
                if (freeBoostReady) detail = !string.IsNullOrEmpty(opening) ? opening + " · 免耗加速待用" : "免耗加速待用 · 升级 " + progress;
                Label(new Rect(88, 135, 580, 52), detail, freeBoostReady ? 24 : !string.IsNullOrEmpty(opening) ? 29 : 24, !string.IsNullOrEmpty(opening) ? new Color(1f, .80f, .38f) : new Color(.60f, .72f, .79f), TextAnchor.MiddleLeft, !string.IsNullOrEmpty(opening));
            }
            else Label(new Rect(88, 135, 410, 52), comparisonVariant >= 0 && api.Config.ComparisonPreview ? "试玩 · 不计奖励或排行" : depthMode ? "练习 · " + DepthGoalText() : daily != null ? (activeRun == null ? "练习局 · 不计排行" : "统一规则 · 真人排行") : activeRun == null ? "练习局 · 不计账号奖励" : activeRun.trialSkin > 0 ? "皮肤试穿中 · 本局有效" : "击败对手，生存到最后", 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleLeft);
            bool urgent = RoundTimerPresentation.IsUrgent(state);
            int timerSize = urgent ? (reducedEffects ? 44 : Mathf.RoundToInt(42 + 2 * Mathf.Sin(state.remaining * Mathf.PI * 2))) : 40;
            Color timerColor = urgent ? new Color(1f, .76f, .40f) : Color.white;
            Label(new Rect(530, 70, 185, 58), state == null ? "--:--" : FormatTime(state.remaining), timerSize, timerColor, TextAnchor.MiddleCenter, true);
            Label(new Rect(720, 70, 250, 58), me == null ? "--" : daily != null && !(comparisonVariant >= 0 && api.Config.ComparisonPreview) ? "分数 " + daily.Score : "长度 " + me.segments.Count, 30, Color.white, TextAnchor.MiddleRight, true);
            Label(new Rect(720, 135, 250, 52), me == null ? "" : (fixedPve != null && fixedPve.StrengthPreview ? "等级 " + fixedPve.StrengthLevel(playerId) + " · 淘汰 " + me.kills : "淘汰 " + me.kills + (depthMode ? "  星核 " : "  事件 ") + me.starPoints), 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleRight);

            if (arenaRenderer.VisualFallback)
                Label(new Rect(300, 235, 500, 45), "造型素材不可用 · 备用显示", 19, new Color(.9f, .92f, .84f), TextAnchor.MiddleCenter);
            if (state != null) DrawMiniMap(state);
            string depthHint = depthMode ? DepthChallenge.ObjectiveHint(depthRoom, state, playerId) : "";
            int strengthBuff = fixedPve != null && fixedPve.StrengthPreview ? fixedPve.StrengthBuffTicks(playerId) : 0;
            int loopBuff = fixedPve != null && fixedPve.StrengthPreview ? fixedPve.StrengthLoopTicks(playerId) : 0;
            if (fixedPve != null && fixedPve.StrengthPreview && (strengthBuff > 0 || loopBuff > 0 || state.strengthPickupVisible || state.strengthPickupPending))
            {
                string powerHint = strengthBuff > 0 ? "能量 +12% " + Mathf.CeilToInt(strengthBuff / 20f) + "秒" : "";
                if (loopBuff > 0) powerHint += (powerHint.Length > 0 ? " · " : "") + "围猎 +10% " + Mathf.CeilToInt(loopBuff / 20f) + "秒";
                if (state.strengthPickupPending)
                    powerHint += (powerHint.Length > 0 ? "\n" : "") + "能量 " + state.strengthPickupPendingSeconds + "秒后出现";
                else if (state.strengthPickupVisible && powerHint.Length == 0) powerHint = "能量出现 · 拾取后攻防强化";
                Panel(new Rect(275, 285, 490, 96), new Color(.15f, .12f, .19f, .87f));
                Label(new Rect(285, 292, 470, 82), powerHint, 23, new Color(1f, .87f, .55f), TextAnchor.MiddleCenter, true);
            }
            if (!string.IsNullOrEmpty(depthHint))
            {
                Panel(new Rect(275, 285, 490, 96), new Color(.15f, .12f, .19f, .87f));
                Label(new Rect(285, 292, 470, 82), depthHint, 23, new Color(1f, .87f, .55f), TextAnchor.MiddleCenter, true);
            }
            else if (!depthMode && state != null && state.eventStatus == "active")
            {
                string eventHint = state.eventKind == "bounty" ? "巨蛇悬赏 · 围猎标记对手" : state.eventKind == "star" ? "星核争夺 · 携带得事件点" : "果潮混战 · 圈内淘汰得事件点";
                var boss = state.snakes.Find(s => s.boss && s.alive);
                if (boss != null) eventHint = boss.bossWindup > 0 ? "首领蓄力！躲开橙色方向线 · 护盾 " + boss.bossShield : "首领护盾 " + boss.bossShield + " · 拦截或收圈";
                if (state.eventKind == "star" && state.starVisible)
                    eventHint = string.IsNullOrEmpty(state.starHolder) ? "星核出现 · 快去争夺" : state.starHolder == playerId ? "携带星核 · 坚持 " + Mathf.CeilToInt((1f - state.starProgress) * 8f) + " 秒" : "对手携带星核 · 去围猎";
                Panel(new Rect(275, 285, 490, 96), new Color(.15f, .12f, .19f, .87f));
                Label(new Rect(285, 292, 470, 82), eventHint + "  " + Mathf.CeilToInt(state.eventRemaining) + "秒", 23, new Color(1f, .87f, .55f), TextAnchor.MiddleCenter, true);
            }
            else if (!depthMode && state != null && state.starVisible)
            {
                string starHint = string.IsNullOrEmpty(state.starHolder) ? "星核出现" : state.starHolder == playerId ? "携带星核 " + Mathf.CeilToInt((1f - state.starProgress) * 10f) + " 秒" : "对手携带星核";
                Label(new Rect(275, 300, 490, 60), starHint, 25, new Color(1f, .85f, .48f), TextAnchor.MiddleCenter, true);
            }
            if (!depthMode && state != null && state.eventStatus != "active")
            {
                string eventMessage = eventOutcome.Current(state);
                if (!string.IsNullOrEmpty(eventMessage))
                {
                    Panel(new Rect(275, 285, 490, 96), new Color(.15f, .12f, .19f, .87f));
                    Label(new Rect(285, 292, 470, 82), eventMessage, 23, new Color(1f, .87f, .55f), TextAnchor.MiddleCenter, true);
                }
            }
            if (showKillNotice)
            {
                float flash = reducedEffects ? 1f : 1f + .10f * Mathf.Sin(state.tick * .7f);
                Label(headlineArea, killMessage, Mathf.RoundToInt(37 * flash), new Color(1f, .88f, .45f), TextAnchor.MiddleCenter, true);
            }
            if (showLevelNotice)
                Label(headlineArea, levelNotice, 36, new Color(.78f, 1f, .64f), TextAnchor.MiddleCenter, true);
            bool showCaptureNotice = !showContactNotice && !string.IsNullOrEmpty(captureText);
            if (!showContactNotice) ContactNoticeLayout.TryGetArea(false, showCaptureNotice, out contactNoticeArea);
            bool hasContactNoticeArea = showContactNotice || showCaptureNotice;
            if (showCaptureNotice)
                Label(new Rect(contactNoticeArea.x, contactNoticeArea.y, contactNoticeArea.width, contactNoticeArea.height), captureText, 31, new Color(.85f, 1f, .70f), TextAnchor.MiddleCenter, true);
            string elimination = eliminationFeedback.Current(state);
            if (!string.IsNullOrEmpty(elimination))
            {
                Panel(new Rect(300, 1080, 480, 72), new Color(.035f, .09f, .13f, .86f));
                Label(new Rect(315, 1085, 450, 62), elimination, 24, new Color(.94f, .96f, .78f), TextAnchor.MiddleCenter, true);
            }
            if (showContactNotice)
            {
                Panel(new Rect(contactNoticeArea.x, contactNoticeArea.y, contactNoticeArea.width, contactNoticeArea.height), new Color(.025f, .08f, .14f, .90f));
                Label(new Rect(contactNoticeArea.x + 15, contactNoticeArea.y, contactNoticeArea.width - 30, contactNoticeArea.height), strengthNotice, 29,
                    strengthWarning ? new Color(1f, .88f, .65f) : new Color(.85f, 1f, .70f), TextAnchor.MiddleCenter, true);
            }
            if (showProtection || showRecovery || showStrengthIntro)
            {
                Panel(StrengthGuidancePanel, new Color(.025f, .08f, .14f, .90f));
                Label(new Rect(StrengthGuidancePanel.x + 25, StrengthGuidancePanel.y + 10, StrengthGuidancePanel.width - 50, StrengthGuidancePanel.height - 20),
                    showProtection ? protectionText : showRecovery ? recoveryText : StrengthTutorialHints.Select(state.tick, hasSteered, tutorialBoosted, fixedPve.LocalOpeningFood, fixedPve.LocalFreeBoost),
                    showProtection || showRecovery ? 30 : 25, new Color(.75f, .97f, .88f), TextAnchor.MiddleCenter, true);
            }
            bool strengthHeadWarning = !paused && fixedPve != null && fixedPve.StrengthPreview && state != null && !string.IsNullOrEmpty(state.strengthHeadThreat);
            if (strengthHeadWarning)
            {
                Panel(new Rect(250, 1635, 580, 74), new Color(.12f, .075f, .04f, .90f));
                var threat = state.snakes.Find(s => s.id == state.strengthHeadThreat);
                bool hasDirection = me != null && me.segments.Count > 0 && threat != null && threat.segments.Count > 0 &&
                    (threat.segments[0] - me.segments[0]).Length > .01f;
                Label(new Rect(270, 1640, 540, 64), hasDirection ? "附近有强敌 · 留意方向箭头" : "附近有强敌 · 注意碰头", 23, new Color(1f, .89f, .65f), TextAnchor.MiddleCenter, true);
            }
            else if (!paused && !(fixedPve != null && fixedPve.StrengthPreview) && DangerProbe.Find(state, playerId, out _))
                Label(new Rect(270, 1640, 540, 64), "附近有危险 · 留意方向标记", 23, new Color(1f, .89f, .65f), TextAnchor.MiddleCenter, true);
            else if (!paused && !(fixedPve != null && fixedPve.StrengthPreview) && DangerProbe.FindBeforeProtectionEnds(state, playerId, out _))
                Label(new Rect(230, 1640, 620, 64), "附近有对手 · 保护即将结束", 23, new Color(1f, .89f, .65f), TextAnchor.MiddleCenter, true);
            bool strengthBodyWarning = !paused && fixedPve != null && fixedPve.StrengthPreview && me != null && me.strengthThreatSegments.Contains(true);
            if (strengthBodyWarning)
            {
                Panel(new Rect(230, 1525, 620, 70), new Color(.12f, .055f, .045f, .90f));
                Label(new Rect(245, 1530, 590, 60), "红叉处身体有危险 · 留意对手", 23, new Color(1f, .83f, .72f), TextAnchor.MiddleCenter, true);
            }
            string strengthBoostHint = StrengthBoostNotice.Text(fixedPve, state, playerId, pointerBoost, SimulationSuspended);
            if (!string.IsNullOrEmpty(strengthBoostHint))
            {
                Panel(new Rect(220, 1715, 640, 70), new Color(.10f, .075f, .035f, .91f));
                Label(new Rect(235, 1720, 610, 60), strengthBoostHint, 23, new Color(1f, .90f, .69f), TextAnchor.MiddleCenter, true);
            }
            string boostHint = depthMode && !paused ? DepthChallenge.ReboundBoostHint(me) : "";
            if (!string.IsNullOrEmpty(boostHint))
            {
                Panel(new Rect(220, 1510, 640, 70), new Color(.035f, .12f, .17f, .90f));
                Label(new Rect(235, 1515, 610, 60), boostHint, 24, new Color(.75f, 1f, .90f), TextAnchor.MiddleCenter, true);
            }
            if (state != null && state.phase == "running")
            {
                DrawNames(state, showProtection || showRecovery || showStrengthIntro, hasContactNoticeArea, contactNoticeArea, showKillNotice || showLevelNotice, headlineArea);
                edgeObstacles.Clear();
                if (hasContactNoticeArea) edgeObstacles.Add(contactNoticeArea);
                if (showGuidance) edgeObstacles.Add(new EdgeObstacle(StrengthGuidancePanel.x, StrengthGuidancePanel.y, StrengthGuidancePanel.width, StrengthGuidancePanel.height));
                if (strengthHeadWarning) edgeObstacles.Add(new EdgeObstacle(250, 1635, 580, 74));
                if (strengthBodyWarning) edgeObstacles.Add(new EdgeObstacle(230, 1525, 620, 70));
                if (!string.IsNullOrEmpty(strengthBoostHint)) edgeObstacles.Add(new EdgeObstacle(220, 1715, 640, 70));
                if (showKillNotice || showLevelNotice) edgeObstacles.Add(new EdgeObstacle(headlineArea.x, headlineArea.y, headlineArea.width, headlineArea.height));
                foreach (var entry in nameplateEntries)
                    edgeObstacles.Add(new EdgeObstacle(entry.box.x, entry.box.y, entry.box.width, entry.box.height));
                bool hasObjectiveEdge = DrawEventTargetEdge(state, out Rect objectiveEdge);
                if (hasObjectiveEdge) edgeObstacles.Add(new EdgeObstacle(objectiveEdge.x, objectiveEdge.y, objectiveEdge.width, objectiveEdge.height));
                if (!DrawWeakBodyEdge(state)) DrawOpponentEdge(state);
            }
            if (firstRun && tutorialHints && !online && !paused && !(fixedPve != null && fixedPve.StrengthPreview) && CanControlPlayer())
            {
                string hint = FirstRunHints.Select(state.tick, hasSteered, tutorialClosedLoop, tutorialRebounded, me == null ? 0 : me.kills, FirstRunHints.ApproachingOwnTrail(me), tutorialBoosted);
                if (!string.IsNullOrEmpty(hint))
                {
                    Panel(new Rect(100, 550, 880, 100), new Color(.025f, .08f, .14f, .90f));
                    Label(new Rect(125, 560, 660, 80), hint, 25, new Color(.75f, .97f, .88f), TextAnchor.MiddleCenter, true);
                    if (SmallButton(new Rect(800, 570, 155, 60), "关闭提示", new Color(.16f, .26f, .31f), 20)) SetTutorialHints(false);
                }
            }
            if (!string.IsNullOrEmpty(status))
            {
                Panel(new Rect(110, 680, 860, 260), new Color(.04f, .08f, .14f, .94f));
                Label(new Rect(150, 705, 780, 120), status, 32, Color.white, TextAnchor.MiddleCenter);
                if (Button(new Rect(300, 840, 480, 75), "返回菜单", new Color(.20f, .34f, .49f))) BackToMenu();
            }
            else if (state == null || state.phase == "waiting")
                Label(new Rect(90, 650, 900, 100), state == null ? "正在连接…" : "等待另一位玩家加入", 36, Color.white, TextAnchor.MiddleCenter, true);
            else if (state.phase == "ended") { if (homeScreen == null || online || depthMode) DrawResult(me); }
            else if (me != null && !me.alive)
                Label(new Rect(120, 680, 840, 110), "已被淘汰 · 正在观战", 38, Color.white, TextAnchor.MiddleCenter, true);
            else if (paused)
            {
                bool strengthGuide = fixedPve != null && fixedPve.StrengthPreview;
                Panel(strengthGuide ? new Rect(130, 590, 820, 650) : new Rect(160, 660, 760, 300), new Color(.035f, .065f, .12f, .94f));
                Label(strengthGuide ? new Rect(200, 625, 680, 85) : new Rect(200, 700, 680, 85), "游戏已暂停", 46, Color.white, TextAnchor.MiddleCenter, true);
                if (strengthGuide)
                {
                    Label(new Rect(180, 720, 720, 280),
                        (fixedPve.LocalOpeningFruit ? "开局15秒吸引附近果实，攻防强化\n防御最后3秒逐渐降低\n" : fixedPve.LocalOpeningDefense ? "开局15秒攻防强化，防御最后3秒逐渐降低\n" : fixedPve.LocalOpeningBoost ? "开局15秒攻防额外强化（含出生保护）\n" : fixedPve.LocalStrengthAdvantage ? "本局玩家自带攻防优势\n" : "") + "任意位置拖动转向 · 第二指按住加速\n" + (fixedPve.LocalFreeBoost ? "累计前8秒加速免耗身体\n" : "") + "强度足够才能吃身体，不足时弹开\n金点和弱点箭头提示目标 · 红叉提示危险\n围住对手可获得短时攻防强化\n时间到按长度排名，同长比淘汰",
                        27, new Color(.76f, .92f, .90f), TextAnchor.MiddleCenter);
                }
                if (Button(strengthGuide ? new Rect(265, 1010, 550, 90) : new Rect(265, 825, 550, 90), "继续对战", new Color(.10f, .60f, .49f))) paused = false;
                if (Button(strengthGuide ? new Rect(185, 1120, 310, 75) : new Rect(345, 980, 390, 75), "返回菜单", new Color(.15f, .22f, .31f))) BackToMenu();
                if (strengthGuide && Button(new Rect(525, 1120, 365, 75), "视野：" + GrowthView.PreferenceLabel(viewPreference), new Color(.12f, .35f, .40f))) CycleViewPreference();
            }
            else if (!showGuidance) DrawControls();
        }

        // Screen coordinates here have Unity's bottom-left origin, matching
        // the renderer's sprite bounds and the actual projected fruit center.
        internal static void SelectOccludedRecoveryMarkers(ArenaState world, bool enabled, bool suspended,
            Rect playerBounds, Rect screenBounds, Point playerHead, Func<Point, Vector3> project, List<Rect> obstacles, List<Vector2> markers)
        {
            markers.Clear();
            if (!enabled || suspended || world == null || world.phase != "running" || project == null ||
                playerBounds.width <= 0 || playerBounds.height <= 0 || screenBounds.width <= 0 || screenBounds.height <= 0) return;
            float nearestDistance = float.PositiveInfinity;
            Vector2 nearest = default;
            foreach (var fruit in world.fruits)
            {
                if (!fruit.recoverySupply) continue;
                Vector3 point = project(fruit.position);
                var screen = new Vector2(point.x, point.y);
                if (point.z <= 0 || !screenBounds.Contains(screen)) continue;
                bool obstructed = false;
                if (obstacles != null)
                    foreach (Rect obstacle in obstacles) if (obstacle.Contains(screen)) { obstructed = true; break; }
                if (obstructed) continue;
                // A clearly separate on-screen supply already gives the
                // player somewhere to go; avoid putting marks over the art.
                if (!playerBounds.Contains(screen)) return;
                float dx = fruit.position.x - playerHead.x, dy = fruit.position.y - playerHead.y;
                float distance = dx * dx + dy * dy;
                if (distance < nearestDistance) { nearestDistance = distance; nearest = screen; }
            }
            if (!float.IsPositiveInfinity(nearestDistance)) markers.Add(nearest);
        }

        internal static bool TryRecoveryPlayerBounds(ArenaRenderer renderer, SnakeState player, string playerId, out Rect bounds)
        {
            bounds = default;
            if (renderer == null || player == null || !player.alive || player.segments == null || player.segments.Count == 0) return false;
            return renderer.TryCharacterScreenBounds(playerId, out bounds) ||
                renderer.TryFallbackHeadScreenBounds(player.segments[0], out bounds);
        }

        private void DrawOccludedRecoveryMarkers(bool showContactNotice, EdgeObstacle contactNoticeArea, bool showGuidance,
            bool showHeadlineNotice, Rect headlineArea)
        {
            if (Event.current.type != EventType.Repaint || fixedPve == null || !fixedPve.StrengthPreview || !fixedPve.LocalRecoverySupply || state == null ||
                arenaRenderer == null) return;
            var player = state.snakes.Find(s => s.id == playerId);
            if (!TryRecoveryPlayerBounds(arenaRenderer, player, playerId, out Rect bounds)) return;
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            SafeUiMetrics(out float uiScale, out _, out _);
            float padding = RecoveryMarkerHalfSize * uiScale;
            PrepareRecoveryHudObstacles(recoveryObstacles, safe, Screen.width, Screen.height);
            AppendRecoveryContactObstacle(recoveryObstacles, showContactNotice, contactNoticeArea, safe, Screen.width, Screen.height);
            AppendRecoveryGuidanceObstacle(recoveryObstacles, showGuidance, safe, Screen.width, Screen.height);
            AppendRecoveryHeadlineObstacle(recoveryObstacles, showHeadlineNotice, headlineArea, safe, Screen.width, Screen.height);
            AppendRecoveryCharacterObstacles(recoveryObstacles, arenaRenderer, state, playerId, padding);
            Rect safeMarker = RecoveryMarkerSafeArea(safe, Screen.width, Screen.height);
            SelectOccludedRecoveryMarkers(state, true, SimulationSuspended, bounds, safeMarker,
                player.segments[0], arenaRenderer.FruitToScreen, recoveryObstacles, occludedRecoveryMarkers);
            if (occludedRecoveryMarkers.Count == 0) return;
            Color oldColor = GUI.color;
            foreach (Vector2 screen in occludedRecoveryMarkers)
            {
                Vector2 p = ToVirtual(screen);
                // Four open corners identify the real position rather than
                // drawing another fruit or an offset pickup target. A dark
                // outline keeps the pale corners visible over bright art.
                const float r = 20, length = 10, width = 3, outline = RecoveryMarkerHalfSize - r;
                for (int pass = 0; pass < 2; pass++)
                {
                    GUI.color = pass == 0 ? new Color(.015f, .055f, .045f, .96f) : new Color(.90f, 1f, .76f, 1f);
                    float pad = pass == 0 ? outline : 0;
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    {
                        float left = p.x + x * r - (x < 0 ? 0 : length);
                        float top = p.y + y * r - (y < 0 ? 0 : width);
                        GUI.DrawTexture(new Rect(left - pad, top - pad, length + 2 * pad, width + 2 * pad), Texture2D.whiteTexture);
                        left = p.x + x * r - (x < 0 ? 0 : width);
                        top = p.y + y * r - (y < 0 ? 0 : length);
                        GUI.DrawTexture(new Rect(left - pad, top - pad, width + 2 * pad, length + 2 * pad), Texture2D.whiteTexture);
                    }
                }
            }
            GUI.color = oldColor;
        }

        private void DrawResult(SnakeState me)
        {
            if (depthMode) { DrawDepthResult(me); return; }
            Panel(new Rect(125, 610, 830, 690), new Color(.035f, .065f, .12f, .96f));
            bool comparison = comparisonVariant >= 0 && api.Config.ComparisonPreview;
            string result = comparison ? "对照 " + (char)('A' + comparisonVariant) + " · 结束" : daily != null ? "挑战结束" : fixedPve != null && fixedPve.StrengthPreview && StrengthMatchOutcome.IsSharedFirst(state, playerId) ? "并列第一" : state.winner == playerId ? "竞技胜利" : "对局结束";
            Label(new Rect(170, 665, 740, 100), result, 58, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(170, 790, 740, 75), daily != null && !comparison ? "挑战得分 " + daily.Score : me == null ? "" : "长度 " + me.segments.Count + "     淘汰 " + me.kills, 34, new Color(.64f, .80f, .83f), TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(resultNote)) Label(new Rect(170, 850, 740, 85), resultNote, 22, new Color(.72f, .82f, .87f), TextAnchor.MiddleCenter);
            string strengthSummary = StrengthMatchSummary.Text(fixedPve);
            if (!string.IsNullOrEmpty(strengthSummary)) Label(new Rect(170, 910, 740, 40), strengthSummary, 24, new Color(1f, .88f, .56f), TextAnchor.MiddleCenter);
            if (Button(new Rect(230, 950, 620, 105), "再来一局", new Color(.10f, .60f, .49f)))
            {
                if (online) { Send(new Request { type = "restart" }); status = ""; }
                else RetryMatch();
            }
            if (Button(new Rect(230, 1080, 620, 95), "返回菜单", new Color(.17f, .25f, .34f))) BackToMenu();
            DrawRewardOffer(new Rect(245, 1190, 590, 70));
        }

        private void DrawControls()
        {
            string boostHelp = fixedPve != null && fixedPve.LocalFreeBoost && fixedPve.FreeBoostRemaining > 0
                ? "双指加速前8秒免耗" : "双指加速消耗长度";
            Label(new Rect(105, 1795, 870, 70), "全屏拖动转向  ·  " + boostHelp + "  ·  撞墙反弹", 24, new Color(.80f, .87f, .90f), TextAnchor.MiddleCenter);
        }

        // Gold weak points and red danger crosses can fall beneath the map
        // even when every head is outside it. Use their actual body-segment
        // projections; unmarked bodies do not keep the map faded indefinitely.
        internal static bool MinimapCoversStrengthMarkers(ArenaState world, ArenaRenderer renderer, Rect mapPixels, bool strengthEnabled)
        {
            if (!strengthEnabled || world == null || renderer == null || mapPixels.width <= 0 || mapPixels.height <= 0) return false;
            Vector2 padding = renderer.StrengthMarkerScreenPadding();
            if (padding.x <= 0f || padding.y <= 0f) return false;
            Rect expanded = Rect.MinMaxRect(mapPixels.xMin - padding.x, mapPixels.yMin - padding.y,
                mapPixels.xMax + padding.x, mapPixels.yMax + padding.y);
            foreach (var snake in world.snakes)
            {
                if (snake == null || !snake.alive || snake.segments == null) continue;
                for (int i = 1; i < snake.segments.Count; i++)
                {
                    bool weak = i < snake.strengthWeakSegments.Count && snake.strengthWeakSegments[i];
                    bool threat = i < snake.strengthThreatSegments.Count && snake.strengthThreatSegments[i];
                    if (!weak && !threat) continue;
                    Vector3 pixel = renderer.HeadToScreen(snake.segments[i]);
                    if (pixel.z > 0 && expanded.Contains(new Vector2(pixel.x, pixel.y))) return true;
                }
            }
            return false;
        }

        private void DrawMiniMap(ArenaState world)
        {
            Rect map = GameMiniMap;
            if (!ReferenceEquals(minimapWorld, world)) { minimapWorld = world; minimapFrame = -1; minimapOcclusion.Reset(); }
            if (minimapFrame != Time.frameCount)
            {
                minimapFrame = Time.frameCount;
                bool covered = false;
                foreach (var snake in world.snakes)
                {
                    if (!snake.alive || snake.segments == null || snake.segments.Count == 0) continue;
                    if (!arenaRenderer.TryCharacterScreenBounds(snake.id, out Rect pixel) &&
                        !arenaRenderer.TryFallbackHeadScreenBounds(snake.segments[0], out pixel)) continue;
                    Vector2 a = ToVirtual(new Vector2(pixel.xMin, pixel.yMin)), b = ToVirtual(new Vector2(pixel.xMax, pixel.yMax));
                    var character = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    if (map.Overlaps(character)) { covered = true; break; }
                }
                if (!covered && fixedPve != null && fixedPve.StrengthPreview)
                {
                    Rect pixels = GameUiScreenRect(map, Screen.safeArea, Screen.width, Screen.height);
                    covered = MinimapCoversStrengthMarkers(world, arenaRenderer, pixels, true);
                }
                minimapOcclusion.Advance(world.tick, covered, Time.unscaledDeltaTime, SimulationSuspended, reducedEffects);
            }
            float visibility = minimapOcclusion.Visibility;
            Panel(map, new Color(.025f, .06f, .10f, .85f * visibility));
            foreach (var snake in world.snakes)
            {
                if (!snake.alive || snake.segments.Count == 0) continue;
                Point p = snake.segments[0];
                float x = map.x + 15 + (p.x / world.halfSize + 1) * .5f * (map.width - 30);
                float y = map.y + 15 + (1 - (p.y / world.halfSize + 1) * .5f) * (map.height - 30);
                float size = snake.id == playerId ? 16 : 10;
                Color previous = GUI.color;
                bool target = world.eventStatus == "active" && world.eventTarget == snake.id;
                if (target)
                {
                    GUI.color = new Color(1f, .83f, .35f, visibility);
                    GUI.DrawTexture(new Rect(x - 10, y - 10, 20, 20), rounded);
                }
                GUI.color = snake.id == playerId ? Color.white : arenaRenderer.TrailColor(snake, playerId);
                GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, visibility);
                GUI.DrawTexture(new Rect(x - size / 2, y - size / 2, size, size), rounded);
                GUI.color = previous;
            }
            if (world.starVisible || world.strengthPickupVisible || world.strengthPickupPending)
            {
                Point p = world.strengthPickupVisible || world.strengthPickupPending ? world.strengthPickupPosition : world.starPosition;
                float x = map.x + 15 + (p.x / world.halfSize + 1) * .5f * (map.width - 30);
                float y = map.y + 15 + (1 - (p.y / world.halfSize + 1) * .5f) * (map.height - 30);
                if (!string.IsNullOrEmpty(world.starHolder)) { x += 10; y -= 10; }
                x = Mathf.Clamp(x, map.x + 8, map.xMax - 8); y = Mathf.Clamp(y, map.y + 8, map.yMax - 8);
                Color previous = GUI.color; GUI.color = new Color(1f, .84f, .32f, visibility);
                GUI.DrawTexture(new Rect(x - 7, y - 7, 14, 14), rounded); GUI.color = previous;
            }
        }

        private bool DrawEventTargetEdge(ArenaState world, out Rect box)
        {
            box = default;
            if (!EventTargetIndicator.TryTarget(world, playerId, out var target, out string name)) return false;
            var player = world.snakes.Find(s => s.id == playerId);
            if (player == null || !player.alive || player.segments == null || player.segments.Count == 0) return false;
            Vector3 screen = arenaRenderer.HeadToScreen(target);
            Vector3 playerScreen = arenaRenderer.HeadToScreen(player.segments[0]);
            if (screen.z <= 0 || playerScreen.z <= 0) return false;
            Vector2 projected = ToVirtual(screen);
            if (!EventTargetIndicator.TryEdge(new Point(projected.x, projected.y), 110, 410, 970, 1450, out var edge)) return false;
            Vector2 direction = projected - ToVirtual(playerScreen);
            if (direction.sqrMagnitude < .01f) return false;
            if (!EdgeIndicatorLayout.TryPlace(edge, edgeObstacles, out Point origin, EdgeIndicatorLayout.ObjectiveWidth)) return false;
            box = new Rect(origin.x, origin.y, EdgeIndicatorLayout.ObjectiveWidth, 54);
            Panel(box, new Color(.15f, .11f, .06f, .88f));
            DrawEdgeArrow(box, direction, new Color(1f, .88f, .55f));
            Label(new Rect(box.x + 48, box.y, box.width - 48, box.height), name, 22, new Color(1f, .88f, .55f), TextAnchor.MiddleCenter, true);
            return true;
        }

        private Point ProjectWeakBodyEdge(Point position)
        {
            Vector3 pixel = arenaRenderer.HeadToScreen(position);
            if (pixel.z <= 0 || weakProjectionScale <= 0) return new Point(float.NaN, float.NaN);
            return new Point((pixel.x - weakProjectionLeft) / weakProjectionScale,
                (weakProjectionHeight - pixel.y - weakProjectionTop) / weakProjectionScale);
        }

        private bool DrawWeakBodyEdge(ArenaState world)
        {
            if (world == null || fixedPve == null || !fixedPve.StrengthPreview || SimulationSuspended || !string.IsNullOrEmpty(status))
            {
                weakEdgeWorld = null; weakEdgeFrame = -1; weakEdgeId = ""; weakEdgeSegment = -1; weakEdgeAvailable = false;
                return false;
            }
            if (!ReferenceEquals(weakEdgeWorld, world))
            { weakEdgeWorld = world; weakEdgeFrame = -1; weakEdgeId = ""; weakEdgeSegment = -1; weakEdgeAvailable = false; }
            if (weakEdgeFrame != Time.frameCount)
            {
                weakEdgeFrame = Time.frameCount;
                float width = Screen.width, height = Screen.height;
                var viewport = WeakBodySafeViewport(Screen.safeArea, width, height,
                    out weakProjectionScale, out weakProjectionLeft, out weakProjectionTop);
                weakProjectionHeight = height;
                if (weakEdgeProjection == null) weakEdgeProjection = ProjectWeakBodyEdge;
                weakEdgeAvailable = WeakBodyEdgeIndicator.TrySelect(world, playerId, weakEdgeProjection, viewport, weakEdgeId, weakEdgeSegment, out weakEdgeTarget);
                weakEdgeId = weakEdgeAvailable ? weakEdgeTarget.id : "";
                weakEdgeSegment = weakEdgeAvailable ? weakEdgeTarget.segment : -1;
            }
            if (!weakEdgeAvailable) return false;
            SnakeState player = null;
            foreach (var snake in world.snakes) if (snake != null && snake.id == playerId) { player = snake; break; }
            if (player == null || !player.alive || player.segments == null || player.segments.Count == 0) return false;
            Point playerScreen = ProjectWeakBodyEdge(player.segments[0]);
            var direction = new Vector2(weakEdgeTarget.screen.x - playerScreen.x, weakEdgeTarget.screen.y - playerScreen.y);
            if (float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsInfinity(direction.x) || float.IsInfinity(direction.y) || direction.sqrMagnitude < .01f ||
                !EventTargetIndicator.TryEdge(weakEdgeTarget.screen, 110, 410, 970, 1450, out Point edge) ||
                !EdgeIndicatorLayout.TryPlace(edge, edgeObstacles, out Point origin)) return false;
            var box = new Rect(origin.x, origin.y, EdgeIndicatorLayout.DefaultWidth, 54);
            Panel(box, new Color(.15f, .11f, .045f, .92f));
            var color = new Color(1f, .88f, .55f);
            DrawEdgeArrow(box, direction, color);
            Label(new Rect(box.x + 48, box.y, box.width - 48, box.height), "弱点", 26, color, TextAnchor.MiddleCenter, true);
            return true;
        }

        private void DrawOpponentEdge(ArenaState world)
        {
            var player = world.snakes.Find(s => s.id == playerId);
            if (player == null || !player.alive || player.segments.Count == 0) { opponentEdgeId = ""; return; }
            Vector3 playerScreen = arenaRenderer.HeadToScreen(player.segments[0]);
            if (playerScreen.z <= 0) { opponentEdgeId = ""; return; }
            Vector2 playerPosition = ToVirtual(playerScreen);
            SnakeState selected = null, previous = null;
            Rect selectedBox = default, previousBox = default;
            Vector2 selectedDirection = default, previousDirection = default;
            float best = float.PositiveInfinity, previousDistance = float.PositiveInfinity;
            foreach (var other in world.snakes)
            {
                if (other == null || other.segments == null || other.segments.Count == 0) continue;
                Vector3 screen = arenaRenderer.HeadToScreen(other.segments[0]);
                if (screen.z <= 0) continue;
                Vector2 target = ToVirtual(screen);
                if (!OpponentEdgeIndicator.TryCandidate(world, playerId, other, new Point(target.x, target.y), out float distance, out Point edge)) continue;
                if (!EdgeIndicatorLayout.TryPlace(edge, edgeObstacles, out Point origin)) continue;
                Rect box = new Rect(origin.x, origin.y, 140, 54);
                if (other.id == opponentEdgeId) { previous = other; previousBox = box; previousDirection = target - playerPosition; previousDistance = distance; }
                if (distance < best) { selected = other; selectedBox = box; selectedDirection = target - playerPosition; best = distance; }
            }
            if (previous != null && OpponentEdgeIndicator.KeepPrevious(previousDistance, best))
            { selected = previous; selectedBox = previousBox; selectedDirection = previousDirection; }
            if (selected == null || selectedDirection.sqrMagnitude < .01f) { opponentEdgeId = ""; return; }
            opponentEdgeId = selected.id;
            Panel(selectedBox, new Color(.035f, .085f, .12f, .90f));
            Color color = Color.Lerp(arenaRenderer.TrailColor(selected, playerId), Color.white, .55f);
            DrawEdgeArrow(selectedBox, selectedDirection, color);
            Label(new Rect(selectedBox.x + 48, selectedBox.y, selectedBox.width - 48, selectedBox.height),
                selected.bot ? "AI" : "玩家", 22, Color.white, TextAnchor.MiddleCenter, true);
        }

        private void DrawEdgeArrow(Rect box, Vector2 heading, Color color)
        {
            Vector2 tip = new Vector2(box.x + 30, box.center.y);
            Vector2 direction = heading.normalized, side = new Vector2(-direction.y, direction.x);
            DrawEdgeLine(tip - direction * 18 + side * 11, tip, color);
            DrawEdgeLine(tip - direction * 18 - side * 11, tip, color);
        }

        private void DrawEdgeLine(Vector2 start, Vector2 end, Color color)
        {
            Vector2 delta = end - start;
            Color previousColor = GUI.color;
            GUI.color = color;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 3f));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(start, end, (float)i / steps);
                GUI.DrawTexture(new Rect(point.x - 3, point.y - 3, 6, 6), Texture2D.whiteTexture);
            }
            GUI.color = previousColor;
        }

        private void DrawNames(ArenaState world, bool strengthPromptVisible, bool hasContactNoticeArea, EdgeObstacle contactNoticeArea,
            bool showHeadlineNotice, Rect headlineArea)
        {
            nameplates.BeginFrame(world.tick); nameplateEntries.Clear();
            ReservePlayerNameplateBounds(nameplates, arenaRenderer, world, playerId, Screen.safeArea, Screen.width, Screen.height);
            if (hasContactNoticeArea) nameplates.Reserve(contactNoticeArea.x, contactNoticeArea.y, contactNoticeArea.width, contactNoticeArea.height);
            if (!paused && fixedPve != null && fixedPve.StrengthPreview && !string.IsNullOrEmpty(world.strengthHeadThreat))
                nameplates.Reserve(250, 1635, 580, 74);
            if (!paused && fixedPve != null && fixedPve.StrengthPreview)
            {
                var player = world.snakes.Find(s => s.id == playerId);
                if (player != null && player.strengthThreatSegments.Contains(true)) nameplates.Reserve(230, 1525, 620, 70);
            }
            if (!string.IsNullOrEmpty(StrengthBoostNotice.Text(fixedPve, world, playerId, pointerBoost, SimulationSuspended)))
                nameplates.Reserve(220, 1715, 640, 70);
            if (showHeadlineNotice) nameplates.Reserve(headlineArea.x, headlineArea.y, headlineArea.width, headlineArea.height);
            if (strengthPromptVisible) nameplates.Reserve(StrengthGuidancePanel.x, StrengthGuidancePanel.y, StrengthGuidancePanel.width, StrengthGuidancePanel.height);
            else if (firstRun && tutorialHints && !online && !paused && !(fixedPve != null && fixedPve.StrengthPreview) && CanControlPlayer())
            {
                var own = world.snakes.Find(s => s.id == playerId);
                string hint = FirstRunHints.Select(world.tick, hasSteered, tutorialClosedLoop, tutorialRebounded,
                    own == null ? 0 : own.kills, FirstRunHints.ApproachingOwnTrail(own), tutorialBoosted);
                if (!string.IsNullOrEmpty(hint)) nameplates.Reserve(100, 550, 880, 100);
            }
            foreach (var snake in world.snakes)
            {
                if (!snake.alive || snake.segments.Count == 0) continue;
                Vector3 screen = arenaRenderer.NameplateAnchor(snake.id, snake.segments[0]);
                if (screen.z <= 0) continue;
                Vector2 anchor = ToVirtual(screen);
                nameplates.AddAnchor(new Point(anchor.x, anchor.y));
            }
            // The player and a marked objective get the first available slots.
            for (int priority = 0; priority < 3; priority++)
                foreach (var snake in world.snakes)
                {
                    bool own = snake.id == playerId;
                    bool target = !own && (snake.boss || (world.eventStatus == "active" && world.eventTarget == snake.id));
                    if ((priority == 0 && !own) || (priority == 1 && !target) || (priority == 2 && (own || target))) continue;
                    CollectNameplate(world, snake);
                }
            // Leaders go behind every label so they never draw over the player's name.
            foreach (var entry in nameplateEntries) DrawNameplateLeader(entry);
            foreach (var entry in nameplateEntries) DrawNameplate(entry);
        }

        private bool StrengthIntroVisible() => firstRun && tutorialHints && fixedPve != null && fixedPve.StrengthPreview && state != null &&
            state.phase == "running" && state.tick < 260 && !paused &&
            string.IsNullOrEmpty(StrengthContactNotice.Text(fixedPve, state.tick, out _)) &&
            string.IsNullOrEmpty(CaptureNotice.Text(state, playerId, false));

        internal static string ProtectionNameplateSuffix(bool strength, bool own, bool escaping)
            => strength ? (own && escaping ? " 脱困" : " 保护") : " · 保护";

        private void CollectNameplate(ArenaState world, SnakeState snake)
        {
            if (!snake.alive || snake.segments.Count == 0) return;
            Vector3 projected = arenaRenderer.NameplateAnchor(snake.id, snake.segments[0]);
            if (projected.z <= 0) return;
            Vector2 position = ToVirtual(projected);
            if (!nameplates.TryPlace(snake.id, new Point(position.x, position.y), out var placed)) return;
            string label = snake.id == playerId ? "你" : snake.bot ? snake.name : "玩家 " + snake.name;
            if (fixedPve != null && fixedPve.StrengthPreview)
                label = snake.id == playerId ? "你" : snake.bot ? "AI" : "玩家";
            if (snake.boss) label = "首领 · 护盾 " + snake.bossShield;
            if (world.eventStatus == "active" && world.eventTarget == snake.id) label = "悬赏 · " + label;
            if (snake.shield > 0) label += ProtectionNameplateSuffix(fixedPve != null && fixedPve.StrengthPreview,
                snake.id == playerId, fixedPve != null && fixedPve.SqueezeEscapeTicks > 0);
            nameplateEntries.Add(new NameplateEntry { box = new Rect(placed.x, placed.y, NameplateLayout.Width, NameplateLayout.Height),
                anchor = position, color = arenaRenderer.TrailColor(snake, playerId), label = label });
        }

        private void DrawNameplateLeader(NameplateEntry entry)
        {
            Rect box = entry.box; Vector2 position = entry.anchor;
            if (Mathf.Abs(box.y - (position.y - 68)) <= 8f && Mathf.Abs(box.x - (position.x - NameplateLayout.Width / 2)) <= 8f) return;
            Vector2 start = new Vector2(Mathf.Clamp(position.x, box.x + 12, box.xMax - 12), position.y >= box.yMax ? box.yMax : box.y);
            Vector2 delta = position - start;
            if (delta.sqrMagnitude <= 1f) return;
            Matrix4x4 oldMatrix = GUI.matrix; Color oldColor = GUI.color;
            GUI.color = new Color(.75f, .90f, .86f, .55f);
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - 1, delta.magnitude, 2), Texture2D.whiteTexture);
            GUI.matrix = oldMatrix; GUI.color = oldColor;
        }

        private void DrawNameplate(NameplateEntry entry)
        {
            Rect box = entry.box;
            Panel(box, new Color(.03f, .07f, .12f, .88f));
            Color previous = GUI.color; GUI.color = entry.color;
            GUI.DrawTexture(new Rect(box.x + 8, box.y + 10, 8, box.height - 20), rounded); GUI.color = previous;
            Label(new Rect(box.x + 16, box.y, box.width - 20, box.height), entry.label, 23, Color.white, TextAnchor.MiddleCenter, true);
        }

        private static string FormatTime(float seconds)
        {
            int remaining = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00");
        }

        private Texture2D CreateRoundTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - 31.5f) - 20f);
                float dy = Mathf.Max(0, Mathf.Abs(y - 31.5f) - 20f);
                float alpha = Mathf.Clamp01(12f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            texture.Apply();
            return texture;
        }

        private void Panel(Rect rect, Color color)
        {
            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box);
                panelStyle.normal.background = rounded;
                panelStyle.border = new RectOffset(12, 12, 12, 12);
            }
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUI.color = previous;
        }

        private GUIStyle Style(int size, Color color, TextAnchor alignment, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.font = uiFont != null ? uiFont : GUI.skin.font;
            style.fontSize = size;
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.normal.textColor = color;
            style.alignment = alignment;
            style.wordWrap = true;
            return style;
        }

        private void Label(Rect rect, string text, int size, Color color, TextAnchor alignment, bool bold = false)
            => GUI.Label(rect, text, Style(size, color, alignment, bold));

        private bool Button(Rect rect, string text, Color color)
        {
            var style = Style(36, Color.white, TextAnchor.MiddleCenter, true);
            style.normal.background = rounded;
            style.hover.background = rounded;
            style.active.background = rounded;
            style.border = new RectOffset(12, 12, 12, 12);
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool pressed = GUI.Button(rect, text, style);
            GUI.backgroundColor = previous;
            return pressed;
        }

        private string TextField(Rect rect, string value, int maxLength)
        {
            var style = Style(30, Color.white, TextAnchor.MiddleLeft);
            style.normal.background = rounded;
            style.focused.background = rounded;
            style.border = new RectOffset(12, 12, 12, 12);
            style.focused.textColor = Color.white;
            style.padding = new RectOffset(25, 25, 10, 10);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(.12f, .20f, .28f);
            string result = GUI.TextField(rect, value ?? "", maxLength, style);
            GUI.backgroundColor = previous;
            return result;
        }
    }
}
