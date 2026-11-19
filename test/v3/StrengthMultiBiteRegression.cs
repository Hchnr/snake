using SnakeClub.Core;
using System;
using System.Linq;
using System.Collections.Generic;

public static class StrengthMultiBiteRegression
{
  public static void Run()
  {
    static PveFixedFighter F(string id, int x, int y = 0, int angle = 0, int length = 16) => new(id, new PveFixedMotion(x, y, angle, length));
    static StrengthInputs P(int n) => new() { basePower = n, speed = 1000, baseSpeed = 1000, randomFactor = 1000, operationFactor = 1000 };
    static (string, int) Run(int x1, int x2, int length, bool multi, bool reverse)
    {
      var a = F("a", x1, 800, 16384); var b = F("b", x2, -800, 49152); var t = F("target", 0, length: length);
      var fs = reverse ? new[] { t, b, a } : new[] { a, b, t }; var powers = fs.Select(z => P(z.id == "target" ? 100 : 300)).ToArray();
      var bites = new StrengthBodyCombat().ResolveBuffered(0, fs, powers, false, multi);
      return (string.Join(",", bites.Select(z => $"{z.attacker}:{z.defender}:{z.segment}+{z.removed}")), t.motion.Length);
    }
    void C(bool ok, string msg) { if (!ok) throw new Exception(msg); }
    var legacy = Run(-4500, -11700, 16, false, false); C(legacy == ("b:target:13+3", 13), "legacy old behaviour");
    var separate = Run(-4500, -11700, 16, true, false); C(separate.Item2 == 10 && separate.Item1.Contains("a:target:5+3") && separate.Item1.Contains("b:target:13+3"), "distinct targets");
    C(separate == Run(-4500, -11700, 16, true, true), "permutation deterministic");
    var overlap = Run(-4500, -6300, 16, true, false); C(overlap == ("b:target:7+3,a:target:5+2", 11), "overlap truncates weaker contiguous candidate at first occupied segment");
    var budget = Run(-1800, -5400, 8, true, false); C(budget == ("b:target:6+2,a:target:2+1", 5), "minimum body budget allocated deterministically");

    for (int seed = 1; seed <= 16; seed++)
    {
      var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true);
      var inputs = new List<byte>(); while (!world.Finished) { byte h = (byte)((world.Tick * 13 + seed) % 256); inputs.Add(h); inputs.Add(2); world.Step(h, false, true); }
      bool valid = PveFixedRecording.TryReplayStrength(seed, false, inputs.ToArray(), out var replay, true, true, true, true, true, true, true, true, true, true, true);
      C(valid && world.RulesVersion == "strength-local-advantage-11" && StrengthStateDigest.Compute(world) == StrengthStateDigest.Compute(replay), $"v11 replay seed {seed}");
      C(PveFixedRecording.TryReplayLocalStrength(world.RulesVersion, seed, false, inputs.ToArray(), out var imported) && StrengthStateDigest.Compute(world) == StrengthStateDigest.Compute(imported), $"v11 version import seed {seed}");
      C(!PveFixedRecording.TryReplayLocalStrength("strength-local-advantage-unknown", seed, false, inputs.ToArray(), out _), "unknown version rejected");
    }
    {
      var w = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true, localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true);
      var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w);
      actors[0] = F("you", 0); actors[1] = F("bot-1", -4500, 400, 16384, 80); actors[2] = F("bot-2", -11700, -400, 49152, 80); for (int i = 3; i < actors.Length; i++) actors[i].alive = false;
      w.Step(0, false, true);
      C(w.LastPlayerDamageTick == w.Tick && w.LastPlayerDamageCount == 6 && w.PlayerSegmentsLostToBites == 6 && w.LastPlayerBiteCount == 6 && w.LastPlayerWasBitten, "actual simultaneous two-attacker damage aggregated");
      C(w.LastPlayerDamagePoints.Count == 2 && (w.LastPlayerDamagePoints[0].x != w.LastPlayerDamagePoints[1].x || w.LastPlayerDamagePoints[0].y != w.LastPlayerDamagePoints[1].y), "two actual damage positions preserved");
    }
    {
      var a = new PveFixedMotion(0, 0, 0, 16); var b = new PveFixedMotion(0, 0, 0, 16);
      var field = typeof(PveFixedMotion).GetField("segments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      foreach (var m in new[] { a, b }) { var xs = (List<CapturePoint>)field.GetValue(m); xs[1] = new CapturePoint(-4000, 0); }
      int n = a.RemoveBodySegments(8, 3); var mask = new bool[Arena.MaximumLength]; for (int i = 8; i < 11; i++) mask[i] = true; b.RemoveBodySegmentsBatch(mask);
      C(n == 3 && a.Segments.Select(x => (x.x, x.y)).SequenceEqual(b.Segments.Select(x => (x.x, x.y))), "single bite keeps legacy shape and preexisting gap");
    }
    {
      var states = new List<string>();
      foreach (bool aStronger in new[] { false, true })
      {
        var a = F("a", -4500, 800, 16384); var b = F("b", -11700, -800, 49152); var t = F("target", 0);
        var bites = new StrengthBodyCombat().ResolveBuffered(0, new[] { a, b, t }, new[] { P(aStronger ? 300 : 200), P(aStronger ? 200 : 300), P(100) }, false, true);
        C(bites.Count == 2 && t.motion.Length == 10, "two distinct segments allocated");
        states.Add(string.Join(";", t.motion.Segments.Select(p => $"{p.x},{p.y}")));
      }
      C(states[0] == states[1], "batch final geometry independent of deletion priority");
    }

    {
      foreach (var order in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
      {
        var all = new[] { F("a", -4500, 800, 16384), F("b", -6300, -800, 49152), F("target", 0) }; var f = order.Select(i => all[i]).ToArray();
        var power = f.Select(v => P(v.id == "target" ? 100 : 300)).ToArray(); var bites = new StrengthBodyCombat().ResolveBuffered(0, f, power, false, true);
        C(string.Join(",", bites.Select(x => $"{x.attacker}:{x.defender}:{x.segment}+{x.removed}")) == "b:target:7+3,a:target:5+2" && all[2].motion.Length == 11, "all six overlap permutations");
      }
      var a = F("a", -4500, 800, 49152); var b = F("b", -4500, -800, 16384); var t = F("target", 0);
      var contests = new StrengthBodyCombat().ResolveBuffered(0, new[] { a, b, t }, new[] { P(300), P(300), P(100) }, false, true);
      C(contests.Count == 1 && contests[0].attacker == "a" && contests[0].segment == 5 && contests[0].removed == 3 && t.motion.Length == 13, "same first segment blocked after stable ID winner");
    }

    {
      var a = F("a", 0, length: 80);
      var b = F("b", -71100, angle: 32768, length: 80);
      var bites = new StrengthBodyCombat().ResolveBuffered(0, new[] { a, b }, new[] { P(100), P(100) }, false, true);
      C(bites.Count == 2 && bites.Any(x => x.attacker == "a" && x.defender == "b" && x.removed == 1) &&
        bites.Any(x => x.attacker == "b" && x.defender == "a" && x.removed == 1), "full-length mutual tail bites resolved");
      C(a.motion.Length == Arena.MaximumLength && b.motion.Length == Arena.MaximumLength, "mutual loss and growth remain capped");
      C(a.motion.X == 0 && b.motion.X == -71100 && a.motion.Segments.Count == 80 && b.motion.Segments.Count == 80, "full-length mutual bites preserve heads and complete bodies");
    }
    {
      var world = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
        localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true, localCappedStart: true,
        localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true);
      var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(world);
      actors[0] = F("you", 0); actors[1] = F("bot-1", -4500, 400, 16384, 80); actors[2] = F("bot-2", -11700, -400, 49152, 80);
      actors[3] = F("bot-3", 12000, length: 16); for (int i = 4; i < actors.Length; i++) actors[i].alive = false;
      world.Step(0, false, true);
      C(world.LastPlayerAttackTick == world.Tick && world.LastPlayerAttackCount == 3 && world.PlayerSegmentsEaten == 3 &&
        world.LastPlayerDamageTick == world.Tick && world.LastPlayerDamageCount == 6 && world.PlayerSegmentsLostToBites == 6,
        "actual simultaneous attack and two injuries aggregate independently");
      C(actors[0].motion.Length == 13 && actors[0].motion.X == 300 && actors[0].motion.Y == 0,
        "simultaneous losses then growth preserve head and expected final length");
      C(world.LastPlayerDamagePoints.Count == 2 && world.LastPlayerDamagePoints[0].x != world.LastPlayerDamagePoints[1].x,
        "both distinct snapshot injury positions retained");
      C(StrengthContactNotice.Text(world, world.Tick, out bool warning) == "互相吞食 · 吃 3 节 / 失 6 节" && warning,
        "mutual bite notice includes actual aggregate attack and damage");
      C(world.RecoverySuppliesUsed == 1 && Enumerable.Range(0, world.Fruit.Count).Count(i => world.IsRecoveryFruit(i) && world.RecoveryPriorityTicks(i) == 40) == 4,
        "same-step recovery produces four real priority fruit");
    }
    {
      foreach (bool reverse in new[] { false, true })
      {
        var target = F("target", 0, length: 16);
        var mask = new bool[Arena.MaximumLength];
        foreach (int part in new[] { 2, 3, 6, 7 }) mask[part] = true;
        var oldContact = target.motion.Segments[12];
        target.motion.RemoveBodySegmentsBatch(mask);
        var projected = target.motion.Segments[8];
        var player = F("you", projected.x, projected.y + 300);
        var bites = new[] { new BodyBite("a", "target", 2, 2, 200, 100), new BodyBite("b", "target", 6, 2, 200, 100) };
        if (reverse) Array.Reverse(bites);
        var contact = new BlockedBodyContact("you", "target", oldContact, new CapturePoint(0, 1), 0, 12);
        C(StableBlockedSeparation.Apply(new[] { player, target }, bites, new[] { contact }, new bool[2], out _, true), "surviving contact maps across two deleted ranges in either order");
        var deletedContact = new BlockedBodyContact("you", "target", oldContact, new CapturePoint(0, 1), 0, 6);
        C(!StableBlockedSeparation.Apply(new[] { player, target }, bites, new[] { deletedContact }, new bool[2], out _, true), "deleted snapshot contact cannot bounce");
      }
    }

  }
}
