using System;
using System.Collections.Generic;
using SnakeClub.Core;
using UnityEngine;

namespace SnakeClub
{
    public sealed partial class SnakeGameApp
    {
        [Serializable] private sealed class PendingChallenge
        {
            public string playerId, runId, token, inputs, mode, rules;
            public int score, seconds, actions;
        }
        [Serializable] private sealed class PendingChallenges { public List<PendingChallenge> entries = new List<PendingChallenge>(); }

        private GameApiClient api;
        private readonly PlayerProgress practiceProfile = new PlayerProgress();
        private DailyMatch daily;
        private readonly List<byte> runInputs = new List<byte>();
        private ApiReply activeRun;
        private Texture2D caveBackground;
        private HomeAvatar homeAvatar;
        private HomeScreen homeScreen;
        private string frontPage = "home", frontNotice = "", resultNote = "", lastRewardId = "", lastRewardKind = "", activePlayerId = "", friendDate = "";
        private string activePracticeAccount = "", practicedAccount = "";
        private bool busy, resultRecorded, flushing, adShowing, connecting, verifiedDaily;
        private int comparisonVariant = -1;
        private int friendScore, activeFriendScore = -1;
        private string friendRules = "", activeFriendDate = "", activeFriendRules = "", verifiedShareToken = "";
        private int verifiedShareScore;
        private bool developerRooms = false;
        private string caveStory = "";
        private float caveStoryUntil;
        private int previewSkin, lastRewardCoins, localVisualSkin = -1;
        private ApiReply board;
        private int boardGeneration;
        private const string PendingKey = "snake.v3.pending";
        private readonly PendingRetrySchedule pendingRetry = new PendingRetrySchedule();
        private readonly CaveSyncState caveSync = new CaveSyncState();
        private readonly PendingSettlementSchedule settlementRetry = new PendingSettlementSchedule();
        private readonly IncomingFriendChallenge incomingFriend = new IncomingFriendChallenge();
        private int observedChallengeRevision;
        private float nextPendingPoll;
        private PlayerProgress CurrentProfile => api != null && api.Connected ? api.Profile : practiceProfile;
        private bool TutorialReady => CurrentProfile.tutorialComplete || (api != null && api.Connected &&
            !string.IsNullOrEmpty(practicedAccount) && api.Profile.playerId == practicedAccount);
        private bool ReferencePreviewEnabled => api != null && api.Config.allowReferenceSkinPreview && SkinVisual.ReferenceAvailable;
        private int EffectiveSkin => ReferencePreviewEnabled && localVisualSkin >= SkinCatalog.OwnedSkinCount
            && localVisualSkin < SkinCatalog.All.Length ? localVisualSkin : CurrentProfile.selectedSkin;

        private void SetupHomeScreen()
        {
            homeScreen = GetComponent<HomeScreen>();
            if (homeScreen == null) homeScreen = gameObject.AddComponent<HomeScreen>();
            homeScreen.Setup(uiFont, StartMain, () => { frontPage = "skins"; previewSkin = EffectiveSkin; },
                OpenCaves, () => frontPage = "more", () => feedback.Enabled = !feedback.Enabled,
                () => { frontPage = "home"; arenaRenderer.SelectedSkin = EffectiveSkin; },
                index => previewSkin = index, EquipPreviewSkin, TrialPreviewSkin);
            homeScreen.SetupReconnect(ConnectProfile);
            homeScreen.SetupPreferences(
                () => { reducedEffects = !reducedEffects; PlayerPrefs.SetInt("snake.v031.reducedEffects", reducedEffects ? 1 : 0); PlayerPrefs.Save(); },
                () => feedback.HapticsEnabled = !feedback.HapticsEnabled,
                () => feedback.MusicEnabled = !feedback.MusicEnabled,
                () => feedback.MusicVolume = AudioLevels.Next(feedback.MusicVolume),
                () => feedback.EffectsVolume = AudioLevels.Next(feedback.EffectsVolume),
                () => SetTutorialHints(!tutorialHints));
            homeScreen.SetupResults(
                () => { if (comparisonVariant >= 0 && api.Config.ComparisonPreview) BeginComparison(comparisonVariant); else if (daily != null) RequestDaily(); else RequestPve(); }, BackToMenu,
                ShareCurrentChallenge,
                () =>
                {
                    if (string.IsNullOrEmpty(lastRewardId) || lastRewardCoins <= 0) return;
                    StartRewarded(lastRewardKind, lastRewardId, 0, reply =>
                    {
                        if (reply.ok) { lastRewardId = ""; resultNote = "额外金币 +" + reply.coins; }
                        else if (!string.IsNullOrEmpty(reply.error)) resultNote = reply.error;
                    });
                });
        }

        private void EquipPreviewSkin(int index)
        {
            if (index < 0 || index >= SkinCatalog.All.Length || index == EffectiveSkin) return;
            PlayerProgress p = CurrentProfile;
            if (SkinCatalog.Get(index).ReferencePreview)
            {
                if (!ReferencePreviewEnabled || SkinVisual.Texture(index) == null) return;
                localVisualSkin = index; PlayerPrefs.SetInt("snake.v031.previewSkin", index); PlayerPrefs.Save();
                previewSkin = index; frontNotice = "本机造型试用中";
            }
            else if (!api.Connected && !p.ownedSkins.Contains(index)) frontNotice = "联网后可解锁并保存皮肤";
            else if (!api.Connected)
            {
                localVisualSkin = -1; practiceProfile.selectedSkin = index; PlayerPrefs.DeleteKey("snake.v031.previewSkin"); previewSkin = index;
            }
            else Mutate(p.ownedSkins.Contains(index) ? "skin/equip" : "skin/buy", new ApiRequest { index = index }, reply =>
            {
                if (reply.ok) { localVisualSkin = -1; PlayerPrefs.DeleteKey("snake.v031.previewSkin"); previewSkin = index; }
            });
        }

        private void TrialPreviewSkin()
        {
            if (previewSkin < 1 || previewSkin >= SkinCatalog.OwnedSkinCount) return;
            int selected = previewSkin;
            StartRewarded("trial", "", selected, reply => { if (reply.ok) frontNotice = "试穿已领取，将在下场普通对局使用"; });
        }

        private void StartMain()
        {
            if (api.Config.ComparisonPreview) { frontPage = "comparison"; return; }
            if (api.Config.FixedPvePreview || api.Config.FixedPveServerPreview) { RequestPve(); return; }
            if (!TutorialReady) RequestPve();
            else RequestDaily();
        }

        private void LoadPreferences()
        {
            reducedEffects = PlayerPrefs.GetInt("snake.v031.reducedEffects", 0) != 0;
            tutorialHints = PlayerPrefs.GetInt("snake.v031.tutorialHints", 1) != 0;
        }

