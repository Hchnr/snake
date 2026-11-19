using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SnakeClub.Core;
namespace SnakeClub.Tests
{
  public static class OpeningBoostRegression
  {
    public static void Run()
    {
      void C(bool ok, string m) { if (!ok) throw new Exception(m); Console.WriteLine("PASS " + m); }
      PveFixedWorld W(bool v13) => new(1, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true, localOpeningBoost: v13);
      var w = W(true); var old = W(false);
      foreach (var world in new[] { w, old }) for (int i = 1; i < world.Fighters.Count; i++) world.Fighters[i].alive = false;
      void Tick(PveFixedWorld x, int t) => typeof(PveFixedWorld).GetProperty("Tick").GetSetMethod(true).Invoke(x, new object[] { t });
      foreach (int t in new[] { 0, 1, 299, 300, 301 }) { Tick(w, t); w.TryGetStrength("you", out var a, out int tail); Console.WriteLine($"tick={t} attack={a.attack} head={a.defense} tail={tail} remaining={w.OpeningBoostRemaining}"); C((t <= 300 ? a.attack == 140 : a.attack == 120), "actual attack " + t); C(w.OpeningBoostRemaining == (t == 0 ? 300 : Math.Max(0, 301 - t)), "remaining " + t); }
      Tick(old, 1); old.TryGetStrength("you", out var neutral, out _); C(neutral.attack == 120 && old.RulesVersion == "strength-local-advantage-12", "old unchanged");
      C(w.RulesVersion == "strength-local-advantage-13", "new version");
      for (int seed = 1; seed <= 8; seed++)
      {
        var x = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true, localOpeningBoost: true);
        var tape = new List<byte>(); while (!x.Finished) { byte h = (byte)(x.Tick * 13 + seed); tape.Add(h); tape.Add(2); x.Step(h, false, true); }
        C(PveFixedRecording.TryReplayLocalStrength(x.RulesVersion, seed, false, tape.ToArray(), out var replay) && StrengthStateDigest.Compute(x) == StrengthStateDigest.Compute(replay), "v13 replay " + seed);
      }
      {
        var x = W(true); var fs = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(x);
        fs[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16)); fs[1] = new PveFixedFighter("bot-1", new PveFixedMotion(20000, 0, 0, 60));
        for (int i = 2; i < fs.Length; i++) fs[i].alive = false;
        Tick(x, 300); var snap = x.RenderState(); var markers = snap.snakes[1].strengthWeakSegments.ToArray(); int before = snap.snakes[0].strengthHeadDefense;
        Tick(x, 301); snap = x.RenderState(); var next = snap.snakes[1].strengthWeakSegments.ToArray(); int after = snap.snakes[0].strengthHeadDefense;
        C(before == 156 && after == 129, "appearance mirrors actual power at expiry"); C(markers.Zip(next, (a, b) => a && !b).Any(a => a), "some legitimate weak-segment hints disappear as power expires");
      }
      {
        var x = W(true);
        Tick(x, 0); C(OpeningBoostNotice.Text(x) == "攻防强化 15秒", "notice at start");
        Tick(x, 280); C(OpeningBoostNotice.Text(x) == "攻防强化 2秒", "notice before last second");
        Tick(x, 281); C(OpeningBoostNotice.Text(x) == "攻防强化 最后1秒", "notice last second");
        Tick(x, 300); C(OpeningBoostNotice.Text(x) == "攻防强化 最后1秒", "notice last boosted tick");
        Tick(x, 301); C(OpeningBoostNotice.Text(x) == "攻防强化已结束", "notice after boost");
        Tick(x, 320); C(OpeningBoostNotice.Text(x) == "攻防强化已结束", "notice until tick 320");
        Tick(x, 321); C(OpeningBoostNotice.Text(x) == "", "notice cleared");
        Tick(x, 1); x.Fighters[0].alive = false; C(OpeningBoostNotice.Text(x) == "", "notice absent after death");
        x = W(true); typeof(PveFixedWorld).GetProperty("Finished").GetSetMethod(true).Invoke(x, new object[] { true }); C(OpeningBoostNotice.Text(x) == "", "notice absent after match ends");
        C(OpeningBoostNotice.Text(W(false)) == "", "notice absent in old rules");
        C(OpeningBoostNotice.Text(W(true)) == "攻防强化 15秒", "notice resets on restart");
      }

    }
  }
}
