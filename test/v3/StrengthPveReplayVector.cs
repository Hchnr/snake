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
            foreach (int seed in new[] { 1, 73 })
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
                    Add(world.StrengthPickupVisible ? 1 : 0); Add(world.StrengthPickupPosition.x); Add(world.StrengthPickupPosition.y);
                    Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
                    Add(world.LastPlayerBlockedTick); Add(world.LastPlayerHeadTieTick);
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
    }
}