        private void SetTutorialHints(bool enabled)
        {
            tutorialHints = enabled;
            PlayerPrefs.SetInt("snake.v031.tutorialHints", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void ConnectProfile()
        {
            if (connecting) return;
            connecting = true;
            caveBackground = Resources.Load<Texture2D>("HomeGarden");
            if (caveBackground == null) caveBackground = Resources.Load<Texture2D>("MysticCave");
            if (caveBackground == null) Debug.LogWarning("[Snake Club] Home background could not be loaded.");
            if (homeAvatar == null) homeAvatar = gameObject.AddComponent<HomeAvatar>();
            frontNotice = "正在连接进度服务…";
            api.Login(reply =>
            {
                connecting = false;
                frontNotice = reply.ok ? "进度已同步" : "离线练习 · 进度和排行暂不同步";
                localVisualSkin = ReferencePreviewEnabled ? PlayerPrefs.GetInt("snake.v031.previewSkin", SkinCatalog.OwnedSkinCount) : -1;
                previewSkin = EffectiveSkin;
                arenaRenderer.SelectedSkin = previewSkin;
                if (reply.ok)
                {
                    pendingRetry.Reset();
                    FlushPending();
                }
                else pendingRetry.Fail(Time.unscaledTime);
            });
        }

        private static byte EncodeDirection(Point point)
        {
            double angle = Math.Atan2(point.y, point.x);
            return (byte)(((int)Math.Round(angle * 256 / (Math.PI * 2)) + 256) & 255);
        }

        private static int PracticeSeed()
        {
            string date = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
            unchecked { uint hash = 2166136261; foreach (char c in date) hash = (hash ^ c) * 16777619; return (int)hash; }
        }

        private void RequestPve()
        {
            if (busy) return;
            depthMode = false;
            if (api.Config.FixedPvePreview) { BeginFixedPve(); return; }
            if (api.Config.FixedPveServerPreview)
            {
                if (!api.Connected) { BeginFixedPve(); resultNote = "本机验证服务未连接 · 离线试玩，不计奖励"; return; }
                busy = true;
                api.Post("internal/fixed-pve/start", new ApiRequest(), reply =>
                {
                    busy = false;
                    if (reply.ok && reply.mode == "fixed-eval" && reply.rules == PveFixedRecording.ExperimentalRules &&
                        !string.IsNullOrEmpty(reply.runId) && !string.IsNullOrEmpty(reply.token) && reply.startConfig != null && reply.startConfig.Supported)
                        BeginFixedPveWithRun(reply);
                    else { BeginFixedPve(); resultNote = "本机实验开局不可用 · 离线试玩，不计奖励"; }
                });
                return;
            }
            if (!api.Connected) { BeginPve(null); resultNote = "离线练习不计账号奖励"; return; }
            busy = true;
            api.Post("match/start", new ApiRequest(), reply =>
            {
                busy = false;
                BeginPve(reply.ok ? reply : null);
                resultNote = reply.ok ? "" : "离线练习不计账号奖励";
            });
        }

        private void RequestDaily()
        {
            if (busy) return;
            depthMode = false;
            activeFriendScore = !string.IsNullOrEmpty(friendDate) && friendDate == api.Date && DailyMatch.SupportsFriendChallenge(friendRules) ? friendScore : -1;
            activeFriendDate = friendDate; activeFriendRules = friendRules;
            string requestedRules = activeFriendScore >= 0 ? activeFriendRules : DailyMatch.RulesVersion;
            friendDate = ""; friendRules = "";
            if (!api.Connected) { BeginDaily(null); return; }
            busy = true;
            api.Post("challenge/start?rules=" + requestedRules, new ApiRequest(), reply => { busy = false; BeginDaily(reply.ok ? reply : null); });
        }

        private void StartDepth()
        {
            if (busy) return;
            int completed = DepthChallenge.CompletedMatches(CurrentProfile);
            if (!DepthChallenge.IsUnlocked(CurrentProfile)) { frontNotice = "完成 3 场对局后开启深潜（" + completed + "/3）"; return; }
            depthMode = true; depthRoom = 1; depthShield = depthRebound = depthGrowth = 0;
            RequestDepthRoom();
        }

        private void RequestDepthRoom()
        {
            if (busy) return;
            depthWon = false;
            // Depth uses different rules and cannot be settled as an ordinary PvE match.
            BeginPve(null);
            resultNote = "深潜练习 · 不计账号奖励或排行";
        }

        private string DepthGoalText() => depthRoom == 1 ? "目标：淘汰 1 条对手" : depthRoom == 2 ? "目标：携带星核得分" : "目标：击破护盾并围猎首领";

        private bool DepthGoalReached(ArenaState world)
        {
            return DepthChallenge.GoalReached(depthRoom, world, playerId);
        }

        private void SelectDepthBonus(int choice)
        {
            if (busy) return;
            if (choice == 0) depthShield++;
            else if (choice == 1) depthRebound++;
            else depthGrowth++;
            depthRoom++;
            RequestDepthRoom();
        }

        private void DrawDepthResult(SnakeState me)
        {
            Panel(new Rect(105, 490, 870, 970), new Color(.035f, .065f, .12f, .96f));
            string title = depthWon ? depthRoom == 3 ? "深潜完成！" : "房间完成！" : "本次深潜结束";
            Label(new Rect(140, 530, 800, 90), title, 49, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(150, 640, 780, 70), me == null ? "" : "淘汰 " + me.kills + "  ·  长度 " + me.segments.Count, 29, new Color(.75f, .92f, .88f), TextAnchor.MiddleCenter);
            Label(new Rect(150, 710, 780, 70), resultNote, 22, new Color(.75f, .88f, .91f), TextAnchor.MiddleCenter);
            Label(new Rect(140, 765, 800, 75), DepthChallenge.BonusSummary(depthShield, depthRebound, depthGrowth), 20, new Color(.68f, .84f, .86f), TextAnchor.MiddleCenter);
            if (depthWon && depthRoom < 3)
            {
                Label(new Rect(150, 835, 780, 55), busy ? "正在准备下一间…" : "选择一个本次深潜增益", 29, new Color(1f, .86f, .56f), TextAnchor.MiddleCenter, true);
                if (Button(new Rect(175, 900, 730, 100), DepthChallenge.BonusChoice(0, depthShield, depthRebound, depthGrowth), new Color(.13f, .51f, .46f))) SelectDepthBonus(0);
                if (Button(new Rect(175, 1020, 730, 100), DepthChallenge.BonusChoice(1, depthShield, depthRebound, depthGrowth), new Color(.23f, .40f, .62f))) SelectDepthBonus(1);
                if (Button(new Rect(175, 1140, 730, 100), DepthChallenge.BonusChoice(2, depthShield, depthRebound, depthGrowth), new Color(.42f, .33f, .60f))) SelectDepthBonus(2);
                if (Button(new Rect(250, 1280, 580, 85), "退出深潜，返回大厅", new Color(.15f, .22f, .31f)) && !busy) BackToMenu();
            }
            else
            {
                Label(new Rect(150, 850, 780, 80), depthWon ? "三间秘境全部完成" : DepthChallenge.FailureReason(depthRoom, state, playerId), 24, new Color(.80f, .94f, .90f), TextAnchor.MiddleCenter);
                if (Button(new Rect(210, 980, 660, 105), "再次深潜", new Color(.10f, .60f, .49f))) StartDepth();
                if (Button(new Rect(210, 1110, 660, 100), "返回大厅", new Color(.17f, .25f, .34f))) BackToMenu();
            }
        }

        private void BeginDaily(ApiReply run) => BeginDailyWithMatch(run, null);

        private void BeginComparison(int variant)
        {
            if (!api.Config.ComparisonPreview || variant < 0 || variant > 3) return;
            comparisonVariant = variant;
            int seed = PracticeSeed();
            var match = variant == 0 ? new DailyMatch(seed, DailyMatch.PreviousRulesVersion) :
                variant == 1 ? new DailyMatch(seed, DailyMatch.RulesVersion) : variant == 2 ? DailyMatch.CreateNoCaptureComparison(seed) : DailyMatch.CreateOpeningExperiment(seed);
            BeginDailyWithMatch(null, match);
            firstRun = false;
            resultNote = "对照试玩 · 不计账号奖励或排行";
        }

        private void BeginDailyWithMatch(ApiReply run, DailyMatch comparison)
        {
            nameplates.ClearHistory(); opponentEdgeId = "";
            dragSteering.Reset();
            connection.Close(); online = false; inGame = true; paused = false; status = ""; room = "";
            playerId = "you"; arena = null; fixedPve = null; daily = comparison ?? new DailyMatch(run != null ? run.seed : PracticeSeed(),
                run != null && !string.IsNullOrEmpty(run.rules) ? run.rules : DailyMatch.RulesVersion);
            state = daily.State; verifiedDaily = false; verifiedShareToken = ""; matchHighlights.Reset(); matchHighlights.Observe(state, playerId); eventOutcome.Reset(); eliminationFeedback.Reset(); eliminationFeedback.Observe(state, playerId);
            killFeedback.Reset(); killFeedback.Observe(state, playerId);
            activeRun = run; resultRecorded = false; runInputs.Clear();
            activePracticeAccount = "";
            activePlayerId = api.Profile != null ? api.Profile.playerId : ""; lastRewardId = "";
            stepTimer = 0; direction = new Point(1, 0); pointerBoost = false;
            controlActions = 0; firstRun = !CurrentProfile.tutorialComplete;
            hasSteered = false; tutorialClosedLoop = false; tutorialRebounded = false; tutorialBoosted = false;
            resultNote = run == null ? "离线练习不计入排行榜" : "挑战完成后验证成绩";
            arenaRenderer.SelectedSkin = EffectiveSkin;
        }

        private void RecordResult()
        {
            resultRecorded = true;
            if (comparisonVariant >= 0 && api.Config.ComparisonPreview) { resultNote = "对照试玩 · 不计账号奖励或排行"; return; }
            if (fixedPve != null) { VerifyFixedPreview(); return; }
            if (depthMode) { resultNote = "深潜练习 · 不计账号奖励或排行"; return; }
            if (activeRun == null || string.IsNullOrEmpty(activePlayerId))
            {
                if (controlActions > 0) { practiceProfile.tutorialComplete = true; practiceProfile.completedMatches++; }
                if (daily == null && !depthMode && controlActions > 0 && !string.IsNullOrEmpty(activePracticeAccount))
                    practicedAccount = activePracticeAccount;
                resultNote = daily != null ? "练习局 · 不计入排行榜" : api.Connected ? "练习局 · 不计账号奖励" : "离线练习 · 不计账号奖励";
                if (daily == null && !depthMode && api.Connected && api.Profile.playerId == practicedAccount && !string.IsNullOrEmpty(practicedAccount))
                    resultNote += "\n返回大厅可参加每日挑战";
                return;
            }
            string runId = activeRun.runId;
            var pending = ReadPending();
            pending.entries.Add(new PendingChallenge { playerId = activePlayerId, runId = runId, token = activeRun.token,
                mode = daily != null ? "daily" : "pve", rules = activeRun.rules, inputs = daily != null || !depthMode ? Convert.ToBase64String(runInputs.ToArray()) : "",
                score = daily != null ? daily.Score : 0, seconds = state.tick / 20, actions = controlActions });
            SavePending(pending);
            resultNote = api.Connected && appFocused && !appPaused && platformVisible && !adShowing ? "正在结算…" : "结果已保存，返回游戏后重试";
            FlushPending();
        }

        private void VerifyFixedPreview()
        {
            var world = fixedPve;
            var run = activeRun;
            if (run == null || !api.Config.FixedPveServerPreview || !api.Connected || api.Profile.playerId != activePlayerId)
            { resultNote = "整数规则试玩 · 不计账号奖励或排行"; return; }
            string player = activePlayerId;
            resultNote = "正在验证本机回放 · 不计奖励或排行";
            api.Post("internal/fixed-pve/verify", new ApiRequest { runId = run.runId, token = run.token,
                inputs = Convert.ToBase64String(runInputs.ToArray()) }, reply =>
            {
                if (fixedPve != world || activeRun != run || activePlayerId != player) return;
                bool matches = reply.ok && reply.verified && reply.experimental && reply.rules == PveFixedRecording.ExperimentalRules &&
                    reply.rewards == 0 && reply.ticks == world.Tick && reply.winner == world.Winner &&
                    reply.kills == world.Fighters[0].kills && reply.length == world.Fighters[0].motion.Length &&
                    reply.eventPoints == world.Fighters[0].eventPoints;
                resultNote = matches ? "本机回放一致 · 不计奖励或排行" : reply.ok ?
                    "本机回放结果不一致 · 不计奖励或排行" : "本机回放未验证 · 不计奖励或排行";
            });
        }

        private static PendingChallenges ReadPending()
        {
            string raw = PlayerPrefs.GetString(PendingKey, "");
            if (!string.IsNullOrEmpty(raw)) try { var data = JsonUtility.FromJson<PendingChallenges>(raw); if (data != null && data.entries != null) return data; } catch (Exception) { }
            return new PendingChallenges();
        }

        private static void SavePending(PendingChallenges data)
        {
            PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        private void PumpIncomingFriend()
        {
            if (api.ChallengeRevision != observedChallengeRevision)
            {
                observedChallengeRevision = api.ChallengeRevision;
                incomingFriend.Offer(api.LaunchChallengeToken);
            }
            if (!incomingFriend.Pending || !appFocused || appPaused || !platformVisible || adShowing || connecting || busy ||
                (inGame && state != null && state.phase == "running")) return;
            if (!incomingFriend.Begin(Time.unscaledTime, out string token, out int version)) return;
            if (!api.Connected)
            {
                incomingFriend.Complete(version, true, Time.unscaledTime);
                ConnectProfile();
                return;
            }
            api.Get("challenge/shared?token=" + Uri.EscapeDataString(token), shared =>
            {
                bool retryable = !shared.ok && (shared.httpStatus == 0 || shared.httpStatus == 401 || shared.httpStatus == 429 || shared.httpStatus >= 500);
                if (!incomingFriend.Complete(version, retryable, Time.unscaledTime)) return;
                if (shared.ok && shared.date == api.Date && DailyMatch.SupportsFriendChallenge(shared.rules))
                {
                    friendDate = shared.date; friendRules = shared.rules; friendScore = shared.score;
                    frontNotice = "好友今日挑战 " + friendScore + " 分 · 点击接受挑战";
                }
                else frontNotice = retryable ? "好友挑战暂时无法验证，将自动重试" : "好友挑战无效或已过期";
            });
        }

        private void PumpPending()
        {
            if (!appFocused || appPaused || !platformVisible || adShowing || flushing || connecting || busy ||
                (inGame && state != null && state.phase == "running") || Time.unscaledTime < nextPendingPoll ||
                !pendingRetry.Due(Time.unscaledTime)) return;
            nextPendingPoll = Time.unscaledTime + 2f;
            var entries = ReadPending().entries;
            if (api.Profile == null)
            {
                if (entries.Exists(item => item != null && !string.IsNullOrEmpty(item.playerId))) ConnectProfile();
            }
            else if (entries.Exists(item => item != null && PendingRetrySchedule.OwnedBy(item.playerId, api.Profile.playerId)))
            {
                if (api.Connected) FlushPending(); else ConnectProfile();
            }
        }

        private void FlushPending()
        {
            if (!appFocused || appPaused || !platformVisible || adShowing || flushing || !api.Connected) return;
            var pending = ReadPending();
            var entry = pending.entries.Find(item => item != null && PendingRetrySchedule.OwnedBy(item.playerId, api.Profile.playerId) &&
                settlementRetry.Due(item.playerId, item.runId, Time.unscaledTime));
            if (entry == null) return;
            flushing = true;
            bool challenge = entry.mode != "pve";
            api.Post(challenge ? "challenge/submit" : "match/finish", new ApiRequest { runId = entry.runId, token = entry.token, inputs = entry.inputs, score = entry.score, seconds = entry.seconds, actions = entry.actions }, reply =>
            {
                flushing = false;
                bool terminal = !string.IsNullOrEmpty(reply.error) && (reply.error.Contains("凭证无效或已过期") || reply.error.Contains("未检测到操作") || reply.error.Contains("回放") || reply.error.Contains("成绩与") || reply.error.Contains("规则已更新") || reply.error.Contains("对局时长无效"));
                if (reply.ok || terminal)
                {
                    settlementRetry.Remove(entry.playerId, entry.runId);
                    var latest = ReadPending(); latest.entries.RemoveAll(item => item != null && item.runId == entry.runId && PendingRetrySchedule.OwnedBy(item.playerId, entry.playerId)); SavePending(latest);
                    if (!reply.ok && (activeRun == null || activeRun.runId != entry.runId)) frontNotice = "待结算对局未通过：" + reply.error;
                    if (activeRun != null && activeRun.runId == entry.runId)
                    {
                        string rewards = "金币 +" + (reply.coins + reply.questCoins) + "  经验 +" + (reply.experience + reply.questExperience) + (reply.questExperience > 0 ? "（含日常）" : "");
                        if (reply.unlockedSkin > 0 && reply.unlockedSkin < SkinCatalog.OwnedSkinCount) rewards += "\n解锁皮肤：" + SkinCatalog.Get(reply.unlockedSkin).Name;
                        verifiedDaily = challenge && reply.ok && reply.verified;
                        verifiedShareToken = verifiedDaily ? reply.shareToken : "";
                        verifiedShareScore = reply.score;
                        string comparison = verifiedDaily && activeFriendScore >= 0 && activeRun.date == activeFriendDate && entry.rules == activeFriendRules
                            ? FriendChallengeComparison.Format(reply.score, activeFriendScore) : "";
                        resultNote = reply.ok ? (challenge ? "成绩已验证 · " : "") + rewards + comparison : reply.error;
                        if (reply.ok && reply.coins > 0) { lastRewardId = entry.runId; lastRewardKind = "match"; lastRewardCoins = reply.coins; }
                        if (reply.ok && challenge && activeRun.date == api.Date)
                        {
                            string expectedDate = activeRun.date;
                            string expectedRules = string.IsNullOrEmpty(entry.rules) ? DailyMatch.LegacyRulesVersion : entry.rules;
                            api.Get("challenge/leaderboard?rules=" + Uri.EscapeDataString(expectedRules), ranking =>
                            {
                                if (!ranking.ok || activeRun == null || activeRun.runId != entry.runId || api.Profile == null ||
                                    api.Profile.playerId != entry.playerId || api.Date != expectedDate || ranking.date != expectedDate ||
                                    (!string.IsNullOrEmpty(ranking.rules) && ranking.rules != expectedRules)) return;
                                string position = ranking.rank > 0 ? "今日最佳第 " + ranking.rank + " 名 / " + ranking.total + " 人" : "成绩已记录";
                                if (ranking.rank > 1) position += ranking.gap == 0 ? " · 同分先达者领先" : " · 距上一名 " + ranking.gap + " 分";
                                resultNote = position + "\n" + rewards + comparison;
                            });
                        }
                    }
                    FlushPending();
                }
                else
                {
                    settlementRetry.Fail(entry.playerId, entry.runId, Time.unscaledTime);
                    // Check other due runs on the next frame without recursive
                    // callbacks; this run remains durable and backs off alone.
                    nextPendingPoll = Time.unscaledTime;
                    if (activeRun != null && activeRun.runId == entry.runId) resultNote = "成绩已保存，将自动重试";
                    else frontNotice = "有待结算对局，将自动重试";
                }
            });
        }

        private void ShareCurrentChallenge()
        {
            if (busy || !verifiedDaily || activeRun == null || !api.Connected || string.IsNullOrEmpty(verifiedShareToken)) return;
            // The signed token was delivered with the verified result, so the
            // platform share call remains directly inside the user's tap.
            GamePlatform.ShareChallenge(verifiedShareToken, verifiedShareScore, message => { if (!string.IsNullOrEmpty(message)) resultNote = message; });
        }

        private void DrawFront()
        {
            if (Event.current.type == EventType.KeyDown && frontPage == "home")
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) { StartMain(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.D) { RequestDaily(); Event.current.Use(); }
            }
#if UNITY_EDITOR
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F10) { developerRooms = !developerRooms; onlineMenu = false; }
#endif
            if (developerRooms) { if (homeAvatar != null) homeAvatar.Visible = false; DrawLegacyMenu(); return; }
            if (frontPage == "home" || frontPage == "skins") return;
            if (homeAvatar != null) { homeAvatar.Visible = frontPage == "caves"; homeAvatar.Skin = EffectiveSkin; }
            if (caveBackground != null) GUI.DrawTexture(new Rect(0, 0, 1080, 1920), caveBackground, ScaleMode.ScaleAndCrop);
            Color old = GUI.color; GUI.color = new Color(.015f, .035f, .065f, .28f); GUI.DrawTexture(new Rect(0, 0, 1080, 1920), Texture2D.whiteTexture); GUI.color = old;
            if (frontPage == "home") DrawHome();
            else if (frontPage == "skins") DrawSkins();
            else if (frontPage == "talents") DrawTalents();
            else if (frontPage == "caves") DrawCaves();
            else if (frontPage == "cave-album") DrawCaveAlbum();
            else if (frontPage == "leaderboard") DrawLeaderboard();
            else if (frontPage == "more") DrawMore();
            else if (frontPage == "comparison" && api.Config.ComparisonPreview) DrawComparison();
        }

        private void DrawComparison()
        {
            Panel(new Rect(75, 320, 930, 1160), new Color(.035f, .065f, .12f, .96f));
            Label(new Rect(120, 370, 840, 80), "玩法对照试玩", 45, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(140, 465, 800, 95), "请选择一个方案 · 不计奖励或排行\n各方案使用同一角色和当日种子", 25, new Color(.75f, .88f, .91f), TextAnchor.MiddleCenter);
            if (Button(new Rect(180, 585, 720, 115), "试玩方案 A", new Color(.13f, .48f, .45f))) BeginComparison(0);
            if (Button(new Rect(180, 730, 720, 115), "试玩方案 B", new Color(.21f, .36f, .58f))) BeginComparison(1);
            if (Button(new Rect(180, 875, 720, 115), "试玩方案 C", new Color(.40f, .31f, .57f))) BeginComparison(2);
            if (Button(new Rect(180, 1020, 720, 115), "试玩方案 D", new Color(.48f, .34f, .25f))) BeginComparison(3);
            if (Button(new Rect(250, 1210, 580, 90), "返回大厅", new Color(.15f, .22f, .31f))) frontPage = "home";
        }

        private void DrawHome()
        {
            PlayerProgress p = CurrentProfile;
            Panel(new Rect(50, 50, 980, 116), new Color(.025f, .07f, .12f, .84f));
            Label(new Rect(85, 72, 440, 55), "等级 " + Progression.Level(p.experience) + "   天赋点 " + Progression.AvailablePoints(p), 27, Color.white, TextAnchor.MiddleLeft, true);
            Label(new Rect(560, 72, 420, 55), "金币  " + p.coins, 29, new Color(1f, .82f, .43f), TextAnchor.MiddleRight, true);
            Label(new Rect(80, 205, 920, 56), "S N A K E   C L U B", 28, new Color(.50f, .96f, .88f), TextAnchor.MiddleCenter, true);
            Label(new Rect(80, 262, 920, 120), "灵蛇秘境", 82, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(100, 382, 880, 55), "围猎对手  ·  梦游探洞  ·  每日争榜", 30, new Color(.76f, .91f, .93f), TextAnchor.MiddleCenter);
            if (homeAvatar != null && homeAvatar.Texture != null) GUI.DrawTexture(new Rect(260, 465, 560, 540), homeAvatar.Texture, ScaleMode.ScaleToFit, true);
            if (Button(new Rect(120, 1035, 840, 142), busy ? "正在准备…" : "开始对战", new Color(.08f, .61f, .50f))) RequestPve();
            if (Button(new Rect(120, 1205, 840, 130), friendDate == api.Date && !string.IsNullOrEmpty(friendDate) ? "应战好友  ·  60 秒" : "每日挑战  ·  60 秒", new Color(.20f, .38f, .69f))) RequestDaily();
            if (SmallButton(new Rect(90, 1390, 205, 108), "皮肤", new Color(.26f, .36f, .54f), 30)) { frontPage = "skins"; previewSkin = p.selectedSkin; }
            if (SmallButton(new Rect(320, 1390, 205, 108), "天赋", new Color(.30f, .30f, .56f), 30)) frontPage = "talents";
            if (SmallButton(new Rect(550, 1390, 205, 108), "探洞", new Color(.19f, .42f, .47f), 30)) OpenCaves();
            if (SmallButton(new Rect(780, 1390, 205, 108), "排行", new Color(.40f, .32f, .47f), 30)) OpenBoard();
            Panel(new Rect(120, 1560, 840, 156), new Color(.025f, .07f, .12f, .82f));
            string progress = p.expedition != null && !p.expedition.claimed ? "梦游探洞进行中，回来可领取奖励" : "派遣灵蛇梦游探洞，离线也有收获";
            Label(new Rect(145, 1580, 790, 65), progress, 28, new Color(.85f, .96f, .94f), TextAnchor.MiddleCenter);
            Label(new Rect(145, 1650, 790, 40), frontNotice, 22, new Color(.67f, .84f, .86f), TextAnchor.MiddleCenter);
            if (!api.Connected && SmallButton(new Rect(90, 1760, 240, 70), "重新连接", new Color(.12f, .35f, .34f), 22)) ConnectProfile();
            if (SmallButton(new Rect(790, 1760, 200, 70), feedback.Enabled ? "音效：开" : "音效：关", new Color(.12f, .24f, .31f), 22)) feedback.Enabled = !feedback.Enabled;
        }

        private void FrontHeader(string title, string subtitle, string back = "home")
        {
            Panel(new Rect(55, 65, 970, 215), new Color(.025f, .065f, .12f, .91f));
            if (SmallButton(new Rect(80, 102, 130, 76), "返回", new Color(.15f, .25f, .33f), 24)) { frontPage = back; arenaRenderer.SelectedSkin = EffectiveSkin; }
            Label(new Rect(225, 91, 610, 82), title, 48, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(100, 185, 880, 55), subtitle, 25, new Color(.68f, .84f, .87f), TextAnchor.MiddleCenter);
        }

        private void DrawSkins()
        {
            PlayerProgress p = CurrentProfile;
            FrontHeader("灵蛇衣橱", "收集外观与小幅属性 · 每日挑战使用统一属性");
            if (homeAvatar != null && homeAvatar.Texture != null) GUI.DrawTexture(new Rect(185, 280, 710, 600), homeAvatar.Texture, ScaleMode.ScaleToFit, true);
            Label(new Rect(180, 815, 720, 70), Progression.SkinNames[previewSkin] + "  ·  " + Progression.SkinDescriptions[previewSkin], 32, Color.white, TextAnchor.MiddleCenter, true);
            int count = api.Config.allowReferenceSkinPreview ? SkinCatalog.All.Length : SkinCatalog.OwnedSkinCount;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                Rect card = new Rect(90 + i % 2 * 465, 905 + i / 2 * 225, 435, 205);
                Panel(card, i == previewSkin ? new Color(.10f, .31f, .33f, .96f) : new Color(.035f, .10f, .16f, .93f));
                Label(new Rect(card.x + 25, card.y + 8, 385, 48), Progression.SkinNames[i], 31, Color.white, TextAnchor.MiddleCenter, true);
                Label(new Rect(card.x + 25, card.y + 58, 385, 43), Progression.SkinDescriptions[i], 22, new Color(.66f, .89f, .84f), TextAnchor.MiddleCenter);
                if (SmallButton(new Rect(card.x + 24, card.y + 118, 145, 70), "预览", new Color(.18f, .31f, .40f), 24)) previewSkin = index;
                bool reference = SkinCatalog.Get(i).ReferencePreview;
                string action = reference ? (EffectiveSkin == i ? "试用中" : "本机试用") : p.ownedSkins.Contains(i) ? (EffectiveSkin == i ? "已使用" : "使用") : Progression.SkinPrices[i] + " 金币";
                if (SmallButton(new Rect(card.x + 188, card.y + 118, 220, 70), action, new Color(.12f, .51f, .44f), 23) && EffectiveSkin != index)
                {
                    if (reference)
                    {
                        localVisualSkin = index; PlayerPrefs.SetInt("snake.v031.previewSkin", index); PlayerPrefs.Save();
                        previewSkin = index; frontNotice = "本机造型试用中";
                    }
                    else if (!api.Connected && !p.ownedSkins.Contains(index)) frontNotice = "联网后可解锁并保存皮肤";
                    else if (!api.Connected) { localVisualSkin = -1; practiceProfile.selectedSkin = index; PlayerPrefs.DeleteKey("snake.v031.previewSkin"); previewSkin = index; }
                    else Mutate(p.ownedSkins.Contains(index) ? "skin/equip" : "skin/buy", new ApiRequest { index = index }, reply =>
                    {
                        if (reply.ok) { localVisualSkin = -1; PlayerPrefs.DeleteKey("snake.v031.previewSkin"); previewSkin = index; }
                    });
                }
            }
            if (p.trialSkin > 0)
                Label(new Rect(120, 1600, 840, 70), "下场普通对局试穿 " + Progression.SkinNames[p.trialSkin], 26, new Color(.65f, .95f, .84f), TextAnchor.MiddleCenter);
            else if (previewSkin < SkinCatalog.OwnedSkinCount && p.tutorialComplete && !p.dailyTrialUsed && !p.ownedSkins.Contains(previewSkin) && GamePlatform.HasRewardedAds(api))
            {
                int selected = previewSkin;
                if (SmallButton(new Rect(210, 1590, 660, 95), "看 1 次广告，试穿 1 局", new Color(.25f, .40f, .55f), 25))
                    StartRewarded("trial", "", selected, reply => { if (reply.ok) frontNotice = "试穿已领取，将在下场普通对局使用"; });
            }
            Label(new Rect(100, 1710, 880, 90), "当前金币 " + p.coins + "  ·  " + frontNotice, 25, new Color(.83f, .92f, .92f), TextAnchor.MiddleCenter);
        }

