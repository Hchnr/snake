using System;
using System.Text;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    // Protected controlled match, used only for cross-runtime diagnostics.
    public static class FreeBoostVector
    {
        public static string Compute()
        {
            var world = new PveFixedWorld(11, false, null, true,
                localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
                localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
                localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true,
                localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
                localOpeningBoost: true, localFreeBoost: true);
            foreach (var fighter in world.Fighters) fighter.shieldTicks = 10000;
            var output = new StringBuilder("v14-steps=");
            for (int tick = 1; tick <= 230; tick++)
            {
                bool boosting = tick <= 80 || tick > 100;
                world.Step(0, boosting, true);
                if (world.Finished || world.Tick != tick || !world.Fighters[0].alive) throw new InvalidOperationException("Fixture finished early");
                int expected = tick <= 80 ? tick : tick <= 100 ? 80 : Math.Min(160, tick - 20);
                if (world.FreeBoostTicksUsed != expected) throw new InvalidOperationException("Unexpected free boost use at tick " + tick);
                ulong hash = 14695981039346656037UL;
                void Add(int value) { unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; } } }
                void AddText(string value) { if (value == null) { Add(-1); return; } Add(value.Length); foreach (char c in value) Add(c); }
                AddText(StrengthPveReplayVector.HiddenFingerprint(world));
                Add(world.Tick); Add(world.FreeBoostTicksUsed); Add(world.LastFreeBoostTick);
                foreach (var fighter in world.Fighters)
                {
                    AddText(fighter.id); Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks);
                    Add(fighter.motion.Angle); Add(fighter.motion.Length);
                    foreach (var segment in fighter.motion.Segments) { Add(segment.x); Add(segment.y); }
                }
                if (tick != 1) output.Append(',');
                output.Append(hash.ToString("x16"));
            }
            if (world.FreeBoostTicksUsed != 160 || world.LastFreeBoostTick != 180) throw new InvalidOperationException("Fixture missed exhaustion");
            return output.ToString();
        }
    }
}
