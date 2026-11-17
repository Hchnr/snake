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

        public static string BonusSummary(int shield, int rebound, int growth)
        {
            return "当前构筑：收圈护盾 " + Math.Min(2, Math.Max(0, shield)) * 2 + " 秒 · 反弹免费加速 " +
                Math.Min(2, Math.Max(0, rebound)) * 3 + " 秒 · 淘汰增长 " + Math.Min(2, Math.Max(0, growth)) * 3 + " 节";
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