        private void DrawMore()
        {
            FrontHeader("更多玩法", "成长、挑战与排行");
            if (Button(new Rect(130, 390, 820, 140), "普通围猎  ·  属性生效", new Color(.10f, .55f, .46f))) RequestPve();
            if (Button(new Rect(130, 570, 820, 140), "每日挑战  ·  60 秒", new Color(.20f, .38f, .69f))) RequestDaily();
            if (Button(new Rect(130, 750, 820, 140), "每日排行榜", new Color(.34f, .31f, .62f))) OpenBoard();
            if (Button(new Rect(130, 930, 820, 140), "灵蛇天赋", new Color(.30f, .30f, .56f))) frontPage = "talents";
            int completed = DepthChallenge.CompletedMatches(CurrentProfile);
            if (Button(new Rect(130, 1110, 820, 140), completed >= 3 ? "秘境深潜  ·  三重挑战" : "秘境深潜  ·  " + completed + "/3 场", new Color(.42f, .28f, .57f))) StartDepth();
            Label(new Rect(150, 1300, 780, 120), string.IsNullOrEmpty(frontNotice) ? "每日挑战采用统一属性\n普通围猎可体验皮肤与天赋加成" : frontNotice, 25, new Color(.84f, .96f, .94f), TextAnchor.MiddleCenter);
        }

