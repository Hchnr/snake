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
            public string playerId, runId, token, inputs, mode;
            public int score, seconds, actions;
        }
        [Serializable] private sealed class PendingChallenges { public List<PendingChallenge> entries = new List<PendingChallenge>(); }

        private GameApiClient api;
        private readonly PlayerProgress practiceProfile = new PlayerProgress();
        private DailyMatch daily;
        private readonly List<byte> dailyInputs = new List<byte>();
        private ApiReply activeRun;
        private Texture2D caveBackground;
        private HomeAvatar homeAvatar;
        private string frontPage = "home", frontNotice = "", resultNote = "", lastRewardId = "", lastRewardKind = "", activePlayerId = "", friendDate = "";
        private bool busy, resultRecorded, flushing, adShowing, connecting;
        private bool developerRooms = false;
        private int previewSkin, lastRewardCoins;
        private ApiReply board;
        private const string PendingKey = "snake.v3.pending";
        private PlayerProgress CurrentProfile => api != null && api.Connected ? api.Profile : practiceProfile;

        private void ConnectProfile()
        {
            if (connecting) return;
            connecting = true;
            caveBackground = Resources.Load<Texture2D>("MysticCave");
            if (caveBackground == null) Debug.LogWarning("[Snake Club] Home background could not be loaded.");
            if (homeAvatar == null) homeAvatar = gameObject.AddComponent<HomeAvatar>();
            frontNotice = "正在连接进度服务…";
            api.Login(reply =>
            {
                connecting = false;
                frontNotice = reply.ok ? "进度已同步" : "离线练习 · 进度和排行暂不同步";
                previewSkin = CurrentProfile.selectedSkin;
                arenaRenderer.SelectedSkin = previewSkin;
                if (reply.ok)
                {
                    friendDate = api.LaunchChallengeDate;
                    if (!string.IsNullOrEmpty(friendDate))
                    {
                        bool current = friendDate == api.Date && api.LaunchChallengeRules == DailyMatch.RulesVersion;
                        frontNotice = current ? "收到好友今日挑战，点击每日挑战开始" : "好友挑战已过期或规则已更新，可参加今天的挑战";
                        if (!current) friendDate = "";
                    }
                    FlushPending();
                }
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
            if (!api.Connected) { BeginDaily(null); return; }
            busy = true;
            api.Post("challenge/start", new ApiRequest(), reply => { busy = false; BeginDaily(reply.ok ? reply : null); });
        }

        private void BeginDaily(ApiReply run)
        {
            connection.Close(); online = false; inGame = true; paused = false; status = ""; room = "";
            playerId = "you"; arena = null; daily = new DailyMatch(run != null ? run.seed : PracticeSeed());
            state = daily.State; activeRun = run; resultRecorded = false; dailyInputs.Clear();
            activePlayerId = api.Profile != null ? api.Profile.playerId : ""; lastRewardId = "";
            stepTimer = 0; direction = new Point(1, 0); pointerBoost = false;
            controlActions = 0; firstRun = !CurrentProfile.tutorialComplete;
            resultNote = run == null ? "离线练习不计入排行榜" : "挑战完成后验证成绩";
            arenaRenderer.SelectedSkin = CurrentProfile.selectedSkin;
        }

        private void RecordResult()
        {
            resultRecorded = true;
            if (activeRun == null || string.IsNullOrEmpty(activePlayerId))
            {
                if (controlActions > 0) practiceProfile.tutorialComplete = true;
                resultNote = daily != null ? "练习局 · 不计入排行榜" : "离线练习 · 不计账号奖励"; return;
            }
            string runId = activeRun.runId;
            var pending = ReadPending();
            pending.entries.Add(new PendingChallenge { playerId = activePlayerId, runId = runId, token = activeRun.token,
                mode = daily != null ? "daily" : "pve", inputs = daily != null ? Convert.ToBase64String(dailyInputs.ToArray()) : "",
                score = daily != null ? daily.Score : 0, seconds = state.tick / 20, actions = controlActions });
            SavePending(pending);
            resultNote = api.Connected ? "正在结算…" : "结果已保存，联网后重试";
            FlushPending();
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

        private void FlushPending()
        {
            if (flushing || !api.Connected) return;
            var pending = ReadPending();
            var entry = pending.entries.Find(item => item.playerId == api.Profile.playerId);
            if (entry == null) return;
            flushing = true;
            bool challenge = entry.mode != "pve";
            api.Post(challenge ? "challenge/submit" : "match/finish", new ApiRequest { runId = entry.runId, token = entry.token, inputs = entry.inputs, score = entry.score, seconds = entry.seconds, actions = entry.actions }, reply =>
            {
                flushing = false;
                bool terminal = !string.IsNullOrEmpty(reply.error) && (reply.error.Contains("凭证无效或已过期") || reply.error.Contains("未检测到操作") || reply.error.Contains("回放") || reply.error.Contains("成绩与") || reply.error.Contains("规则已更新") || reply.error.Contains("对局时长无效"));
                if (reply.ok || terminal)
                {
                    var latest = ReadPending(); latest.entries.RemoveAll(item => item.runId == entry.runId); SavePending(latest);
                    if (activeRun != null && activeRun.runId == entry.runId)
                    {
                        string rewards = "金币 +" + (reply.coins + reply.questCoins) + "  经验 +" + (reply.experience + reply.questExperience) + (reply.questExperience > 0 ? "（含日常）" : "");
                        resultNote = reply.ok ? (challenge ? "成绩已验证 · " : "") + rewards : reply.error;
                        if (reply.ok && reply.coins > 0) { lastRewardId = entry.runId; lastRewardKind = "match"; lastRewardCoins = reply.coins; }
                        if (reply.ok && challenge) api.Get("challenge/leaderboard", ranking =>
                        {
                            if (!ranking.ok || activeRun == null || activeRun.runId != entry.runId) return;
                            string position = ranking.rank > 0 ? "今日第 " + ranking.rank + " 名 / " + ranking.total + " 人" : "成绩已记录";
                            if (ranking.rank > 1) position += ranking.gap == 0 ? " · 同分先达者领先" : " · 距上一名 " + ranking.gap + " 分";
                            resultNote = position + "\n" + rewards;
                        });
                    }
                    FlushPending();
                }
                else if (activeRun != null && activeRun.runId == entry.runId) resultNote = "成绩已保存，联网后重试";
            });
        }

        private void DrawFront()
        {
            if (Event.current.type == EventType.KeyDown && frontPage == "home")
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) { RequestPve(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.D) { RequestDaily(); Event.current.Use(); }
            }
#if UNITY_EDITOR
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F10) { developerRooms = !developerRooms; onlineMenu = false; }
#endif
            if (developerRooms) { if (homeAvatar != null) homeAvatar.Visible = false; DrawLegacyMenu(); return; }
            if (homeAvatar != null) { homeAvatar.Visible = frontPage == "home" || frontPage == "skins"; homeAvatar.Skin = frontPage == "skins" ? previewSkin : CurrentProfile.selectedSkin; }
            if (caveBackground != null) GUI.DrawTexture(new Rect(0, 0, 1080, 1920), caveBackground, ScaleMode.ScaleAndCrop);
            Color old = GUI.color; GUI.color = new Color(.015f, .035f, .065f, .28f); GUI.DrawTexture(new Rect(0, 0, 1080, 1920), Texture2D.whiteTexture); GUI.color = old;
            if (frontPage == "home") DrawHome();
            else if (frontPage == "skins") DrawSkins();
            else if (frontPage == "talents") DrawTalents();
            else if (frontPage == "caves") DrawCaves();
            else if (frontPage == "leaderboard") DrawLeaderboard();
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
            if (Button(new Rect(120, 1205, 840, 130), friendDate == api.Date && !string.IsNullOrEmpty(friendDate) ? "应战好友  ·  60 秒" : "每日挑战  ·  60 秒", new Color(.20f, .38f, .69f))) { friendDate = ""; RequestDaily(); }
            if (SmallButton(new Rect(90, 1390, 205, 108), "皮肤", new Color(.26f, .36f, .54f), 30)) { frontPage = "skins"; previewSkin = p.selectedSkin; }
            if (SmallButton(new Rect(320, 1390, 205, 108), "天赋", new Color(.30f, .30f, .56f), 30)) frontPage = "talents";
            if (SmallButton(new Rect(550, 1390, 205, 108), "探洞", new Color(.19f, .42f, .47f), 30)) frontPage = "caves";
            if (SmallButton(new Rect(780, 1390, 205, 108), "排行", new Color(.40f, .32f, .47f), 30)) OpenBoard();
            Panel(new Rect(120, 1560, 840, 156), new Color(.025f, .07f, .12f, .82f));
            string progress = p.expedition != null && !p.expedition.claimed ? "梦游探洞进行中，回来可领取奖励" : "派遣灵蛇梦游探洞，离线也有收获";
            Label(new Rect(145, 1580, 790, 65), progress, 28, new Color(.85f, .96f, .94f), TextAnchor.MiddleCenter);
            Label(new Rect(145, 1650, 790, 40), frontNotice, 22, new Color(.67f, .84f, .86f), TextAnchor.MiddleCenter);
            if (!api.Connected && SmallButton(new Rect(90, 1760, 240, 70), "重新连接", new Color(.12f, .35f, .34f), 22)) ConnectProfile();
            if (SmallButton(new Rect(790, 1760, 200, 70), feedback.Enabled ? "声音：开" : "声音：关", new Color(.12f, .24f, .31f), 22)) feedback.Enabled = !feedback.Enabled;
        }

        private void FrontHeader(string title, string subtitle)
        {
            Panel(new Rect(55, 65, 970, 215), new Color(.025f, .065f, .12f, .91f));
            if (SmallButton(new Rect(80, 102, 130, 76), "返回", new Color(.15f, .25f, .33f), 24)) { frontPage = "home"; arenaRenderer.SelectedSkin = CurrentProfile.selectedSkin; }
            Label(new Rect(225, 91, 610, 82), title, 48, Color.white, TextAnchor.MiddleCenter, true);
            Label(new Rect(100, 185, 880, 55), subtitle, 25, new Color(.68f, .84f, .87f), TextAnchor.MiddleCenter);
        }

        private void DrawSkins()
        {
            PlayerProgress p = CurrentProfile;
            FrontHeader("灵蛇衣橱", "收集外观与小幅属性 · 每日挑战使用统一属性");
            if (homeAvatar != null && homeAvatar.Texture != null) GUI.DrawTexture(new Rect(275, 285, 530, 430), homeAvatar.Texture, ScaleMode.ScaleToFit, true);
            Label(new Rect(180, 680, 720, 70), Progression.SkinNames[previewSkin] + "  ·  " + Progression.SkinDescriptions[previewSkin], 32, Color.white, TextAnchor.MiddleCenter, true);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Rect card = new Rect(90 + i % 2 * 465, 805 + i / 2 * 310, 435, 280);
                Panel(card, new Color(.035f, .10f, .16f, .93f));
                Label(new Rect(card.x + 25, card.y + 20, 385, 55), Progression.SkinNames[i], 34, Color.white, TextAnchor.MiddleCenter, true);
                Label(new Rect(card.x + 25, card.y + 80, 385, 50), Progression.SkinDescriptions[i], 24, new Color(.66f, .89f, .84f), TextAnchor.MiddleCenter);
                if (SmallButton(new Rect(card.x + 24, card.y + 155, 145, 80), "预览", new Color(.18f, .31f, .40f), 25)) previewSkin = index;
                string action = p.ownedSkins.Contains(i) ? (p.selectedSkin == i ? "已使用" : "使用") : Progression.SkinPrices[i] + " 金币";
                if (SmallButton(new Rect(card.x + 188, card.y + 155, 220, 80), action, new Color(.12f, .51f, .44f), 25) && p.selectedSkin != index)
                {
                    if (!api.Connected) frontNotice = "联网后可解锁并保存皮肤";
                    else Mutate(p.ownedSkins.Contains(index) ? "skin/equip" : "skin/buy", new ApiRequest { index = index }, reply => { if (reply.ok) previewSkin = index; });
                }
            }
            if (p.trialSkin > 0)
                Label(new Rect(120, 1490, 840, 70), "下场普通对局试穿 " + Progression.SkinNames[p.trialSkin], 26, new Color(.65f, .95f, .84f), TextAnchor.MiddleCenter);
            else if (p.tutorialComplete && !p.dailyTrialUsed && !p.ownedSkins.Contains(previewSkin) && GamePlatform.HasRewardedAds(api))
            {
                int selected = previewSkin;
                if (SmallButton(new Rect(210, 1470, 660, 95), "看 1 次广告，试穿 1 局", new Color(.25f, .40f, .55f), 25))
                    StartRewarded("trial", "", selected, reply => { if (reply.ok) frontNotice = "试穿已领取，将在下场普通对局使用"; });
            }
            Label(new Rect(100, 1590, 880, 90), "当前金币 " + p.coins + "  ·  " + frontNotice, 25, new Color(.83f, .92f, .92f), TextAnchor.MiddleCenter);
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
                    Label(new Rect(card.x + 12, card.y + 67, 261, 55), Progression.TalentDescriptions[index], 19, new Color(.68f, .84f, .87f), TextAnchor.MiddleCenter);
                    string value = p.talents[index] + " / 3  +";
                    if (SmallButton(new Rect(card.x + 40, card.y + 137, 205, 70), value, new Color(.13f, .43f, .42f), 24))
                    {
                        if (api.Connected) Mutate("talent/allocate", new ApiRequest { index = index });
                        else if (!Progression.Allocate(practiceProfile, index)) frontNotice = "天赋点不足或已满级";
                    }
                }
            }
            if (SmallButton(new Rect(290, 1450, 500, 95), "免费重置", new Color(.23f, .32f, .40f), 27))
            {
                if (api.Connected) Mutate("talent/reset", new ApiRequest()); else practiceProfile.talents = new int[9];
            }
            Label(new Rect(80, 1560, 920, 80), api.Connected ? frontNotice : "离线天赋仅供练习，不会写入账号", 23, new Color(.75f, .88f, .88f), TextAnchor.MiddleCenter);
        }

        private void DrawCaves()
        {
            PlayerProgress p = CurrentProfile;
            FrontHeader("梦游探洞", "派遣梦境分身，离开游戏后也会继续探索");
            ExpeditionState expedition = p.expedition;
            if (!api.Connected)
            {
                Panel(new Rect(100, 570, 880, 390), new Color(.025f, .08f, .14f, .94f));
                Label(new Rect(150, 650, 780, 170), "连接账号后可派遣探洞\n对战仍可离线练习", 35, Color.white, TextAnchor.MiddleCenter, true);
                if (SmallButton(new Rect(250, 845, 580, 90), "重新连接", new Color(.12f, .51f, .44f), 28)) ConnectProfile();
                return;
            }
            if (expedition != null && !expedition.claimed)
            {
                long end = expedition.startedAt + expedition.durationMinutes * 60000L;
                long remaining = Math.Max(0, end - api.EstimatedServerTime);
                Panel(new Rect(100, 410, 880, 560), new Color(.025f, .08f, .14f, .94f));
                Label(new Rect(150, 460, 780, 75), Progression.CaveNames[expedition.cave] + " · 梦游中", 40, Color.white, TextAnchor.MiddleCenter, true);
                Label(new Rect(150, 560, 780, 80), remaining == 0 ? "探索完成" : "剩余 " + FormatCountdown(remaining), 34, new Color(.60f, .95f, .83f), TextAnchor.MiddleCenter);
                Label(new Rect(160, 665, 760, 100), "完整收益 " + Progression.ExpeditionCoins(expedition.durationMinutes, expedition.bonusPercent) + " 金币 + " + Progression.ExpeditionExperience(expedition.durationMinutes) + " 经验\n每日探洞经验剩余 " + (20 - p.dailyExpeditionXp), 25, new Color(.72f, .85f, .88f), TextAnchor.MiddleCenter);
                if (SmallButton(new Rect(240, 805, 600, 105), remaining == 0 ? "领取收获" : "提前带回（按时间结算）", new Color(.10f, .55f, .46f), 27))
                {
                    string id = expedition.id;
                    Mutate("expedition/claim", new ApiRequest { id = id }, reply =>
                    {
                        if (reply.ok) { frontNotice = "探洞收获：金币 +" + reply.coins + "  经验 +" + reply.experience + (reply.fragments > 0 ? "  碎片 +" + reply.fragments : ""); lastRewardId = id; lastRewardKind = "cave"; lastRewardCoins = reply.baseCoins; }
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
                    Label(new Rect(card.x + 35, card.y + 95, 545, 100), "约 " + Progression.ExpeditionCoins(Progression.CaveMinutes[i], Progression.CaveBonusPercent(p)) + " 金币 + " + Progression.ExpeditionExperience(Progression.CaveMinutes[i]) + " 经验\n碎片 " + fragments + " · " + Progression.CaveMilestone(fragments), 23, new Color(.68f, .86f, .87f), TextAnchor.MiddleLeft);
                    if (SmallButton(new Rect(card.x + 610, card.y + 75, 230, 95), "派遣", new Color(.10f, .55f, .46f), 30)) Mutate("expedition/start", new ApiRequest { index = index });
                }
                Label(new Rect(100, 1210, 880, 100), "同时仅可探索一个洞穴，最多累计 8 小时。\n每日探洞经验剩余 " + (20 - p.dailyExpeditionXp), 24, new Color(.77f, .88f, .88f), TextAnchor.MiddleCenter);
                DrawRewardOffer(new Rect(200, 1340, 680, 95));
            }
            Label(new Rect(100, 1530, 880, 100), frontNotice, 24, new Color(.80f, .91f, .91f), TextAnchor.MiddleCenter);
        }

        private void OpenBoard()
        {
            frontPage = "leaderboard"; board = null;
            if (!api.Connected) { frontNotice = "连接后可查看真人排行"; return; }
            api.Get("challenge/leaderboard", reply => { board = reply.ok ? reply : null; frontNotice = reply.ok ? "" : reply.error; });
        }

        private void DrawLeaderboard()
        {
            FrontHeader("每日挑战榜", "真实玩家 · 统一规则 · 成绩经回放验证");
            Panel(new Rect(80, 315, 920, 1110), new Color(.025f, .08f, .14f, .94f));
            if (board == null) Label(new Rect(120, 600, 840, 100), string.IsNullOrEmpty(frontNotice) ? "正在加载…" : frontNotice, 31, Color.white, TextAnchor.MiddleCenter);
            else
            {
                string position = board.rank > 0 ? "今日第 " + board.rank + " 名 / " + board.total + " 人" : "完成挑战后参与排名";
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
                GamePlatform.ShareChallenge(api.Date, message => { if (!string.IsNullOrEmpty(message)) frontNotice = message; });
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
                if (!prepared.ok) return;
                adShowing = true;
                GamePlatform.ShowRewarded(api, (completed, error) =>
                {
                    adShowing = false;
                    if (!completed) { frontNotice = error; return; }
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
