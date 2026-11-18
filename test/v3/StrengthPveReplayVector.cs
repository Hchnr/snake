using System;
using System.Text;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    // Mutable, non-authoritative prototype diagnostic, not a release ruleset.
    // Hashes observable integer state on every step; hidden state and arbitrary
    // seeds/inputs still require independent verification before any rewards.
    public static class StrengthPveReplayVector
    {
        private static ulong hash;
        private static void Add(int value)
        {
            unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; } }
        }
        private static void Add(string value)
        {
            if (value == null) { Add(-1); return; }
            Add(value.Length); foreach (char c in value) Add(c);
        }
        // Fingerprint of observable terminal state only, used by diagnostics.
        public static string Terminal(PveFixedWorld world)
        {
            if (world == null || !world.StrengthPreview || !world.Finished) throw new ArgumentException("Expected a completed strength preview");
            hash = 14695981039346656037UL;
            Add(world.Tick); Add(world.Winner); Add(world.StrengthPickupVisible ? 1 : 0);
            Add(world.StrengthPickupPosition.x); Add(world.StrengthPickupPosition.y);
            Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
            Add(world.LastPlayerBlockedTick); Add(world.LastPlayerHeadTieTick);
            foreach (var fighter in world.Fighters)
            {
                Add(fighter.id); Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks); Add(fighter.kills);
                Add(fighter.eliminatedBy); Add(fighter.eliminationReason); Add(fighter.motion.Angle);
                Add(fighter.motion.Length); Add(fighter.motion.ReboundTicks); Add(fighter.motion.ReboundCooldown);
                Add(fighter.motion.Boosting ? 1 : 0); Add(world.StrengthBuffTicks(fighter.id)); Add(world.StrengthLoopTicks(fighter.id));
                bool hasLevel = world.TryGetStrengthLevelProgress(fighter.id, out int level, out int xp, out int required);
                Add(hasLevel ? 1 : 0); Add(level); Add(xp); Add(required);
                bool hasPower = world.TryGetStrength(fighter.id, out var power, out int tail);
                Add(hasPower ? 1 : 0); Add(power.attack); Add(power.defense); Add(tail);
                foreach (var part in fighter.motion.Segments) { Add(part.x); Add(part.y); }
            }
            foreach (var fruit in world.Fruit) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
            return hash.ToString("x16");
        }
        public static string Compute()
        {
            var result = new StringBuilder("strength=");
            foreach (int seed in new[] { 1, 73 })
            foreach (bool first in new[] { false, true })
            for (int pattern = 0; pattern < 3; pattern++)
            {
                hash = 14695981039346656037UL;
                var snapshot = pattern == 2 ? StrengthEvalStartConfig.Capture(new PlayerProgress { experience = 700, tutorialComplete = !first }) : null;
                var world = new PveFixedWorld(seed, first, null, strengthPreview: true, strengthSnapshot: snapshot);
                int flags = 0;
                while (!world.Finished)
                {
                    int tick = world.Tick;
                    byte heading = pattern == 0 ? (byte)0 : pattern == 1 ? (byte)(tick / 53 * 37 + seed) : (byte)(tick * 17);
                    bool boost = pattern == 1 ? tick % 120 >= 95 : pattern == 2 && tick % 30 < 15;
                    world.Step(heading, boost);
                    Add(world.Tick); Add(world.Finished ? 1 : 0); Add(world.Winner);
                    Add(world.StrengthPickupVisible ? 1 : 0); Add(world.StrengthPickupPosition.x); Add(world.StrengthPickupPosition.y);
                    Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
                    Add(world.LastPlayerBlockedTick); Add(world.LastPlayerHeadTieTick);
                    if (world.StrengthPickupVisible) flags |= 1;
                    if (world.LastPlayerAttackTick >= 0 || world.LastPlayerDamageTick >= 0) flags |= 2;
                    foreach (var fighter in world.Fighters)
                    {
                        Add(fighter.id); Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks); Add(fighter.kills);
                        Add(fighter.eliminatedBy); Add(fighter.eliminationReason); Add(fighter.motion.Angle);
                        Add(fighter.motion.Length); Add(fighter.motion.ReboundTicks); Add(fighter.motion.ReboundCooldown);
                        Add(fighter.motion.Boosting ? 1 : 0); Add(world.StrengthBuffTicks(fighter.id)); Add(world.StrengthLoopTicks(fighter.id));
                        bool hasLevel = world.TryGetStrengthLevelProgress(fighter.id, out int level, out int xp, out int required);
                        Add(hasLevel ? 1 : 0); Add(level); Add(xp); Add(required);
                        bool hasPower = world.TryGetStrength(fighter.id, out var power, out int tail);
                        Add(hasPower ? 1 : 0); Add(power.attack); Add(power.defense); Add(tail);
                        if (world.StrengthBuffTicks(fighter.id) > 0) flags |= 4;
                        if (world.StrengthLoopTicks(fighter.id) > 0) flags |= 8;
                        if (level > 0) flags |= 16;
                        foreach (var part in fighter.motion.Segments) { Add(part.x); Add(part.y); }
                    }
                    foreach (var fruit in world.Fruit) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
                }
                if (result.Length > 9) result.Append(',');
                result.Append(seed).Append(':').Append(first ? 1 : 0).Append(':').Append(pattern).Append(':')
                    .Append(world.Tick).Append(':').Append(flags).Append(':').Append(hash.ToString("x16"));
            }
            return result.ToString();
        }
    }
}