        private void DrawTalents()
        {
            PlayerProgress p = CurrentProfile;
            FrontHeader("灵蛇天赋", "自由搭配，可免费重置；每日榜不应用属性");
            Panel(new Rect(75, 310, 930, 150), new Color(.025f, .08f, .14f, .91f));
            Label(new Rect(100, 330, 880, 55), "成长等级 " + Progression.Level(p.experience) + "     可用点数 " + Progression.AvailablePoints(p), 32, Color.white, TextAnchor.MiddleCenter, true);
            int next = Progression.ExperienceForLevel(Progression.Level(p.experience) + 1);
            Label(new Rect(100, 390, 880, 40), "距离下一级还需 " + Math.Max(0, next - p.experience) + " 经验", 24, new Color(.68f, .84f, .87f), TextAnchor.MiddleCenter);
            string[] branches = { "追风", "周旋", "探洞" };
            for (int column = 0; column < 3; column++)
            {
                Label(new Rect(80 + column * 305, 505, 295, 60), branches[column], 32, new Color(.63f, .96f, .85f), TextAnchor.MiddleCenter, true);
                for (int row = 0; row < 3; row++)
                {
                    int index = column * 3 + row;
                    Rect card = new Rect(82 + column * 305, 580 + row * 265, 285, 235);
                    Panel(card, new Color(.035f, .10f, .16f, .93f));
                    Label(new Rect(card.x + 10, card.y + 16, 265, 48), Progression.TalentNames[index], 25, Color.white, TextAnchor.MiddleCenter, true);
                    Label(new Rect(card.x + 12, card.y + 62, 261, 42), Progression.TalentDescriptions[index], 19, new Color(.68f, .84f, .87f), TextAnchor.MiddleCenter);
                    int rank = p.talents[index];
                    float each = index < 6 ? .5f : 1f;
                    string current = (rank * each).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                    string nextRank = (Math.Min(3, rank + 1) * each).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                    Label(new Rect(card.x + 12, card.y + 102, 261, 35), rank >= 3 ? "当前 +" + current + "% · 已满级" : "当前 +" + current + "% → +" + nextRank + "%", 18, new Color(.80f, .94f, .86f), TextAnchor.MiddleCenter);
                    string value = p.talents[index] + " / 3  +";
                    if (SmallButton(new Rect(card.x + 40, card.y + 137, 205, 70), value, new Color(.13f, .43f, .42f), 24))
                    {
                        if (api.Connected) Mutate("talent/allocate", new ApiRequest { index = index });
                        else if (!Progression.Allocate(practiceProfile, index)) frontNotice = "天赋点不足或已满级";
                    }
                }
            }
            if (SmallButton(new Rect(95, 1450, 425, 95), "推荐加点", new Color(.12f, .45f, .40f), 27))
            {
                if (api.Connected) Mutate("talent/recommend", new ApiRequest());
                else frontNotice = Progression.AllocateRecommended(practiceProfile) > 0 ? "已按推荐方案分配" : "没有可分配的天赋点";
            }
            if (SmallButton(new Rect(560, 1450, 425, 95), "免费重置", new Color(.23f, .32f, .40f), 27))
            {
                if (api.Connected) Mutate("talent/reset", new ApiRequest()); else practiceProfile.talents = new int[9];
            }
            Label(new Rect(80, 1560, 920, 80), string.IsNullOrEmpty(frontNotice) ? (api.Connected ? "推荐加点会分配全部可用点数，可随时免费重置" : "离线天赋仅供练习，不会写入账号") : frontNotice, 23, new Color(.75f, .88f, .88f), TextAnchor.MiddleCenter);
        }

