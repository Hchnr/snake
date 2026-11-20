using System;
using System.Text;
using SnakeClub.Core;
namespace SnakeClub.Tests
{
    // Development diagnostic only; this samples one deterministic scenario.
    public static class OpeningFruitVector
    {
        public static string Compute()
        {
            var world = new PveFixedWorld(11, false, null, true,
                localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
                localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
                localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true,
                localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
                localOpeningBoost: true, localFreeBoost: true, localOpeningDefense: true, localOpeningFruit: true);
            var output = new StringBuilder("v16-steps=");
            bool sawAttraction = false;
            for (int tick = 1; tick <= 400; tick++)
            {
                world.Step(0, tick <= 80 || tick > 100, true);
                if (world.Finished || world.Tick != tick) throw new InvalidOperationException("Fixture finished before tick 400");
                if (!world.TryGetStrength("you", out var power, out int tail)) throw new InvalidOperationException("No player power");
                if (tick == 300 && world.OpeningBoostRemaining != 1 || tick == 301 && world.OpeningBoostRemaining != 0)
                    throw new InvalidOperationException("Opening boost boundary mismatch");
                ulong hash = 14695981039346656037UL;
                void Add(int value) { unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; } } }
                void AddText(string value) { if (value == null) { Add(-1); return; } Add(value.Length); foreach (char c in value) Add(c); }
                AddText(StrengthPveReplayVector.HiddenFingerprint(world));
                Add(world.Tick); Add(power.attack); Add(power.defense); Add(tail); Add(world.OpeningBoostRemaining); Add(world.CurrentPlayerHardness); Add(world.FreeBoostTicksUsed);
                Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
                Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
                foreach (var fighter in world.Fighters)
                {
                    AddText(fighter.id); Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks); Add(fighter.kills);
                    Add(fighter.motion.Angle); Add(fighter.motion.Length);
                    foreach (var segment in fighter.motion.Segments) { Add(segment.x); Add(segment.y); }
                }
                for (int i = 0; i < world.Fruit.Count; i++)
                {
                    var fruit = world.Fruit[i]; bool attracted = world.IsOpeningFruitAttracted(i);
                    sawAttraction |= attracted; Add(attracted ? 1 : 0);
                    Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color);
                }
                if (tick != 1) output.Append(',');
                output.Append(hash.ToString("x16"));
            }
            if (!sawAttraction) throw new InvalidOperationException("Fixture missed actual fruit movement");
            return output.ToString();
        }
    }
}
