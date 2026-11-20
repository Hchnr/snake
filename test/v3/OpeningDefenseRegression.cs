using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    public static class OpeningDefenseRegression
    {
        private static PveFixedWorld World(int seed, bool first, bool defense = true) => new PveFixedWorld(seed, first, null, true,
            localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true,
            localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true,
            localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
            localOpeningBoost: true, localFreeBoost: true, localOpeningDefense: defense);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Tick(PveFixedWorld world, int tick) =>
            typeof(PveFixedWorld).GetProperty("Tick").GetSetMethod(true).Invoke(world, new object[] { tick });

        public static void Run()
        {
            var current = World(1, false); var old = World(1, false, false);
            Check(current.RulesVersion == PveFixedRecording.LocalStrengthOpeningDefenseRules &&
                old.RulesVersion == PveFixedRecording.LocalStrengthFreeBoostRules, "practice versions not separated");
            foreach (int tick in new[] { 0, 1, 239, 240, 241, 270, 299, 300, 301, 320 })
            {
                Tick(current, tick); Tick(old, tick);
                int expected = tick <= 240 ? 250 : tick <= 300 ? 80 + 170 * (300 - tick) / 60 : 80;
                Check(current.CurrentPlayerHardness == expected && old.CurrentPlayerHardness == (tick <= 300 ? 120 : 80), "hardness boundary " + tick);
                Check(current.TryGetStrength("you", out var head, out int tail), "missing player strength");
                var input = new StrengthInputs { basePower = tick <= 300 ? 140 : 120, hardness = expected, length = 16,
                    speed = 300, baseSpeed = 300, randomFactor = 1000, operationFactor = 1000 };
                Check(BodyStrength.TryCalculate(input, 0, out var expectedHead) && BodyStrength.TryCalculate(input, 15, out var expectedTail) &&
                    head.defense == expectedHead.defense && tail == expectedTail.defense, "actual head or tail defense " + tick);
            }
            Tick(current, 240); Check(OpeningBoostNotice.Text(current) == "攻防强化 4秒", "opening countdown before fade");
            Tick(current, 241); Check(OpeningBoostNotice.Text(current) == "防御逐渐降低 3秒", "fade notice start");
            Tick(current, 300); Check(OpeningBoostNotice.Text(current) == "防御逐渐降低 1秒", "fade notice end");
            Tick(current, 301); Check(OpeningBoostNotice.Text(current) == "攻防强化已结束", "expiry notice");
            Tick(old, 241); Check(OpeningBoostNotice.Text(old) == "攻防强化 3秒", "v14 notice changed");
            int previous = 250;
            for (int tick = 241; tick <= 300; tick++) { Tick(current, tick); Check(current.CurrentPlayerHardness <= previous, "nonmonotonic fade"); previous = current.CurrentPlayerHardness; }
            Check(previous == 80, "fade did not reach ordinary defense");

            bool rejected = false;
            try { _ = new PveFixedWorld(1, false, null, true, localOpeningDefense: true); } catch (ArgumentException) { rejected = true; }
            Check(rejected && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, localOpeningDefense: true), "invalid v15 combination accepted");
            foreach (bool first in new[] { false, true })
            for (int seed = 1; seed <= 8; seed++)
            {
                var world = World(seed, first); var inputs = new List<byte>();
                while (!world.Finished)
                {
                    byte heading = (byte)((world.Tick / 53 * 37 + seed) & 255);
                    bool boost = world.Tick % 120 < 50;
                    inputs.Add(heading); inputs.Add((byte)((boost ? 1 : 0) | 2)); world.Step(heading, boost, true);
                }
                Check(PveFixedRecording.TryReplayLocalStrength(PveFixedRecording.LocalStrengthOpeningDefenseRules, seed, first, inputs.ToArray(), out var replay) &&
                    world.Tick == replay.Tick && StrengthStateDigest.Compute(world) == StrengthStateDigest.Compute(replay), "v15 replay mismatch " + seed);
                Check(!PveFixedRecording.TryReplayLocalStrength("strength-local-advantage-17", seed, first, inputs.ToArray(), out _), "unknown rules accepted");
            }

            // Replay one identical actual approach: a small opponent eats the
            // old tail, while the added defense blocks that same contact.
            (int loss, bool blocked) Approach(bool defense)
            {
                var match = World(1, false, defense); var actors = (PveFixedFighter[])match.Fighters;
                actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
                actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-13500, -800, 0, 18));
                for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
                match.Step(0, false, true);
                var combat = (StrengthBodyCombat)typeof(PveFixedWorld).GetField("strengthCombat", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
                return (match.PlayerSegmentsLostToBites, combat.BlockedContacts.Any(c => c.attacker == "bot-1" && c.defender == "you"));
            }
            var oldApproach = Approach(false); var newApproach = Approach(true);
            Check(oldApproach.loss == 1 && newApproach.loss == 0 && newApproach.blocked, "added defense did not change real tail contact");

            // A sufficiently strong opponent can still hurt the player;
            // defense is an advantage, never immunity.
            var collision = World(1, false);
            var fighters = (PveFixedFighter[])collision.Fighters;
            fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
            fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-11700, -400, 49152, 80));
            for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
            collision.Step(0, false, true);
            Check(collision.PlayerSegmentsLostToBites > 0 && collision.LastPlayerDamageTick == collision.Tick, "strong opponent could not bite during advantage");
        }
    }
}