        private void OpenCaves()
        {
            caveSync.Invalidate();
            frontPage = "caves";
        }

        private void PumpCaveSync()
        {
            if (inGame || frontPage != "caves" || !appFocused || appPaused || !platformVisible || !api.Connected ||
                busy || connecting || flushing || adShowing || !caveSync.TryBegin(Time.unscaledTime, out int version)) return;
            busy = true;
            api.Get("bootstrap", reply =>
            {
                busy = false;
                bool success = reply.ok && reply.serverTime > 0;
                if (!caveSync.Complete(version, success, Time.unscaledTime)) return;
                frontNotice = success ? "探洞进度已同步" : "暂时无法同步探洞进度，将自动重试";
            });
        }

        private void DrawCaves()
        {
            PlayerProgress p = api.Profile ?? practiceProfile;
            FrontHeader("梦游探洞", "派遣梦境分身，离开游戏后也会继续探索");
            ExpeditionState expedition = p.expedition;
            if (!api.Connected)
            {
                Panel(new Rect(100, 570, 880, 390), new Color(.025f, .08f, .14f, .94f));
                string offline = expedition != null && !expedition.claimed
                    ? "已有探洞进行中\n联网后同步进度并领取" : "连接账号后可派遣探洞\n对战仍可离线练习";
                Label(new Rect(150, 650, 780, 170), offline, 35, Color.white, TextAnchor.MiddleCenter, true);
                if (SmallButton(new Rect(250, 845, 580, 90), "重新连接", new Color(.12f, .51f, .44f), 28)) ConnectProfile();
                return;
            }
            if (!caveSync.Ready)
            {
                Panel(new Rect(100, 570, 880, 390), new Color(.025f, .08f, .14f, .94f));
                Label(new Rect(150, 650, 780, 140), caveSync.InFlight ? "正在同步探洞进度…" : "探洞进度待同步\n联网后可领取", 32, Color.white, TextAnchor.MiddleCenter, true);
                if (!caveSync.InFlight && SmallButton(new Rect(250, 845, 580, 90), "重试同步", new Color(.12f, .51f, .44f), 28)) caveSync.Invalidate();
                return;
            }
            if (expedition != null && !expedition.claimed)
            {
                long end = expedition.startedAt + expedition.durationMinutes * 60000L;
                long remaining = Math.Max(0, end - api.EstimatedServerTime);
                Panel(new Rect(100, 410, 880, 560), new Color(.025f, .08f, .14f, .94f));
                Label(new Rect(150, 460, 780, 75), Progression.CaveNames[expedition.cave] + " · 梦游中", 40, Color.white, TextAnchor.MiddleCenter, true);
                if (homeAvatar != null && homeAvatar.Texture != null)
                {
                    float drift = reducedEffects ? 0 : Mathf.Sin(Time.unscaledTime * 2f) * 12f;
                    GUI.DrawTexture(new Rect(135 + drift, 550, 250, 230), homeAvatar.Texture, ScaleMode.ScaleToFit, true);
                }
                Label(new Rect(380, 560, 535, 80), remaining == 0 ? "探索完成" : "剩余 " + FormatCountdown(remaining), 34, new Color(.60f, .95f, .83f), TextAnchor.MiddleCenter);
                Label(new Rect(365, 665, 550, 100), "预计完整收益 " + Progression.ExpeditionCoins(expedition.durationMinutes, expedition.bonusPercent) + " 金币 + " + Progression.ExpeditionAvailableExperience(expedition.durationMinutes, p.dailyExpeditionXp) + " 经验\n每日探洞经验剩余 " + Progression.ExpeditionRemainingExperience(p.dailyExpeditionXp), 23, new Color(.72f, .85f, .88f), TextAnchor.MiddleCenter);
                if (SmallButton(new Rect(240, 805, 600, 105), remaining == 0 ? "领取收获" : "提前带回（按时间结算）", new Color(.10f, .55f, .46f), 27))
                {
                    string id = expedition.id;
                    Mutate("expedition/claim", new ApiRequest { id = id }, reply =>
                    {
                        if (reply.ok)
                        {
                            frontNotice = "探洞收获：金币 +" + reply.coins + "  经验 +" + reply.experience + (reply.fragments > 0 ? "  碎片 +" + reply.fragments : "");
                            caveStory = reply.fragments > 0 ? "我回来啦！带回了洞穴碎片" : "我回来啦，收获已经带回"; caveStoryUntil = Time.unscaledTime + 4f;
                            lastRewardId = id; lastRewardKind = "cave"; lastRewardCoins = reply.baseCoins;
                        }
                    });
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    int index = i;
                    Rect card = new Rect(95, 360 + i * 260, 890, 225);
                    Panel(card, new Color(.025f, .08f, .14f, .93f));
                    Label(new Rect(card.x + 35, card.y + 24, 430, 60), Progression.CaveNames[i] + "  ·  " + (Progression.CaveMinutes[i] / 60) + " 小时", 32, Color.white, TextAnchor.MiddleLeft, true);
                    int fragments = p.caveFragments != null && p.caveFragments.Length == 3 ? p.caveFragments[i] : 0;
                    Label(new Rect(card.x + 35, card.y + 95, 545, 100), "约 " + Progression.ExpeditionCoins(Progression.CaveMinutes[i], Progression.CaveBonusPercent(p)) + " 金币 + " + Progression.ExpeditionAvailableExperience(Progression.CaveMinutes[i], p.dailyExpeditionXp) + " 经验\n碎片 " + fragments + " · " + Progression.CaveMilestone(fragments), 23, new Color(.68f, .86f, .87f), TextAnchor.MiddleLeft);
                    if (SmallButton(new Rect(card.x + 610, card.y + 75, 230, 95), "派遣", new Color(.10f, .55f, .46f), 30))
                        Mutate("expedition/start", new ApiRequest { index = index }, reply =>
                        {
                            if (reply.ok) { caveStory = "我出发啦，稍后见！"; caveStoryUntil = Time.unscaledTime + 4f; }
                        });
                }
                if (SmallButton(new Rect(260, 1140, 560, 90), "查看碎片图鉴", new Color(.24f, .36f, .48f), 26)) frontPage = "cave-album";
                Label(new Rect(100, 1240, 880, 90), "同时仅可探索一个洞穴，最多累计 8 小时。\n每日探洞经验剩余 " + Progression.ExpeditionRemainingExperience(p.dailyExpeditionXp), 24, new Color(.77f, .88f, .88f), TextAnchor.MiddleCenter);
                DrawRewardOffer(new Rect(200, 1340, 680, 95));
            }
            if (Time.unscaledTime < caveStoryUntil && homeAvatar != null && homeAvatar.Texture != null)
            {
                GUI.DrawTexture(new Rect(95, 1470, 220, 220), homeAvatar.Texture, ScaleMode.ScaleToFit, true);
                Label(new Rect(315, 1500, 650, 65), caveStory, 27, new Color(1f, .92f, .65f), TextAnchor.MiddleLeft, true);
                Label(new Rect(315, 1570, 650, 80), frontNotice, 22, new Color(.80f, .91f, .91f), TextAnchor.MiddleLeft);
            }
            else Label(new Rect(100, 1530, 880, 100), frontNotice, 24, new Color(.80f, .91f, .91f), TextAnchor.MiddleCenter);
        }

