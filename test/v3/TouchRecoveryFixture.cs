using System;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    // The browser fixture holds its disposable simulation at tick 9 until an
    // actual upward drag, so the first recorded turn occurs on tick 10.
    public static class TouchRecoveryFixture
    {
        public static void Run()
        {
            foreach (int offset in new[] { 1, 3, 6 })
            foreach (int duration in new[] { 1, 5, 10 })
            {
                var world = new PveFixedWorld(1016, true, null, true,
                    localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
                    localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
                    localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true,
                    localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
                    localOpeningBoost: true, localFreeBoost: true, localOpeningDefense: true, localOpeningFruit: true);
                while (!world.Finished && world.Tick < 1200 && world.RecoverySuppliesUsed == 0)
                {
                    int tick = world.Tick + 1;
                    world.Step((byte)(tick >= 10 ? 64 : 0),
                        tick >= 10 + offset && tick < 10 + offset + duration, tick >= 10);
                }
                bool marked = false;
                for (int i = 0; i < world.Fruit.Count; i++) marked |= world.IsRecoveryFruit(i);
                if (world.RecoverySuppliesUsed != 1 || world.LastRecoverySupplyTick != world.LastPlayerDamageTick ||
                    world.PlayerSegmentsLostToBites <= 0 || !marked ||
                    !RecoverySupplyNotice.Text(world, world.Tick, false).Contains("回追补给"))
                    throw new Exception($"Recovery fixture did not reach a visible actual-bite supply: offset={offset}, duration={duration}");
            }
        }
    }
}
