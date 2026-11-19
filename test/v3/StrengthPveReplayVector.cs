using System;
using System.Text;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    // Mutable, non-authoritative prototype diagnostic, not a release ruleset.
    // Hashes observable state and selected hidden simulation state every step;
    // arbitrary seeds, inputs and platforms still need independent verification.
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
        // Test helper for detecting changes in the diagnostic's hidden inputs.
        public static string HiddenFingerprint(PveFixedWorld world)
        {
            hash = 14695981039346656037UL;
            world.AppendReplayDiagnostics(Add);
            return hash.ToString("x16");
        }
        // Fingerprint of observable terminal state only, used by diagnostics.
        public static string Terminal(PveFixedWorld world) => StrengthStateDigest.Compute(world);
        public static string Compute()
        {
            var result = new StringBuilder("strength=");
            foreach (int seed in new[] { 1, 2, 73 })
            foreach (bool first in new[] { false, true })
            for (int pattern = 0; pattern < 3; pattern++)
            {
                hash = 14695981039346656037UL;
                // Include the clipped-normal corner case in both runtimes,
                // independently of whether these sampled matches reach a wall.
                foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
                {
                    var corner = new CapturePoint(sx * PveFixedMotion.Edge, sy * PveFixedMotion.Edge);
                    var motion = new PveFixedMotion(corner.x, corner.y, 0, 16);
                    motion.SeparateFrom(corner, new CapturePoint(sx * 1800, sy * 1800));
                    Add(motion.X); Add(motion.Y); Add(motion.Angle);
                }
                var snapshot = pattern == 2 ? StrengthEvalStartConfig.Capture(new PlayerProgress { experience = 700, tutorialComplete = !first }) : null;
                var world = new PveFixedWorld(seed, first, null, strengthPreview: true, strengthSnapshot: snapshot);
                int flags = 0;
                while (!world.Finished)
                {
                    int tick = world.Tick;
                    byte heading = pattern == 0 ? (byte)0 : pattern == 1 ? (byte)(tick / 53 * 37 + seed) : (byte)(tick * 17);
                    bool boost = pattern == 1 ? tick % 120 >= 95 : pattern == 2 && tick % 30 < 15;
                    world.Step(heading, boost);
                    world.AppendReplayDiagnostics(Add);
                    Add(world.Tick); Add(world.Finished ? 1 : 0); Add(world.Winner);
                    Add(world.StrengthPickupVisible ? 1 : 0); Add(world.StrengthPickupPending ? 1 : 0); Add(world.StrengthPickupPendingTicks);
                    Add(world.StrengthPickupPosition.x); Add(world.StrengthPickupPosition.y);
                    Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
                    Add(world.LastPlayerBlockedTick); Add(world.LastPlayerHeadTieTick);
                    Add(world.LastPlayerLevelUpTick); Add(world.LastPlayerLevelUp);
                    Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
                    Add(world.LastPlayerAttackPoint.x); Add(world.LastPlayerAttackPoint.y);
                    Add(world.LastPlayerDamagePoint.x); Add(world.LastPlayerDamagePoint.y);
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

        // Separate local-rule vectors preserve the existing server matrix.
        public static string ComputeRecovery() => ComputeRecovery(false);

        public static string ComputeSafeRecovery() => ComputeRecovery(true);

        private static string ComputeRecovery(bool safe)
        {
            string prefix = safe ? "safe-recovery=" : "recovery=";
            var result = new StringBuilder(prefix);
            foreach (int seed in safe ? new[] { 2, 68, 105, 192 } : new[] { 2, 68, 73, 105 })
            foreach (bool first in new[] { false, true })
            for (int pattern = 0; pattern < (safe ? 3 : 2); pattern++)
            {
                hash = 14695981039346656037UL;
                var world = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true,
                    localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: safe);
                int flags = 0; byte heading = pattern == 1 ? (byte)64 : (byte)0;
                while (!world.Finished)
                {
                    if (pattern == 2 && world.Tick % 8 == 0)
                    {
                        var player = world.Fighters[0].motion;
                        long nearest = long.MaxValue; int dx = 0, dy = 0;
                        foreach (var opponent in world.Fighters)
                        {
                            if (opponent.id == "you" || !opponent.alive || opponent.motion.Length <= PveFixedMotion.MinimumLength) continue;
                            var tail = opponent.motion.Segments[opponent.motion.Length - 1];
                            int x = tail.x - player.X, y = tail.y - player.Y; long distance = (long)x * x + (long)y * y;
                            if (distance < nearest) { nearest = distance; dx = x; dy = y; }
                        }
                        if (nearest < long.MaxValue)
                        {
                            long best = long.MinValue;
                            for (int i = 0; i < 256; i++)
                            {
                                long dot = (long)dx * PveFixedMotion.DirectionX((byte)i) + (long)dy * PveFixedMotion.DirectionY((byte)i);
                                if (dot > best) { best = dot; heading = (byte)i; }
                            }
                        }
                    }
                    world.Step(heading, false);
                    world.AppendReplayDiagnostics(Add);
                    Add(world.Tick); Add(world.Finished ? 1 : 0); Add(world.Winner);
                    Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
                    foreach (var actor in world.Fighters)
                    {
                        Add(actor.id); Add(actor.alive ? 1 : 0); Add(actor.shieldTicks); Add(actor.kills);
                        Add(actor.motion.Angle); Add(actor.motion.Length); Add(actor.motion.Boosting ? 1 : 0);
                        foreach (var segment in actor.motion.Segments) { Add(segment.x); Add(segment.y); }
                    }
                    foreach (var fruit in world.Fruit) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
                    if (world.RecoverySuppliesUsed > 0) flags |= 1;
                    if (world.RecoverySupplyCollectedByPlayer > 0) flags |= 2;
                    if (world.RecoverySupplyCollectedByOpponents > 0) flags |= 4;
                    if (world.RecoverySuppliesUsed > 1) flags |= 8;
                }
                if (result.Length > prefix.Length) result.Append(',');
                result.Append(seed).Append(':').Append(first ? 1 : 0).Append(':').Append(pattern).Append(':')
                    .Append(world.Tick).Append(':').Append(flags).Append(':').Append(hash.ToString("x16"));
            }
            return result.ToString();
        }
    }
}