        private void DrawCaveAlbum()
        {
            FrontHeader("梦游图鉴", "完整探洞获得碎片，解锁各洞穴的故事", "caves");
            PlayerProgress profile = CurrentProfile;
            for (int cave = 0; cave < Progression.CaveNames.Length; cave++)
            {
                int fragments = profile.caveFragments != null && cave < profile.caveFragments.Length ? Math.Max(0, profile.caveFragments[cave]) : 0;
                float y = 340 + cave * 360;
                Panel(new Rect(95, y, 890, 325), new Color(.025f, .08f, .14f, .93f));
                Label(new Rect(130, y + 20, 820, 60), Progression.CaveNames[cave] + "  ·  碎片 " + fragments, 32, Color.white, TextAnchor.MiddleLeft, true);
                for (int chapter = 0; chapter < 3; chapter++)
                {
                    bool unlocked = fragments >= Progression.CaveStoryThreshold(chapter);
                    Label(new Rect(145, y + 90 + chapter * 65, 790, 55), (chapter + 1) + "  " + Progression.CaveStory(cave, chapter, fragments), 25,
                        unlocked ? new Color(.80f, .95f, .87f) : new Color(.55f, .65f, .69f), TextAnchor.MiddleLeft);
                }
            }
            Label(new Rect(110, 1450, 860, 80), api.Connected ? "图鉴根据账号碎片自动解锁，不消耗碎片" : "连接账号后查看已解锁的图鉴", 23, new Color(.77f, .88f, .88f), TextAnchor.MiddleCenter);
        }

