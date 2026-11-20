using System;
using System.Collections.Generic;
using System.Reflection;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    public static class OpeningFruitRegression
    {
        private static PveFixedWorld World(int seed, bool first, bool fruit = true) => new PveFixedWorld(seed, first, null, true,
            localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true,
            localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true,
            localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
            localOpeningBoost: true, localFreeBoost: true, localOpeningDefense: true, localOpeningFruit: fruit);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Tick(PveFixedWorld world, int tick) =>
            typeof(PveFixedWorld).GetProperty("Tick").GetSetMethod(true).Invoke(world, new object[] { tick });
        private static PveFixedWorld Controlled(bool fruit)
        {
            var world = World(1, false, fruit); var actors = (PveFixedFighter[])world.Fighters;
            actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16)) { shieldTicks = 10000 };
            actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(70000, 70000, 0, 16)) { shieldTicks = 10000 };
            for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
            var points = (List<PveFixedFruit>)world.Fruit;
            for (int i = 0; i < points.Count; i++) points[i] = new PveFixedFruit(new CapturePoint(90000, 90000), i % 4);
            return world;
        }
        private static void Put(PveFixedWorld world, int index, int x, int y, int color = 2) =>
            ((List<PveFixedFruit>)world.Fruit)[index] = new PveFixedFruit(new CapturePoint(x, y), color);

        public static void Run()
        {
            var now = Controlled(true); var old = Controlled(false);
            Check(now.RulesVersion == PveFixedRecording.LocalStrengthOpeningFruitRules &&
                old.RulesVersion == PveFixedRecording.LocalStrengthOpeningDefenseRules, "v15 and v16 rules not separated");
            foreach (var world in new[] { now, old })
            {
                Put(world, 0, 2300, 0); // 2000 away from the player's new head
                Put(world, 1, 2800, 0); // exact attraction boundary
                Put(world, 2, 2801, 0); // one unit outside
                world.Step(0, false, true);
            }
            Check(now.Fruit[0].position.x == 1850 && old.Fruit[0].position.x == 2300 && now.Fruit[0].color == 2,
                "nearby fruit did not move toward the actual player");
            Check(now.Fruit[1].position.x == 2350 && now.Fruit[2].position.x == 2801, "attraction radius boundary");
            Check(now.IsOpeningFruitAttracted(0) && now.IsOpeningFruitAttracted(1) && !now.IsOpeningFruitAttracted(2) &&
                now.RenderState().fruits[0].openingAttracted && !old.RenderState().fruits[0].openingAttracted,
                "visual marker did not follow actual movement");
            Check(!now.IsOpeningFruitAttracted(-1) && !now.IsOpeningFruitAttracted(now.Fruit.Count), "invalid fruit index accepted");
            now.Step(0, false, true); old.Step(0, false, true);
            Check(now.Fighters[0].motion.Length == 17 && old.Fighters[0].motion.Length == 16,
                "moved fruit was not actually collected");
            Check(!now.IsOpeningFruitAttracted(0) && !now.RenderState().fruits[0].openingAttracted,
                "respawned fruit retained an unrelated movement marker");

            var diagonal = Controlled(true); Put(diagonal, 0, 1800, 1000, 3); diagonal.Step(0, false, true);
            var moved = diagonal.Fruit[0].position;
            long dx = moved.x - diagonal.Fighters[0].motion.X, dy = moved.y - diagonal.Fighters[0].motion.Y;
            Check(moved.x < 1800 && moved.y < 1000 && dx * dx + dy * dy < 1500L * 1500 + 1000L * 1000 &&
                diagonal.Fruit[0].color == 3 && diagonal.IsOpeningFruitAttracted(0), "diagonal attraction or color");
            var inPickup = Controlled(true); Put(inPickup, 0, 1400, 0); inPickup.Step(0, false, true);
            Check(inPickup.Fighters[0].motion.Length == 17 && !inPickup.IsOpeningFruitAttracted(0), "ordinary nearby pickup changed");

            var boundary = Controlled(true); Tick(boundary, 299); Put(boundary, 0, 2300, 0); boundary.Step(0, false, true);
            Check(boundary.Tick == 300 && boundary.Fruit[0].position.x == 1850 && boundary.IsOpeningFruitAttracted(0), "last active attraction tick");
            Put(boundary, 0, 2600, 0); boundary.Step(0, false, true);
            Check(boundary.Tick == 301 && boundary.Fruit[0].position.x == 2600 && !boundary.IsOpeningFruitAttracted(0), "attraction did not expire");
            Check(OpeningBoostNotice.Text(boundary) == "吸果与强化已结束", "attraction expiry notice missing");
            Check(OpeningBoostNotice.Text(World(1, false)) == "吸果 · 攻防强化 15秒", "opening fruit notice missing");
            Tick(boundary, 250); Check(OpeningBoostNotice.Text(boundary) == "吸果 · 防御渐弱 3秒", "overlapping fade notice missing");

            // A real attracted fruit remains shared; the closer bot can collect it.
            var contested = Controlled(true); var fighters = (PveFixedFighter[])contested.Fighters;
            fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(2500, 1000, 49152, 16)) { shieldTicks = 10000 };
            Put(contested, 0, 2300, 0); contested.Step(0, false, true);
            Check(fighters[0].motion.Length == 16 && fighters[1].motion.Length == 17 && !contested.IsOpeningFruitAttracted(0),
                "attracted fruit stopped being shared or marked a respawn");
            var dead = Controlled(true); Put(dead, 0, 2300, 0); dead.Step(0, false, true);
            ((PveFixedFighter[])dead.Fighters)[0].alive = false;
            Check(!dead.IsOpeningFruitAttracted(0) && !dead.RenderState().fruits[0].openingAttracted, "dead player retained visual attraction");

            bool rejected = false;
            try { _ = new PveFixedWorld(1, false, null, true, localOpeningFruit: true); } catch (ArgumentException) { rejected = true; }
            Check(rejected && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, localOpeningFruit: true), "invalid v16 combination accepted");
            foreach (bool first in new[] { false, true })
            for (int seed = 1; seed <= 8; seed++)
            {
                var world = World(seed, first); var inputs = new List<byte>();
                while (!world.Finished)
                {
                    byte heading = (byte)((world.Tick / 53 * 37 + seed) & 255); bool boost = world.Tick % 120 < 50;
                    inputs.Add(heading); inputs.Add((byte)((boost ? 1 : 0) | 2)); world.Step(heading, boost, true);
                }
                Check(PveFixedRecording.TryReplayLocalStrength(PveFixedRecording.LocalStrengthOpeningFruitRules, seed, first, inputs.ToArray(), out var replay) &&
                    world.Tick == replay.Tick && StrengthStateDigest.Compute(world) == StrengthStateDigest.Compute(replay), "v16 replay mismatch " + seed);
                Check(!PveFixedRecording.TryReplayLocalStrength("strength-local-advantage-17", seed, first, inputs.ToArray(), out _), "unknown rules accepted");
            }
        }
    }
}
