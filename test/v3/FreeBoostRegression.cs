using System;
using System.Collections.Generic;
using System.Reflection;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    public static class FreeBoostRegression
    {
        static PveFixedWorld World(int seed = 1, bool free = true) => new PveFixedWorld(seed, false, null, true,
            localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true,
            localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true,
            localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
            localOpeningBoost: true, localFreeBoost: free);

        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        static PveFixedWorld Controlled(bool free, int length = 16)
        {
            var world = World(free: free);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
            fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, length)) { shieldTicks = 10000 };
            fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(70000, 70000, 0, 16)) { shieldTicks = 10000 };
            for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
            ((List<PveFixedFruit>)typeof(PveFixedWorld).GetField("fruit", flags).GetValue(world)).Clear();
            return world;
        }

        public static void Run()
        {
            var current = Controlled(true); var legacy = Controlled(false);
            Check(current.FreeBoostRemaining == 160 && legacy.FreeBoostRemaining == 0, "initial and legacy allowances");
            for (int i = 0; i < 4; i++) { current.Step(0, false, true); legacy.Step(0, false, true); }
            Check(current.FreeBoostRemaining == 160 && current.LastFreeBoostTick == -1, "unboosted movement spent allowance");
            for (int i = 1; i <= 160; i++)
            {
                current.Step(0, true, true); legacy.Step(0, true, true);
                Check(!current.Finished && !legacy.Finished, "controlled fixture ended early");
                Check(current.Fighters[0].motion.X == legacy.Fighters[0].motion.X &&
                    current.Fighters[0].motion.Y == legacy.Fighters[0].motion.Y &&
                    current.Fighters[0].motion.Angle == legacy.Fighters[0].motion.Angle, "free boost changed speed or turning");
                Check(current.FreeBoostRemaining == 160 - i, "actual use did not consume exactly one tick");
                if (i == 1) Check(StrengthBoostNotice.Text(current, current.RenderState(), "you", true, false).Contains("剩余 8 秒"), "active allowance missing");
            }
            Check(current.Fighters[0].motion.Length == 16 && legacy.Fighters[0].motion.Length == 13, "free steps did not preserve the expected body");
            Check(StrengthBoostNotice.Text(current, current.RenderState(), "you", true, false).Contains("已用完"), "final free step was not explained");
            Check(StrengthBoostNotice.Text(current, current.RenderState(), "you", false, false) == "" &&
                StrengthBoostNotice.Text(current, current.RenderState(), "you", true, true) == "", "notice persisted when released or suspended");
            for (int i = 0; i < 50; i++) current.Step(0, true, true);
            Check(current.Fighters[0].motion.Length == 15 && current.FreeBoostRemaining == 0, "ordinary cost did not resume after allowance");
            Check(StrengthBoostNotice.Text(current, current.RenderState(), "you", true, false) == "", "expired notice did not clear");

            var shortWorld = Controlled(true, PveFixedMotion.MinimumLength);
            shortWorld.Step(0, true, true);
            Check(shortWorld.FreeBoostRemaining == 160 && shortWorld.LastFreeBoostTick == -1 &&
                StrengthBoostNotice.Text(shortWorld, shortWorld.RenderState(), "you", true, false).Contains("吃果实恢复"), "minimum length consumed allowance or hid restriction");
            shortWorld.Fighters[0].motion.Grow(1); shortWorld.Step(0, true, true);
            Check(shortWorld.FreeBoostRemaining == 159, "growth did not restore actual boosting");
            shortWorld.Fighters[0].alive = false;
            Check(StrengthBoostNotice.Text(shortWorld, shortWorld.RenderState(), "you", true, false) == "", "dead player retained notice");
            Check(StrengthTutorialHints.Select(80, true, false, true, true).Contains("前8秒免耗") &&
                !StrengthTutorialHints.Select(80, true, false, true).Contains("免耗"), "tutorial did not isolate the privilege");
            Check(World().FreeBoostRemaining == 160, "new match did not reset allowance");

            bool rejected = false;
            try { _ = new PveFixedWorld(1, false, null, true, localFreeBoost: true); } catch (ArgumentException) { rejected = true; }
            Check(rejected && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, localFreeBoost: true), "invalid version combination accepted");
            for (int seed = 1; seed <= 8; seed++)
            {
                var match = World(seed); var inputs = new List<byte>();
                while (!match.Finished)
                {
                    byte heading = (byte)((match.Tick / 53 * 37 + seed) & 255);
                    bool boost = match.Tick % 120 < 50;
                    inputs.Add(heading); inputs.Add((byte)((boost ? 1 : 0) | 2)); match.Step(heading, boost, true);
                }
                Check(PveFixedRecording.TryReplayLocalStrength(PveFixedRecording.LocalStrengthFreeBoostRules, seed, false, inputs.ToArray(), out var replay) &&
                    StrengthStateDigest.Compute(match) == StrengthStateDigest.Compute(replay) && match.FreeBoostTicksUsed == replay.FreeBoostTicksUsed,
                    "v14 complete recording did not reproduce: " + seed);
                Check(!PveFixedRecording.TryReplayLocalStrength("strength-local-advantage-15", seed, false, inputs.ToArray(), out _), "unknown rules accepted");
            }
        }
    }
}
