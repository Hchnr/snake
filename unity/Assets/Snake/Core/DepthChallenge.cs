#nullable disable
using System;

namespace SnakeClub.Core
{
    public static class DepthChallenge
    {
        public const int RoomCount = 3;
        public static int CompletedMatches(PlayerProgress profile)
            => profile == null ? 0 : Math.Max(profile.completedMatches, profile.tutorialComplete ? 1 : 0);
        public static bool IsUnlocked(PlayerProgress profile) => CompletedMatches(profile) >= 3;

        // Growth is a high-risk choice: each level trades one second of
        // the next room's normal spawn protection for extra elimination growth.
        public static float SpawnProtection(int growth)
            => Arena.SpawnShieldSeconds - Math.Min(2, Math.Max(0, growth));

        public static string BonusSummary(int shield, int rebound, int growth)
        {
            return "当前构筑：收圈护盾 " + Math.Min(2, Math.Max(0, shield)) * 2 + " 秒 · 反弹免费加速 " +
                Math.Min(2, Math.Max(0, rebound)) * 3 + " 秒 · 淘汰增长 " + Math.Min(2, Math.Max(0, growth)) * 3 + " 节\n出生保护 " + SpawnProtection(growth) + " 秒";
        }

        public enum RoomStatus { InProgress, Won, Lost }

        public static string ReboundBoostHint(SnakeState player)
        {
            if (player == null || !player.alive) return "";
            if (player.reboundBoostTime > 0) return "免费加速中 · " + (int)Math.Ceiling(player.reboundBoostTime) + " 秒";
            if (!player.reboundBoostReady) return "";
            return player.reboundBoostWaitRelease ? "反弹蓄能 · 松开后再次按住加速" : "反弹蓄能 · 下次加速免消耗";
        }

        // Depth star holding lasts ten seconds; boss has no timed event countdown.
        public static string ObjectiveHint(int room, ArenaState world, string playerId)
        {
            if (world == null || world.phase != "running") return "";
            if (room == 2)
            {
                if (!world.starVisible) return "星核即将出现 · 靠近自动拾取";
                if (string.IsNullOrEmpty(world.starHolder)) return "星核出现 · 靠近自动拾取";
                if (world.starHolder != playerId) return "对手携带星核 · 围猎后争夺";
                int remaining = (int)Math.Ceiling(Math.Max(0f, Math.Min(1f, 1f - world.starProgress)) * 10f);
                return "守住星核 · 还需 " + remaining + " 秒";
            }
            if (room == 3)
            {
                var boss = world.snakes.Find(s => s.boss && s.alive);
                if (boss == null) return "";
                return boss.bossWindup > 0 ? "首领蓄力！躲开方向线 · 护盾 " + boss.bossShield :
                    "首领护盾 " + boss.bossShield + " · 拦截或收圈";
            }
            return "";
        }

        public static string BonusChoice(int choice, int shield, int rebound, int growth)
        {
            int current;
            switch (choice)
            {
                case 0:
                    current = Math.Min(2, Math.Max(0, shield));
                    return "收圈护盾 " + current * 2 + " → " + Math.Min(2, current + 1) * 2 + " 秒 · 捕获后生效";
                case 1:
                    current = Math.Min(2, Math.Max(0, rebound));
                    return "反弹免费加速 " + current * 3 + " → " + Math.Min(2, current + 1) * 3 + " 秒\n下次使用 · 松开结束 · 8 秒冷却";
                case 2:
                    current = Math.Min(2, Math.Max(0, growth));
                    return "淘汰额外增长 " + current * 3 + " → " + Math.Min(2, current + 1) * 3 + " 节\n出生保护 " + SpawnProtection(current) + " → " + SpawnProtection(current + 1) + " 秒";
                default: return "";
            }
        }

        public static RoomStatus Evaluate(int room, ArenaState world, string playerId)
        {
            if (world == null || room < 1 || room > RoomCount || string.IsNullOrEmpty(playerId)) return RoomStatus.InProgress;
            var player = world.snakes.Find(s => s.id == playerId);
            if (player == null) return RoomStatus.InProgress;
            if (!player.alive) return RoomStatus.Lost;
            if (GoalReached(room, world, playerId)) return RoomStatus.Won;
            if (room == 3)
            {
                var boss = world.snakes.Find(s => s.id == "bot-0");
                if (boss != null && !boss.alive) return RoomStatus.Lost;
            }
            return world.phase == "ended" ? RoomStatus.Lost : RoomStatus.InProgress;
        }

        public static string FailureReason(int room, ArenaState world, string playerId)
        {
            if (Evaluate(room, world, playerId) != RoomStatus.Lost) return "";
            var player = world.snakes.Find(s => s.id == playerId);
            if (player != null && !player.alive)
            {
                string reason = MatchOutcome.Explain(world, playerId);
                return string.IsNullOrEmpty(reason) ? "你已出局，房间目标未完成" : reason + "，房间目标未完成";
            }
            if (room == 3)
            {
                var boss = world.snakes.Find(s => s.id == "bot-0");
                if (boss != null && !boss.alive && boss.eliminatedBy != playerId) return "首领被其他对手击败，目标未完成";
            }
            return world.remaining <= 0 ? "时间到，房间目标未完成" : "房间结束，目标未完成";
        }

        public static bool GoalReached(int room, ArenaState world, string playerId)
        {
            if (world == null || room < 1 || room > RoomCount) return false;
            var player = world.snakes.Find(s => s.id == playerId);
            if (player == null || !player.alive) return false;
            if (room == 1) return player.kills >= 1;
            if (room == 2) return player.starPoints >= 1;
            var boss = world.snakes.Find(s => s.id == "bot-0");
            return boss != null && !boss.alive && boss.eliminatedBy == playerId;
        }
    }
}