        private void OpenBoard()
        {
            frontPage = "leaderboard"; board = null;
            int generation = ++boardGeneration;
            if (!api.Connected) { frontNotice = "连接后可查看真人排行"; return; }
            api.Get("challenge/leaderboard?rules=" + DailyMatch.RulesVersion, reply =>
            {
                if (frontPage != "leaderboard" || generation != boardGeneration) return;
                bool matches = string.IsNullOrEmpty(reply.rules) || reply.rules == DailyMatch.RulesVersion;
                board = reply.ok && matches ? reply : null;
                frontNotice = !reply.ok ? reply.error : matches ? "" : "排行榜规则不一致，请重试";
            });
        }

        private void DrawLeaderboard()
        {
            FrontHeader("每日挑战榜", board == null ? "真实玩家 · 统一规则 · 成绩经回放验证" : board.date + " · " + DailyMatch.RulesVersion + " · 真实玩家");
            Panel(new Rect(80, 315, 920, 1110), new Color(.025f, .08f, .14f, .94f));
            if (board == null) Label(new Rect(120, 600, 840, 100), string.IsNullOrEmpty(frontNotice) ? "正在加载…" : frontNotice, 31, Color.white, TextAnchor.MiddleCenter);
            else
            {
                string position = board.rank > 0 ? "今日最佳第 " + board.rank + " 名 / " + board.total + " 人" : "完成挑战后参与排名";
                if (board.rank > 1) position += board.gap == 0 ? " · 同分先达到者领先" : " · 距上一名 " + board.gap + " 分";
                Label(new Rect(115, 345, 850, 90), position, 28, new Color(.71f, .96f, .84f), TextAnchor.MiddleCenter, true);
                if (board.rows != null) for (int i = 0; i < Math.Min(8, board.rows.Count); i++)
                {
                    var row = board.rows[i];
                    Label(new Rect(140, 470 + i * 102, 160, 70), "#" + row.rank, 32, new Color(1f, .82f, .43f), TextAnchor.MiddleLeft, true);
                    Label(new Rect(290, 470 + i * 102, 430, 70), row.name, 28, Color.white, TextAnchor.MiddleLeft);
                    Label(new Rect(700, 470 + i * 102, 180, 70), row.score.ToString(), 30, Color.white, TextAnchor.MiddleRight, true);
                }
            }
            if (SmallButton(new Rect(190, 1490, 700, 105), "挑战好友", new Color(.17f, .40f, .65f), 29))
                frontNotice = "请在已验证的每日挑战结果页分享";
            Label(new Rect(120, 1610, 840, 65), frontNotice, 22, new Color(.75f, .88f, .88f), TextAnchor.MiddleCenter);
        }

