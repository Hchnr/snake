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
        private bool appFocused = true, appPaused;
        private bool pointerBoost;
        private bool reducedEffects;
        private readonly DragSteering dragSteering = new DragSteering();
        private int suppressPointerUpFrame = -1;
        private bool firstRun;
        private bool depthMode, depthWon;
        private int depthRoom;
        private int depthShield, depthRebound, depthGrowth;
        private int controlActions;
        private Point direction = new Point(1, 0);
        private float stepTimer;
        private float sendTimer;
        private int visualTick = -1, visualKills;
        private float killFlashUntil;
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
            arena = new Arena(24);
            for (int i = 0; i < 8; i++) arena.AddSnake("demo-" + i, "AI " + (i + 1), true);
            arena.Start();
            state = arena.State;
            playerId = "";
            stepTimer = 0;
        }

        private void BeginPve(ApiReply run)
        {
            dragSteering.Reset();
            connection.Close();
            online = false;
            inGame = true;
            paused = false;
            status = "";
            room = "";
            playerId = "you";
            firstRun = !CurrentProfile.tutorialComplete;
            arena = new Arena(unchecked((int)DateTime.UtcNow.Ticks), roundSeconds: depthMode ? 55f : firstRun ? 60f : 90f, nearbyOpponents: true, enableCaptures: true, rotatingEvents: !depthMode);
            arena.AddSnake(playerId, string.IsNullOrWhiteSpace(nameInput) ? "玩家" : nameInput.Trim(), false);
            for (int i = 0; i < 7; i++) arena.AddSnake("bot-" + i, "AI " + (i + 1), true);
            if (depthMode && depthRoom == 2) arena.ConfigureStar(5f, playerId);
            else if (depthMode) arena.DisableStar();
            if (depthMode && depthRoom == 3)
            {
                arena.SetStartingLength("bot-0", 20);
                arena.ConfigureBoss("bot-0");
                arena.State.eventKind = "bounty"; arena.State.eventStatus = "active"; arena.State.eventTarget = "bot-0";
            }
            arena.Start();
            state = arena.State;
            stepTimer = 0;
            direction = new Point(1, 0);
            controlActions = 0;
            daily = null;
            activeRun = run;
            activePlayerId = api.Profile != null ? api.Profile.playerId : "";
            lastRewardId = "";
            resultRecorded = false;
            int skin = run != null && run.trialSkin > 0 ? run.trialSkin : CurrentProfile.selectedSkin;
            var matchProfile = new PlayerProgress { selectedSkin = skin, talents = CurrentProfile.talents };
            var bonuses = Progression.Bonuses(matchProfile);
            if (depthMode)
            {
                bonuses.captureShieldSeconds = Mathf.Min(4f, depthShield * 2f);
                bonuses.reboundFreeSeconds = Mathf.Min(6f, depthRebound * 3f);
                bonuses.bonusGrowthPerKill = Mathf.Min(6, depthGrowth * 3);
            }
            arena.SetBonuses(playerId, bonuses);
            arenaRenderer.SelectedSkin = EffectiveSkin >= SkinCatalog.OwnedSkinCount ? EffectiveSkin : skin;
        }

        private void StartOnline(string action)
        {
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
            arena = null;
            state = null;
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
            dragSteering.Reset();
            connection.Close();
            inGame = false;
            online = false;
            onlineMenu = false;
            paused = false;
            depthMode = false;
            status = "";
            daily = null;
            activeRun = null;
            frontPage = "home";
            arenaRenderer.SelectedSkin = EffectiveSkin;
            Demo();
        }

        private void Update()
        {
            PumpPending();
            bool interaction = Input.anyKeyDown || Input.GetMouseButtonDown(0);
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) interaction = true;
            feedback.UpdateMusic(inGame && state != null && state.phase == "running", appFocused && !appPaused, paused, adShowing, interaction);
            bool homeVisible = !inGame && (frontPage == "home" || frontPage == "skins") && !developerRooms;
            bool caveVisible = !inGame && frontPage == "caves" && !developerRooms;
            bool resultVisible = inGame && !online && !depthMode && state != null && state.phase == "ended" && string.IsNullOrEmpty(status);
            if (homeScreen != null)
            {
                homeScreen.Visible = homeVisible || resultVisible;
                if (homeVisible || resultVisible)
                {
                    homeScreen.SetPage(frontPage == "skins", resultVisible);
                    bool trial = CurrentProfile.tutorialComplete && !CurrentProfile.dailyTrialUsed && GamePlatform.HasRewardedAds(api);
                    homeScreen.SetState(CurrentProfile, homeAvatar != null ? homeAvatar.Texture : null, frontNotice, busy, api.Connected,
                        feedback.Enabled, EffectiveSkin, previewSkin, ReferencePreviewEnabled, trial,
                        !string.IsNullOrEmpty(friendDate) && friendDate == api.Date, reducedEffects, feedback.HapticsEnabled, GamePlatform.SupportsHaptics, feedback.MusicEnabled);
                    if (resultVisible)
                    {
                        SnakeState own = state.snakes.Find(s => s.id == playerId);
                        string title = daily != null ? "挑战结束" : state.winner == playerId ? "围猎胜利" : "围猎结束";
                        string stats = daily != null ? "挑战得分 " + daily.Score + "   ·   淘汰 " + (own != null ? own.kills : 0) + "\n最长 " + daily.PeakLength :
                            own == null ? "" : "长度 " + own.segments.Count + "   ·   淘汰 " + own.kills;
                        bool share = daily != null && activeRun != null && api.Connected;
                        int adCoins = !string.IsNullOrEmpty(lastRewardId) && GamePlatform.HasRewardedAds(api) ? lastRewardCoins : 0;
                        homeScreen.SetResult(title, stats, resultNote, busy, share, adCoins);
                    }
                }
            }
            if (homeAvatar != null)
            {
                homeAvatar.Visible = homeVisible || resultVisible || caveVisible;
                homeAvatar.ReducedEffects = reducedEffects;
                if (homeAvatar.Visible) homeAvatar.Skin = homeVisible && frontPage == "skins" ? previewSkin :
                    resultVisible && activeRun != null && activeRun.trialSkin > 0 && EffectiveSkin < SkinCatalog.OwnedSkinCount ? activeRun.trialSkin : EffectiveSkin;
            }
            if (inGame && !paused && state != null && state.phase == "running") ReadControls();
            else { dragSteering.Reset(); pointerBoost = false; }
            if (daily != null && inGame && !paused)
            {
                stepTimer += Mathf.Min(Time.deltaTime, .2f);
                while (stepTimer >= .05f && !daily.Finished)
                {
                    byte heading = EncodeDirection(direction);
                    daily.Step(heading, pointerBoost);
                    dailyInputs.Add(heading); dailyInputs.Add(pointerBoost ? (byte)1 : (byte)0);
                    stepTimer -= .05f;
                }
                state = daily.State;
            }
            if (arena != null && !paused && !homeVisible)
            {
                stepTimer += Mathf.Min(Time.deltaTime, .2f);
                while (stepTimer >= .05f)
                {
                    if (inGame) arena.SetInput(playerId, direction, pointerBoost);
                    arena.Tick(.05f);
                    if (inGame && depthMode && arena.State.phase == "running" && DepthGoalReached(arena.State)) arena.EndRound();
                    stepTimer -= .05f;
                }
                if (!inGame && arena.State.phase == "ended") Demo();
                state = arena.State;
                if (inGame && depthMode && state.phase == "ended") depthWon = DepthGoalReached(state);
                if (inGame && !online && state.phase == "running")
                {
                    SnakeState ownSnake = state.snakes.Find(s => s.id == playerId);
                    if (ownSnake != null && !ownSnake.alive) arena.EndRound();
                }
            }
            if (inGame && !paused && state != null && state.phase == "running")
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
            if (inGame && !paused) feedback.Observe(state, playerId);
            if (inGame && state != null)
            {
                var own = state.snakes.Find(s => s.id == playerId);
                if (own != null)
                {
                    if (visualTick < 0 || state.tick < visualTick) { visualKills = own.kills; killFlashUntil = 0; }
                    else if (own.kills > visualKills) killFlashUntil = Time.unscaledTime + 1.1f;
                    visualKills = own.kills; visualTick = state.tick;
                }
            }
            arenaRenderer.Visible = !homeVisible;
            arenaRenderer.ReducedEffects = reducedEffects;
            if (arenaRenderer.Visible) arenaRenderer.Draw(state, playerId);
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
            if (x != 0 || y != 0) direction = new Point(x, y).Normalized;

            int activeTouches = 0;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    if (touch.phase == TouchPhase.Ended)
                    {
                        Vector2 last = ToVirtual(touch.position);
                        if (dragSteering.Move(touch.fingerId, new Point(last.x, last.y), out Point next)) { direction = next; acted = true; }
                    }
                    if (dragSteering.End(touch.fingerId)) suppressPointerUpFrame = Time.frameCount;
                }
                else activeTouches++;
            }
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) continue;
                Vector2 position = ToVirtual(touch.position);
                Point point = new Point(position.x, position.y);
                if (!dragSteering.Active) dragSteering.Begin(touch.fingerId, point);
                else if (dragSteering.Move(touch.fingerId, point, out Point next)) { direction = next; acted = true; }
            }
            // Holding a second finger adds an optional boost without reserving a screen area.
            if (activeTouches > 1) { pointerBoost = true; acted = true; }
            if (Input.touchCount == 0)
            {
                Vector2 position = ToVirtual(Input.mousePosition);
                Point point = new Point(position.x, position.y);
                if (Input.GetMouseButtonDown(0)) dragSteering.Begin(-1, point);
                if (Input.GetMouseButton(0) && dragSteering.Move(-1, point, out Point next)) { direction = next; acted = true; }
                if (Input.GetMouseButtonUp(0))
                {
                    if (dragSteering.Move(-1, point, out Point last)) { direction = last; acted = true; }
                    if (dragSteering.End(-1)) suppressPointerUpFrame = Time.frameCount;
                }
            }
            if (acted) controlActions++;
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
            if (isPaused) { feedback?.SuspendMusic(); SuspendInput(); }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            appFocused = hasFocus;
            if (!hasFocus) { feedback?.SuspendMusic(); SuspendInput(); }
        }

        private void SuspendInput()
        {
            dragSteering.Reset(); pointerBoost = false;
            if (inGame && !online) paused = true;
        }

        private void OnGUI()
        {
            if (rounded == null) return;
            if (inGame && Event.current.type == EventType.MouseUp && suppressPointerUpFrame == Time.frameCount)
                Event.current.Use();
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
            Label(new Rect(88, 70, 330, 58), online ? (mode == "pvp" ? "真人对战" : "混合对战") : depthMode ? "秘境深潜 " + depthRoom + "/3" : daily != null ? "每日挑战" : "单人挑战", 32, Color.white, TextAnchor.MiddleLeft, true);
            if (online && !string.IsNullOrEmpty(room)) Label(new Rect(88, 135, 410, 52), "房间  " + room, 28, new Color(.42f, .94f, .80f), TextAnchor.MiddleLeft);
            else Label(new Rect(88, 135, 410, 52), depthMode ? (activeRun == null ? "练习 · " : "") + DepthGoalText() : daily != null ? (activeRun == null ? "练习局 · 不计排行" : "统一规则 · 真人排行") : activeRun == null ? "练习局 · 不计账号奖励" : activeRun.trialSkin > 0 ? "皮肤试穿中 · 本局有效" : "击败对手，生存到最后", 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleLeft);
            Label(new Rect(530, 70, 185, 58), state == null ? "--:--" : FormatTime(state.remaining), 40, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(720, 70, 250, 58), me == null ? "--" : daily != null ? "分数 " + daily.Score : "长度 " + me.segments.Count, 30, Color.white, TextAnchor.MiddleRight, true);
            Label(new Rect(720, 135, 250, 52), me == null ? "" : "淘汰 " + me.kills + (depthMode ? "  星核 " : "  事件 ") + me.starPoints, 24, new Color(.60f, .72f, .79f), TextAnchor.MiddleRight);

            if (state != null) DrawMiniMap(state);
            if (state != null && state.eventStatus == "active")
            {
                string eventHint = state.eventKind == "bounty" ? "巨蛇悬赏 · 围猎标记对手" : state.eventKind == "star" ? "星核争夺 · 携带得事件点" : "果潮混战 · 圈内淘汰得事件点";
                var boss = state.snakes.Find(s => s.boss && s.alive);
                if (boss != null) eventHint = boss.bossWindup > 0 ? "首领蓄力！躲开橙色方向线 · 护盾 " + boss.bossShield : "首领护盾 " + boss.bossShield + " · 拦截或收圈";
                if (state.eventKind == "star" && state.starVisible)
                    eventHint = string.IsNullOrEmpty(state.starHolder) ? "星核出现 · 快去争夺" : state.starHolder == playerId ? "携带星核 · 坚持 " + Mathf.CeilToInt((1f - state.starProgress) * 8f) + " 秒" : "对手携带星核 · 去围猎";
                Panel(new Rect(275, 285, 490, 96), new Color(.15f, .12f, .19f, .87f));
                Label(new Rect(285, 292, 470, 82), eventHint + "  " + Mathf.CeilToInt(state.eventRemaining) + "秒", 23, new Color(1f, .87f, .55f), TextAnchor.MiddleCenter, true);
            }
            else if (state != null && state.starVisible)
            {
                string starHint = string.IsNullOrEmpty(state.starHolder) ? "星核出现" : state.starHolder == playerId ? "携带星核 " + Mathf.CeilToInt((1f - state.starProgress) * 10f) + " 秒" : "对手携带星核";
                Label(new Rect(275, 300, 490, 60), starHint, 25, new Color(1f, .85f, .48f), TextAnchor.MiddleCenter, true);
            }
            if (Time.unscaledTime < killFlashUntil && state != null && state.phase == "running")
            {
                float flash = 1f + .10f * Mathf.Sin((killFlashUntil - Time.unscaledTime) * 14f);
                Label(new Rect(250, 430, 580, 110), "围猎成功  ·  变长！", Mathf.RoundToInt(37 * flash), new Color(1f, .88f, .45f), TextAnchor.MiddleCenter, true);
            }
            if (state != null && state.phase == "running" && state.captureOwner == playerId && state.captureTick >= 0 && state.tick - state.captureTick < 12)
            {
                string captureText = state.captureCount > 0 ? "收圈捕获 " + state.captureCount + " 个对手！" : "轨迹闭合！";
                Label(new Rect(240, 520, 600, 90), captureText, 31, new Color(.85f, 1f, .70f), TextAnchor.MiddleCenter, true);
            }
            if (!paused && DangerProbe.Find(state, playerId, out _))
                Label(new Rect(270, 1640, 540, 64), "附近有危险 · 留意方向标记", 23, new Color(1f, .89f, .65f), TextAnchor.MiddleCenter, true);
            if (state != null && state.phase == "running") DrawNames(state);
            if (firstRun && !online && state != null && state.phase == "running" && state.tick < 600)
            {
                int seconds = state.tick / 20;
                string hint = seconds < 2 ? "" : seconds < 8 ? (controlActions > 0 ? "" : "在屏幕任意位置拖动，控制方向") :
                    seconds < 16 ? "绕住对手，再回到自己的轨迹" : seconds < 24 ? "撞墙不会失败，还会短暂加速" : "让对手撞上你的轨迹，淘汰后立即成长";
                if (!string.IsNullOrEmpty(hint))
                {
                    Panel(new Rect(120, 550, 840, 100), new Color(.025f, .08f, .14f, .90f));
                    Label(new Rect(145, 560, 790, 80), hint, 25, new Color(.75f, .97f, .88f), TextAnchor.MiddleCenter, true);
                }
            }
            if (Button(new Rect(55, 285, 215, 76), online ? "退出" : (paused ? "继续" : "暂停"), new Color(.10f, .17f, .25f, .92f)))
            {
                if (online) BackToMenu(); else paused = !paused;
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
            string result = daily != null ? "挑战结束" : state.winner == playerId ? "竞技胜利" : state.winner == "" ? "对局结束" : "对局结束";
            Label(new Rect(170, 665, 740, 100), result, 58, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(170, 790, 740, 75), daily != null ? "挑战得分 " + daily.Score : me == null ? "" : "长度 " + me.segments.Count + "     淘汰 " + me.kills, 34, new Color(.64f, .80f, .83f), TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(resultNote)) Label(new Rect(170, 850, 740, 85), resultNote, 22, new Color(.72f, .82f, .87f), TextAnchor.MiddleCenter);
            if (Button(new Rect(230, 950, 620, 105), "再来一局", new Color(.10f, .60f, .49f)))
            {
                if (online) { Send(new Request { type = "restart" }); status = ""; }
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
                GUI.color = snake.id == playerId ? Color.white : ArenaRenderer.Palette[Mathf.Abs(snake.color % ArenaRenderer.Palette.Length)];
                GUI.DrawTexture(new Rect(x - size / 2, y - size / 2, size, size), rounded);
                GUI.color = previous;
            }
            if (world.starVisible)
            {
                Point p = world.starPosition;
                float x = map.x + 15 + (p.x / world.halfSize + 1) * .5f * (map.width - 30);
                float y = map.y + 15 + (1 - (p.y / world.halfSize + 1) * .5f) * (map.height - 30);
                Color previous = GUI.color; GUI.color = new Color(1f, .84f, .32f);
                GUI.DrawTexture(new Rect(x - 8, y - 8, 16, 16), rounded); GUI.color = previous;
            }
        }

        private void DrawNames(ArenaState world)
        {
            foreach (var snake in world.snakes)
            {
                if (!snake.alive || snake.segments.Count == 0) continue;
                Vector3 projected = arenaRenderer.WorldToScreen(snake.segments[0]);
                if (projected.z <= 0) continue;
                Vector2 position = ToVirtual(projected);
                if (position.x < 90 || position.x > 990 || position.y < 260 || position.y > 1430) continue;
                string label = snake.id == playerId ? "你" : snake.bot ? snake.name : "玩家 " + snake.name;
                if (snake.boss) label = "首领 · 护盾 " + snake.bossShield;
                if (world.eventStatus == "active" && world.eventTarget == snake.id) label = "悬赏 · " + label;
                Rect box = new Rect(position.x - 105, position.y - 68, 210, 48);
                Panel(box, new Color(.03f, .07f, .12f, .78f));
                Label(box, label, 23, Color.white, TextAnchor.MiddleCenter, true);
            }
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
