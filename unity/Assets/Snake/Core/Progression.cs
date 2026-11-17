#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    [Serializable]
    public sealed class ExpeditionState
    {
        public string id = "";
        public int cave;
        public long startedAt;
        public long endedAt;
        public int durationMinutes;
        public int bonusPercent;
        public bool claimed;
        public int coins, experience, fragments;
    }

    [Serializable]
    public sealed class PlayerProgress
    {
        public string playerId = "";
        public string displayName = "玩家";
        public int experience;
        public int coins;
        public bool tutorialComplete;
        public int completedMatches;
        public int selectedSkin;
        public List<int> ownedSkins = new List<int> { 0 };
        public int[] talents = new int[9];
        public string dailyKey = "";
        public int dailyMatches, dailyRewardedMatches, dailyExpeditionXp;
        public bool dailyQuestClaimed, dailyTrialUsed;
        public int trialSkin = -1;
        public string trialRunId = "";
        public int[] caveFragments = new int[3];
        public ExpeditionState expedition;
    }

    // Platform callbacks can be missing, duplicated, late, or throw. Only the
    // first result may start an exchange or complete the login attempt.
    public static class PlatformLoginFlow
    {
        public static void Start(Action<Action<string, string>, Action> request,
            Action<Action> scheduleTimeout, Action<string, string> exchange, Action<string> fail)
        {
            int resolved = 0;
            bool Claim() => System.Threading.Interlocked.CompareExchange(ref resolved, 1, 0) == 0;
            void Failure(string message) { if (Claim()) fail(message); }
            try
            {
                request((code, anonymousCode) =>
                {
                    if (!Claim()) return;
                    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(anonymousCode))
                    { fail("抖音登录没有返回有效凭证"); return; }
                    try { exchange(code, anonymousCode); }
                    catch (Exception) { fail("抖音登录请求失败"); }
                }, () => Failure("抖音登录失败"));
            }
            catch (Exception) { Failure("抖音登录暂不可用"); }
            if (System.Threading.Volatile.Read(ref resolved) != 0) return;
            try { scheduleTimeout(() => Failure("抖音登录超时")); }
            catch (Exception) { Failure("抖音登录暂不可用"); }
        }
    }

    // Submission retries use monotonic elapsed time supplied by the client;
    // stored run ownership is checked before any network request is sent.
    public sealed class PendingRetrySchedule
    {
        public int Failures { get; private set; }
        public double NextAttempt { get; private set; }
        public bool Due(double now) => now >= NextAttempt;
        public void Reset() { Failures = 0; NextAttempt = 0; }
        public void Fail(double now)
        {
            Failures = Math.Min(Failures + 1, 6);
            NextAttempt = now + Math.Min(60, 1 << Failures);
        }
        public static bool OwnedBy(string owner, string player) =>
            !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(player) && string.Equals(owner, player, StringComparison.Ordinal);
    }

    // A transient error for one saved run must not block other runs. Retry
    // state is per account and run; the durable queue remains the authority.
    public sealed class PendingSettlementSchedule
    {
        private readonly Dictionary<string, PendingRetrySchedule> retries = new Dictionary<string, PendingRetrySchedule>();
        private static string Key(string playerId, string runId) => playerId + "\0" + runId;
        public bool Due(string playerId, string runId, double now)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(runId)) return false;
            return !retries.TryGetValue(Key(playerId, runId), out var retry) || retry.Due(now);
        }
        public void Fail(string playerId, string runId, double now)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(runId)) return;
            string key = Key(playerId, runId);
            if (!retries.TryGetValue(key, out var retry)) retries[key] = retry = new PendingRetrySchedule();
            retry.Fail(now);
        }
        public void Remove(string playerId, string runId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(runId)) retries.Remove(Key(playerId, runId));
        }
    }

    public static class Progression
    {
        public static readonly string[] SkinNames = Array.ConvertAll(SkinCatalog.All, s => s.Name);
        public static readonly string[] SkinDescriptions = Array.ConvertAll(SkinCatalog.All, s => s.Description);
        public static readonly int[] SkinPrices = Array.ConvertAll(SkinCatalog.All, s => s.Price);
        public static readonly string[] TalentNames = { "灵动转向", "轻盈加速", "果实感知", "临墙应变", "短身节流", "回弹控制", "矿脉采集", "任务收获", "梦游收获" };
        public static readonly string[] TalentDescriptions = {
            "转向 +0.5% / 级", "加速消耗 -0.5% / 级", "果实拾取 +0.5% / 级",
            "靠墙转向 +0.5% / 级", "短身加速消耗 -0.5% / 级", "回弹后转向 +0.5% / 级",
            "探洞金币 +1% / 级", "任务金币 +1% / 级", "探洞金币 +1% / 级"
        };
        public static readonly string[] CaveNames = { "浅晶洞", "风纹洞", "星髓洞" };
        public static readonly int[] CaveMinutes = { 120, 240, 480 };

        public static double Growth(int experience)
        {
            double u = Math.Max(0, experience) / 100.0;
            return u <= 7 ? u : 7 + 4 * Math.Log(1 + (u - 7) / 4);
        }

        public static int Level(int experience) => 1 + (int)Math.Floor(Growth(experience) + 1e-9);

        public static int ExperienceForLevel(int level)
        {
            if (level <= 1) return 0;
            double target = level <= 8 ? 100.0 * (level - 1) : 100.0 * (7 + 4 * (Math.Exp((level - 8) / 4.0) - 1));
            return target >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(target - 1e-9);
        }

        public static int AvailablePoints(PlayerProgress profile)
        {
            int spent = 0;
            if (profile.talents != null) foreach (int rank in profile.talents) spent += Math.Max(0, rank);
            return Math.Max(0, Level(profile.experience) - 1 + (profile.tutorialComplete ? 1 : 0) - spent);
        }

        public static bool Allocate(PlayerProgress profile, int index)
        {
            if (index < 0 || index >= TalentNames.Length || profile.talents == null || profile.talents.Length != 9 || profile.talents[index] >= 3 || AvailablePoints(profile) == 0) return false;
            profile.talents[index]++;
            return true;
        }

        // Spread the available points across the basic movement and exploration
        // nodes before ranking any one of them up. Players can reset for free.
        public static int AllocateRecommended(PlayerProgress profile)
        {
            if (profile == null || profile.talents == null || profile.talents.Length != TalentNames.Length) return 0;
            int[] order = { 0, 1, 2, 6, 7, 8, 3, 4, 5 };
            int allocated = 0;
            for (int rank = 0; rank < 3; rank++)
                foreach (int index in order)
                    if (profile.talents[index] == rank && Allocate(profile, index)) allocated++;
            return allocated;
        }

        public static CombatBonuses Bonuses(PlayerProgress profile, bool competitive = false)
        {
            var result = new CombatBonuses();
            if (profile == null) return result;
            int[] ranks = profile.talents != null && profile.talents.Length == 9 ? profile.talents : new int[9];
            float each = competitive ? .0025f : .005f;
            float cap = competitive ? .03f : .06f;
            int points = 0;
            for (int i = 0; i < 6; i++) points += Math.Max(0, Math.Min(3, ranks[i]));
            float scale = points * each > cap ? cap / (points * each) : 1f;
            result.turn = ranks[0] * each * scale;
            result.boostSaving = ranks[1] * each * scale;
            result.pickup = ranks[2] * each * scale;
            result.nearWallTurn = ranks[3] * each * scale;
            result.shortBoostSaving = ranks[4] * each * scale;
            result.reboundTurn = ranks[5] * each * scale;
            switch (profile.selectedSkin)
            {
                case 0: result.turn += .01f; break;
                case 1: result.boostSaving += .01f; break;
                case 2: result.pickup += .01f; break;
            }
            return result;
        }

        public static int CaveBonusPercent(PlayerProgress profile)
        {
            if (profile == null || profile.talents == null || profile.talents.Length != 9) return 0;
            return Math.Max(0, Math.Min(3, profile.talents[6])) + Math.Max(0, Math.Min(3, profile.talents[8])) + (profile.selectedSkin == 3 ? 1 : 0);
        }

        public static int TaskBonusPercent(PlayerProgress profile) => profile != null && profile.talents != null && profile.talents.Length == 9 ? Math.Max(0, Math.Min(3, profile.talents[7])) : 0;

        public static string CaveMilestone(int count) => count >= 7 ? "秘境精通" : count >= 3 ? "探路达人" : count >= 1 ? "初探秘境" : "尚未发现";

        private static readonly int[] CaveStoryThresholds = { 1, 3, 7 };
        private static readonly string[,] CaveStories = {
            { "浅晶在晨光中醒来。", "晶石记住了每次出发。", "回家的路一直闪着光。" },
            { "风纹指向温暖的洞口。", "风把远方的故事带来。", "熟悉的风会陪你归来。" },
            { "星髓藏着一粒微光。", "星光照亮梦中的足迹。", "每次重逢都是新的星图。" }
        };

        public static int CaveStoryThreshold(int chapter)
            => chapter >= 0 && chapter < CaveStoryThresholds.Length ? CaveStoryThresholds[chapter] : 0;

        // Stories are derived from already-persisted fragments; viewing them never consumes inventory.
        public static string CaveStory(int cave, int chapter, int fragments)
        {
            if (cave < 0 || cave >= CaveNames.Length || chapter < 0 || chapter >= CaveStoryThresholds.Length) return "";
            return fragments >= CaveStoryThresholds[chapter] ? CaveStories[cave, chapter] : "收集 " + CaveStoryThresholds[chapter] + " 碎片解锁";
        }

        public static int ExpeditionCoins(int minutes, int bonusPercent)
            => (Math.Max(0, Math.Min(480, minutes)) / 2) * (100 + Math.Max(0, bonusPercent)) / 100;

        public static int ExpeditionExperience(int minutes)
            => Math.Max(0, Math.Min(480, minutes)) * 5 / 120;
    }
}