        private void Mutate(string route, ApiRequest request, Action<ApiReply> after = null)
        {
            if (busy) return;
            busy = true;
            api.Post(route, request, reply => { busy = false; frontNotice = reply.ok ? "已保存" : reply.error; after?.Invoke(reply); });
        }

        private void DrawRewardOffer(Rect rect)
        {
            if (string.IsNullOrEmpty(lastRewardId) || lastRewardCoins <= 0 || !GamePlatform.HasRewardedAds(api)) return;
            if (!SmallButton(rect, "看广告再得 " + lastRewardCoins + " 金币", new Color(.25f, .40f, .55f), 25)) return;
            string source = lastRewardId, kind = lastRewardKind;
            StartRewarded(kind, source, 0, reply => { if (reply.ok) { lastRewardId = ""; frontNotice = "额外金币 +" + reply.coins; } });
        }

        private void StartRewarded(string kind, string source, int index, Action<ApiReply> after)
        {
            if (adShowing) return;
            Mutate("ad/prepare", new ApiRequest { sourceId = source, kind = kind, index = index }, prepared =>
            {
                if (!prepared.ok) { after?.Invoke(prepared); return; }
                adShowing = true;
                feedback.SuspendAudio();
                if (inGame && state != null && state.phase == "running") SuspendInput();
                GamePlatform.ShowRewarded(api, (completed, error) =>
                {
                    adShowing = false;
                    if (!completed) { frontNotice = error; after?.Invoke(new ApiReply { error = error }); return; }
                    Mutate("ad/complete", new ApiRequest { id = prepared.id }, after);
                });
            });
        }

        private static string FormatCountdown(long milliseconds)
        {
            int seconds = (int)(milliseconds / 1000);
            return (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        private bool SmallButton(Rect rect, string text, Color color, int size)
        {
            var style = Style(size, Color.white, TextAnchor.MiddleCenter, true);
            style.normal.background = rounded; style.hover.background = rounded; style.active.background = rounded;
            style.border = new RectOffset(12, 12, 12, 12); style.hover.textColor = Color.white; style.active.textColor = Color.white;
            Color previous = GUI.backgroundColor; GUI.backgroundColor = color;
            bool pressed = GUI.Button(rect, text, style); GUI.backgroundColor = previous; return pressed;
        }
    }
}
