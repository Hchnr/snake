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
        private readonly DragSteering dragSteering = new DragSteering();
        private readonly MatchHighlights matchHighlights = new MatchHighlights();
        private readonly NameplateLayout nameplates = new NameplateLayout();
        private string opponentEdgeId = "";
        private struct NameplateEntry { public Rect box; public Vector2 anchor; public Color color; public string label; }
        private readonly List<NameplateEntry> nameplateEntries = new List<NameplateEntry>(8);
        private readonly List<EdgeObstacle> edgeObstacles = new List<EdgeObstacle>(9);
        private readonly EventOutcomeNotice eventOutcome = new EventOutcomeNotice();
        private readonly EliminationFeedback eliminationFeedback = new EliminationFeedback();
        private readonly KillFeedback killFeedback = new KillFeedback();
        private readonly List<int> activeTouchIds = new List<int>();
        private int suppressPointerUpFrame = -1;
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
            arenaRenderer.SelectedSkin = EffectiveSkin >= SkinCatalog.OwnedSkinCount ? EffectiveSkin : skin;
        }

        private void BeginFixedPve() => BeginFixedPveWithRun(null);

        private void BeginFixedPveWithRun(ApiReply run)
        {
            nameplates.ClearHistory(); opponentEdgeId = "";
            dragSteering.Reset(); connection.Close();
            online = false; inGame = true; paused = false; status = ""; room = "";
            playerId = "you"; arena = null; daily = null; depthMode = false;
            firstRun = run != null ? run.startConfig.firstRun : !practiceProfile.tutorialComplete;
            var profile = run != null ? run.startConfig.MatchProfile() : practiceProfile;
            fixedPve = new PveFixedWorld(run != null ? run.seed : unchecked((int)DateTime.UtcNow.Ticks), firstRun, profile);
            state = fixedPve.RenderState();
            matchHighlights.Reset(); matchHighlights.Observe(state, playerId);
            eventOutcome.Reset(); eliminationFeedback.Reset(); eliminationFeedback.Observe(state, playerId);
            killFeedback.Reset(); killFeedback.Observe(state, playerId);
            stepTimer = 0; direction = new Point(1, 0); pointerBoost = false;
            controlActions = 0; hasSteered = false; tutorialClosedLoop = false; tutorialRebounded = false; tutorialBoosted = false;
            activeRun = run; activePlayerId = run != null && api.Profile != null ? api.Profile.playerId : "";
            activePracticeAccount = ""; lastRewardId = ""; resultRecorded = false; runInputs.Clear();
            verifiedDaily = false; verifiedShareToken = "";
            resultNote = "整数规则试玩 · 不计账号奖励或排行";
            arenaRenderer.SelectedSkin = run != null ? run.startConfig.skin : EffectiveSkin;
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
                killFeedback.Observe(state, playerId);
            }
            bool homeVisible = !inGame && (frontPage == "home" || frontPage == "skins") && !developerRooms;
            bool caveVisible = !inGame && frontPage == "caves" && !developerRooms;
            bool wardrobePreview = homeVisible && frontPage == "skins" && homeScreen != null && homeScreen.ArenaPreview;
            bool renderWardrobePreview = wardrobePreview && appFocused && !appPaused && platformVisible && !adShowing;
            bool resultVisible = inGame && !online && !depthMode && state != null && state.phase == "ended" && string.IsNullOrEmpty(status);
            bool resultWon = resultVisible && MatchOutcome.IsVictory(state, playerId);
            if (homeScreen != null)
            {
                homeScreen.Visible = homeVisible || resultVisible;
                if (homeVisible || resultVisible)
                {
                    homeScreen.SetPage(frontPage == "skins", resultVisible);
                    bool trial = CurrentProfile.tutorialComplete && !CurrentProfile.dailyTrialUsed && GamePlatform.HasRewardedAds(api);
                    homeScreen.SetState(CurrentProfile, homeAvatar != null ? homeAvatar.Texture : null, frontNotice, busy, api.Connected,
                        feedback.Enabled, EffectiveSkin, previewSkin, ReferencePreviewEnabled, trial,
                        !string.IsNullOrEmpty(friendDate) && friendDate == api.Date, reducedEffects, feedback.HapticsEnabled, GamePlatform.SupportsHaptics, feedback.MusicEnabled, feedback.MusicVolume, feedback.EffectsVolume, friendScore, friendRules, friendDate, tutorialHints, api.Config.FixedPvePreview || api.Config.FixedPveServerPreview, TutorialReady);
                    if (resultVisible)
                    {
                        SnakeState own = state.snakes.Find(s => s.id == playerId);
                        string title = comparisonVariant >= 0 && api.Config.ComparisonPreview ? "对照 " + (char)('A' + comparisonVariant) + " · 结束" : daily != null ? "挑战结束" : state.winner == playerId ? "围猎胜利" : "围猎结束";
                        string stats = comparisonVariant >= 0 && api.Config.ComparisonPreview ?
                            "最长 " + daily.PeakLength + "   ·   淘汰 " + (own != null ? own.kills : 0) :
                            daily != null ? "挑战得分 " + daily.Score + "   ·   淘汰 " + (own != null ? own.kills : 0) + "\n最长 " + daily.PeakLength :
                            own == null ? "" : "最长 " + matchHighlights.PeakLength + "   ·   淘汰 " + own.kills;
                        if (own != null && (state.eventStatus == "active" || state.eventStatus == "ended") &&
                            (state.eventKind == "bounty" || state.eventKind == "star" || state.eventKind == "fruit"))
                            stats += "   ·   事件点 " + own.starPoints;
                        bool share = daily != null && activeRun != null && api.Connected && verifiedDaily &&
                            activeRun.date == api.Date && DailyMatch.SupportsFriendChallenge(activeRun.rules) && !string.IsNullOrEmpty(verifiedShareToken);
                        int adCoins = !string.IsNullOrEmpty(lastRewardId) && GamePlatform.HasRewardedAds(api) ? lastRewardCoins : 0;
                        string highlight = matchHighlights.BestCapture > 0 ? "最佳围猎 · 一次圈住 " + matchHighlights.BestCapture + " 个对手" : "";
                        string outcome = MatchOutcome.Explain(state, playerId);
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
                    resultVisible && activeRun != null && activeRun.trialSkin > 0 && EffectiveSkin < SkinCatalog.OwnedSkinCount ? activeRun.trialSkin : EffectiveSkin;
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
            if (inGame && !SimulationSuspended) feedback.Observe(state, playerId);
            arenaRenderer.SetPreviewMode(renderWardrobePreview);
            if (wardrobePreview) arenaRenderer.SelectedSkin = previewSkin;
            arenaRenderer.Visible = appFocused && !appPaused && platformVisible && !adShowing &&
                (!homeVisible || (renderWardrobePreview && arenaRenderer.PreviewTexture != null));
            if (homeScreen != null) homeScreen.SetArenaPreview(renderWardrobePreview ? arenaRenderer.PreviewTexture : null);
            arenaRenderer.ReducedEffects = reducedEffects;
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
                fixedPve.Step(heading, pointerBoost);
                runInputs.Add(heading); runInputs.Add(pointerBoost ? (byte)1 : (byte)0);
                state = fixedPve.RenderState();
                matchHighlights.Observe(state, playerId); ObserveTutorial(state); eventOutcome.Observe(state, playerId); eliminationFeedback.Observe(state, playerId); killFeedback.Observe(state, playerId);
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
                    if (dragSteering.End(touch.fingerId)) suppressPointerUpFrame = Time.frameCount;
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
                    if (dragSteering.End(-1)) suppressPointerUpFrame = Time.frameCount;
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
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            scale = Mathf.Min(safe.width / 1080f, safe.height / 1920f);
            left = safe.xMin + (safe.width - 1080 * scale) * .5f;
            top = Screen.height - safe.yMax + (safe.height - 1920 * scale) * .5f;
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
            if (inGame && Event.current.type == EventType.MouseUp && suppressPointerUpFrame == Time.frameCount)
            {
                // A drag may have started over an IMGUI button. Since its release
                // is consumed, explicitly clear the button's captured pointer.
                GUIUtility.hotControl = 0;
                Event.current.Use();
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

        private void DrawGame()
        {
            Panel(new Rect(48, 50, 984, 188), new Color(.035f, .065f, .12f, .90f));
            SnakeState me = state == null ? null : state.snakes.Find(s => s.id == playerId);
            Label(new Rect(88, 70, 330, 58), online ? (mode == "pvp" ? "真人对战" : "混合对战") : fixedPve != null ? "整数规则试玩" : comparisonVariant >= 0 && api.Config.ComparisonPreview ? "对照方案 " + (char)('A' + comparisonVariant) : depthMode ? "秘境深潜 " + depthRoom + "/3" : daily != null ? "每日挑战" : "单人挑战", 32, Color.white, TextAnchor.MiddleLeft, true);
            if (online && !string.IsNullOrEmpty(room)) Label(new Rect(88, 135, 410, 52), "房间  " + room, 28, new Color(.42f, .94f, .80f), TextAnchor.MiddleLeft);
            else Label(new Rect(88, 135, 410, 52), comparisonVariant >= 0 && api.Config.ComparisonPreview ? "试玩 · 不计奖励或排行" : depthMode ? "练习 · " + DepthGoalText() : daily != null ? (activeRun == null ? "练习局 · 不计排行" : "统一规则 · 真人排行") : activeRun == null ? "练习局 · 不计账号奖励" : activeRun.trialSkin > 0 ? "皮肤试穿中 · 本局有效" : "击败对手，生存到最后", 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleLeft);
            bool urgent = RoundTimerPresentation.IsUrgent(state);
            int timerSize = urgent ? (reducedEffects ? 44 : Mathf.RoundToInt(42 + 2 * Mathf.Sin(state.remaining * Mathf.PI * 2))) : 40;
            Color timerColor = urgent ? new Color(1f, .76f, .40f) : Color.white;
            Label(new Rect(530, 70, 185, 58), state == null ? "--:--" : FormatTime(state.remaining), timerSize, timerColor, TextAnchor.MiddleCenter, true);
            Label(new Rect(720, 70, 250, 58), me == null ? "--" : daily != null && !(comparisonVariant >= 0 && api.Config.ComparisonPreview) ? "分数 " + daily.Score : "长度 " + me.segments.Count, 30, Color.white, TextAnchor.MiddleRight, true);
            Label(new Rect(720, 135, 250, 52), me == null ? "" : "淘汰 " + me.kills + (depthMode ? "  星核 " : "  事件 ") + me.starPoints, 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleRight);

            if (arenaRenderer.VisualFallback)
                Label(new Rect(300, 235, 500, 45), "造型素材不可用 · 备用显示", 19, new Color(.9f, .92f, .84f), TextAnchor.MiddleCenter);
            if (state != null) DrawMiniMap(state);
            string depthHint = depthMode ? DepthChallenge.ObjectiveHint(depthRoom, state, playerId) : "";
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
            string killMessage = killFeedback.Current(state);
            if (!string.IsNullOrEmpty(killMessage))
            {
                float flash = reducedEffects ? 1f : 1f + .10f * Mathf.Sin(state.tick * .7f);
                Label(new Rect(250, 430, 580, 110), killMessage, Mathf.RoundToInt(37 * flash), new Color(1f, .88f, .45f), TextAnchor.MiddleCenter, true);
            }
            bool captureVisible = false;
            if (state != null && state.captureTick >= 0 && state.tick >= state.captureTick && state.tick - state.captureTick < 12 &&
                state.capturePolygon != null && state.capturePolygon.Count > 0)
            {
                Vector3 point = arenaRenderer.HeadToScreen(state.capturePolygon[0]);
                Vector2 projected = ToVirtual(point);
                captureVisible = point.z > 0 && projected.x >= 0 && projected.x <= 1080 && projected.y >= 0 && projected.y <= 1920;
            }
            string captureText = CaptureNotice.Text(state, playerId, captureVisible);
            if (!string.IsNullOrEmpty(captureText))
                Label(new Rect(240, 520, 600, 90), captureText, 31, new Color(.85f, 1f, .70f), TextAnchor.MiddleCenter, true);
            string elimination = eliminationFeedback.Current(state);
            if (!string.IsNullOrEmpty(elimination))
            {
                Panel(new Rect(300, 1080, 480, 72), new Color(.035f, .09f, .13f, .86f));
                Label(new Rect(315, 1085, 450, 62), elimination, 24, new Color(.94f, .96f, .78f), TextAnchor.MiddleCenter, true);
            }
            if (!paused && DangerProbe.Find(state, playerId, out _))
                Label(new Rect(270, 1640, 540, 64), "附近有危险 · 留意方向标记", 23, new Color(1f, .89f, .65f), TextAnchor.MiddleCenter, true);
            else if (!paused && DangerProbe.FindBeforeProtectionEnds(state, playerId, out _))
                Label(new Rect(230, 1640, 620, 64), "附近有对手 · 保护即将结束", 23, new Color(1f, .89f, .65f), TextAnchor.MiddleCenter, true);
            string boostHint = depthMode && !paused ? DepthChallenge.ReboundBoostHint(me) : "";
            if (!string.IsNullOrEmpty(boostHint))
            {
                Panel(new Rect(220, 1510, 640, 70), new Color(.035f, .12f, .17f, .90f));
                Label(new Rect(235, 1515, 610, 60), boostHint, 24, new Color(.75f, 1f, .90f), TextAnchor.MiddleCenter, true);
            }
            if (state != null && state.phase == "running")
            {
                DrawNames(state);
                edgeObstacles.Clear();
                foreach (var entry in nameplateEntries)
                    edgeObstacles.Add(new EdgeObstacle(entry.box.x, entry.box.y, entry.box.width, entry.box.height));
                bool hasObjectiveEdge = DrawEventTargetEdge(state, out Rect objectiveEdge);
                if (hasObjectiveEdge) edgeObstacles.Add(new EdgeObstacle(objectiveEdge.x, objectiveEdge.y, objectiveEdge.width, objectiveEdge.height));
                DrawOpponentEdge(state);
            }
            if (firstRun && tutorialHints && !online && !paused && CanControlPlayer())
            {
                string hint = FirstRunHints.Select(state.tick, hasSteered, tutorialClosedLoop, tutorialRebounded, me == null ? 0 : me.kills, FirstRunHints.ApproachingOwnTrail(me), tutorialBoosted);
                if (!string.IsNullOrEmpty(hint))
                {
                    Panel(new Rect(100, 550, 880, 100), new Color(.025f, .08f, .14f, .90f));
                    Label(new Rect(125, 560, 660, 80), hint, 25, new Color(.75f, .97f, .88f), TextAnchor.MiddleCenter, true);
                    if (SmallButton(new Rect(800, 570, 155, 60), "关闭提示", new Color(.16f, .26f, .31f), 20)) SetTutorialHints(false);
                }
            }
            if (Button(new Rect(55, 285, 215, 76), online ? "退出" : (paused ? "继续" : "暂停"), new Color(.10f, .17f, .25f, .92f)))
            {
                if (online) BackToMenu();
                else { paused = !paused; if (paused) feedback.SuspendAudio(); }
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
                Panel(new Rect(160, 660, 760, 300), new Color(.035f, .065f, .12f, .94f));
                Label(new Rect(200, 700, 680, 85), "游戏已暂停", 46, Color.white, TextAnchor.MiddleCenter, true);
                if (Button(new Rect(265, 825, 550, 90), "继续对战", new Color(.10f, .60f, .49f))) paused = false;
                if (Button(new Rect(345, 980, 390, 75), "返回菜单", new Color(.15f, .22f, .31f))) BackToMenu();
            }
            else DrawControls();
        }

        private void DrawResult(SnakeState me)
        {
            if (depthMode) { DrawDepthResult(me); return; }
            Panel(new Rect(125, 610, 830, 690), new Color(.035f, .065f, .12f, .96f));
            bool comparison = comparisonVariant >= 0 && api.Config.ComparisonPreview;
            string result = comparison ? "对照 " + (char)('A' + comparisonVariant) + " · 结束" : daily != null ? "挑战结束" : state.winner == playerId ? "竞技胜利" : state.winner == "" ? "对局结束" : "对局结束";
            Label(new Rect(170, 665, 740, 100), result, 58, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(170, 790, 740, 75), daily != null && !comparison ? "挑战得分 " + daily.Score : me == null ? "" : "长度 " + me.segments.Count + "     淘汰 " + me.kills, 34, new Color(.64f, .80f, .83f), TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(resultNote)) Label(new Rect(170, 850, 740, 85), resultNote, 22, new Color(.72f, .82f, .87f), TextAnchor.MiddleCenter);
            if (Button(new Rect(230, 950, 620, 105), "再来一局", new Color(.10f, .60f, .49f)))
            {
                if (online) { Send(new Request { type = "restart" }); status = ""; }
                else if (comparison) BeginComparison(comparisonVariant);
                else if (daily != null) RequestDaily(); else RequestPve();
            }
            if (Button(new Rect(230, 1080, 620, 95), "返回菜单", new Color(.17f, .25f, .34f))) BackToMenu();
            DrawRewardOffer(new Rect(245, 1190, 590, 70));
        }

        private void DrawControls()
        {
            Label(new Rect(105, 1795, 870, 70), "全屏拖动转向  ·  双指加速消耗长度  ·  撞墙反弹", 24, new Color(.80f, .87f, .90f), TextAnchor.MiddleCenter);
        }

        private void DrawMiniMap(ArenaState world)
        {
            Rect map = new Rect(790, 285, 240, 240);
            Panel(map, new Color(.025f, .06f, .10f, .85f));
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
                    GUI.color = new Color(1f, .83f, .35f);
                    GUI.DrawTexture(new Rect(x - 10, y - 10, 20, 20), rounded);
                }
                GUI.color = snake.id == playerId ? Color.white : arenaRenderer.TrailColor(snake, playerId);
                GUI.DrawTexture(new Rect(x - size / 2, y - size / 2, size, size), rounded);
                GUI.color = previous;
            }
            if (world.starVisible)
            {
                Point p = world.starPosition;
                float x = map.x + 15 + (p.x / world.halfSize + 1) * .5f * (map.width - 30);
                float y = map.y + 15 + (1 - (p.y / world.halfSize + 1) * .5f) * (map.height - 30);
                if (!string.IsNullOrEmpty(world.starHolder)) { x += 10; y -= 10; }
                x = Mathf.Clamp(x, map.x + 8, map.xMax - 8); y = Mathf.Clamp(y, map.y + 8, map.yMax - 8);
                Color previous = GUI.color; GUI.color = new Color(1f, .84f, .32f);
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
            if (!EdgeIndicatorLayout.TryPlace(edge, edgeObstacles, out Point origin)) return false;
            box = new Rect(origin.x, origin.y, 140, 54);
            Panel(box, new Color(.15f, .11f, .06f, .88f));
            DrawEdgeArrow(box, direction, new Color(1f, .88f, .55f));
            Label(new Rect(box.x + 48, box.y, box.width - 48, box.height), name, 22, new Color(1f, .88f, .55f), TextAnchor.MiddleCenter, true);
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

        private void DrawNames(ArenaState world)
        {
            nameplates.BeginFrame(world.tick); nameplateEntries.Clear();
            if (firstRun && tutorialHints && !online && !paused && CanControlPlayer())
            {
                var own = world.snakes.Find(s => s.id == playerId);
                string hint = FirstRunHints.Select(world.tick, hasSteered, tutorialClosedLoop, tutorialRebounded,
                    own == null ? 0 : own.kills, FirstRunHints.ApproachingOwnTrail(own), tutorialBoosted);
                if (!string.IsNullOrEmpty(hint)) nameplates.Reserve(100, 550, 880, 100);
            }
            foreach (var snake in world.snakes)
            {
                if (!snake.alive || snake.segments.Count == 0) continue;
                Vector3 screen = arenaRenderer.WorldToScreen(snake.segments[0]);
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

        private void CollectNameplate(ArenaState world, SnakeState snake)
        {
            if (!snake.alive || snake.segments.Count == 0) return;
            Vector3 projected = arenaRenderer.WorldToScreen(snake.segments[0]);
            if (projected.z <= 0) return;
            Vector2 position = ToVirtual(projected);
            if (!nameplates.TryPlace(snake.id, new Point(position.x, position.y), out var placed)) return;
            string label = snake.id == playerId ? "你" : snake.bot ? snake.name : "玩家 " + snake.name;
            if (snake.boss) label = "首领 · 护盾 " + snake.bossShield;
            if (world.eventStatus == "active" && world.eventTarget == snake.id) label = "悬赏 · " + label;
            if (snake.shield > 0) label += " · 保护";
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
