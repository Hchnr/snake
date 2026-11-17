#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    // Experimental combat state; the caller advances motion and protection
    // before invoking Resolve once per simulation step.
    public sealed class PveFixedFighter
    {
        public readonly string id;
        public readonly PveFixedMotion motion;
        public bool alive = true;
        public int shieldTicks, captureCooldown, kills, eventPoints;
        public bool credited;
        public string eliminatedBy = "", eliminationReason = "";
        public PveFixedFighter(string id, PveFixedMotion motion)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A fighter needs an ID", nameof(id));
            this.id = id; this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
        }
    }

    public static class PveFixedCombat
    {
        private const long HeadRadiusSquared = 1440L * 1440L;
        // 0.72 * 1.55 world units, expressed in thousandths.
        private const long TrailRadiusSquared = 1116L * 1116L;
        private static long DistanceSquared(CapturePoint a, CapturePoint b)
        {
            long x = (long)a.x - b.x, y = (long)a.y - b.y;
            return x * x + y * y;
        }
        private static bool Active(PveFixedFighter fighter) => fighter.alive && fighter.shieldTicks <= 0;
        private static void Apply(PveFixedFighter[] fighters, int[] killer, string[] reason)
        {
            for (int i = 0; i < fighters.Length; i++) if (killer[i] != -2)
            {
                fighters[i].alive = false;
                fighters[i].eliminatedBy = killer[i] < 0 ? "" : fighters[killer[i]].id;
                fighters[i].eliminationReason = reason[i];
            }
            for (int i = 0; i < fighters.Length; i++)
            {
                int owner = killer[i];
                if (owner < 0 || !fighters[owner].alive) continue;
                fighters[i].credited = true;
                fighters[owner].kills++;
                fighters[owner].motion.Grow(Math.Max(1, fighters[i].motion.Length / 2));
            }
        }
        public static void Resolve(PveFixedFighter[] fighters)
        {
            if (fighters == null) throw new ArgumentNullException(nameof(fighters));
            var ids = new HashSet<string>();
            foreach (var fighter in fighters)
                if (fighter == null || !ids.Add(fighter.id)) throw new ArgumentException("Fighters must have unique IDs", nameof(fighters));
            var killer = new int[fighters.Length]; var reason = new string[fighters.Length];
            for (int i = 0; i < killer.Length; i++) killer[i] = -2;
            for (int i = 0; i < fighters.Length; i++)
            {
                var a = fighters[i]; if (!Active(a)) continue;
                for (int j = i + 1; j < fighters.Length; j++)
                {
                    var b = fighters[j];
                    if (!Active(b) || DistanceSquared(a.motion.Segments[0], b.motion.Segments[0]) >= HeadRadiusSquared) continue;
                    if (a.motion.Length <= b.motion.Length && killer[i] == -2) { killer[i] = a.motion.Length == b.motion.Length ? -1 : j; reason[i] = killer[i] < 0 ? "head_tie" : "head"; }
                    if (b.motion.Length <= a.motion.Length && killer[j] == -2) { killer[j] = a.motion.Length == b.motion.Length ? -1 : i; reason[j] = killer[j] < 0 ? "head_tie" : "head"; }
                }
            }
            for (int i = 0; i < fighters.Length; i++)
            {
                var attacker = fighters[i]; if (!Active(attacker) || killer[i] != -2) continue;
                for (int j = 0; j < fighters.Length && killer[i] == -2; j++)
                {
                    var defender = fighters[j]; if (i == j || !Active(defender)) continue;
                    for (int k = 2; k < defender.motion.Length; k++)
                        if (DistanceSquared(attacker.motion.Segments[0], defender.motion.Segments[k]) < TrailRadiusSquared)
                        { killer[i] = j; reason[i] = "trail"; break; }
                }
            }
            Apply(fighters, killer, reason);
            var loops = new CaptureLoop[fighters.Length];
            for (int i = 0; i < fighters.Length; i++)
            {
                var fighter = fighters[i];
                if (fighter.captureCooldown > 0) fighter.captureCooldown--;
                if (Active(fighter) && fighter.captureCooldown == 0 && fighter.motion.TryCapture(out var loop))
                { loops[i] = loop; fighter.captureCooldown = 15; }
                killer[i] = -2; reason[i] = null;
            }
            for (int victim = 0; victim < fighters.Length; victim++)
            {
                if (!Active(fighters[victim])) continue;
                for (int owner = 0; owner < fighters.Length; owner++)
                {
                    if (owner == victim || loops[owner] == null || !CaptureGeometry.Contains(loops[owner].polygon, fighters[victim].motion.Segments[0])) continue;
                    int previous = killer[victim];
                    if (previous < 0 || loops[owner].doubleArea < loops[previous].doubleArea ||
                        (loops[owner].doubleArea == loops[previous].doubleArea && string.CompareOrdinal(fighters[owner].id, fighters[previous].id) < 0))
                    { killer[victim] = owner; reason[victim] = "capture"; }
                }
            }
            Apply(fighters, killer, reason);
        }
    }
}
