using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    // Head-to-head is resolved before body bites from a shared snapshot.
    // Near-equal or mutually tied strength causes no elimination in the preview.
    public static class StrengthHeadCombat
    {
        public static int[] Resolve(PveFixedFighter[] fighters, StrengthInputs[] inputs, bool deferGrowth = false)
        {
            if (fighters == null || inputs == null || fighters.Length != inputs.Length) throw new ArgumentException("Invalid head combat inputs");
            var values = new StrengthValues[fighters.Length];
            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i] == null) throw new ArgumentException("Invalid fighter");
                for (int j = 0; j < i; j++) if (fighters[i].id == fighters[j].id) throw new ArgumentException("Duplicate fighter ID");
                var power = inputs[i]; power.length = fighters[i].motion.Length;
                if (!BodyStrength.TryCalculate(power, 0, out values[i])) throw new ArgumentException("Invalid strength input");
            }
            var winner = new int[fighters.Length]; var advantage = new long[fighters.Length];
            for (int i = 0; i < winner.Length; i++) winner[i] = -1;
            for (int i = 0; i < fighters.Length; i++) for (int j = i + 1; j < fighters.Length; j++)
            {
                var a = fighters[i]; var b = fighters[j];
                if (!a.alive || !b.alive || a.shieldTicks > 0 || b.shieldTicks > 0) continue;
                long dx = (long)a.motion.X - b.motion.X, dy = (long)a.motion.Y - b.motion.Y;
                if (dx * dx + dy * dy >= 1440L * 1440L) continue;
                long ab = (long)values[i].attack * 100 - (long)values[j].defense * 105;
                long ba = (long)values[j].attack * 100 - (long)values[i].defense * 105;
                if (ab > 0 && ab > ba) Claim(j, i, ab);
                else if (ba > 0 && ba > ab) Claim(i, j, ba);
            }
            void Claim(int victim, int attacker, long margin)
            {
                int prior = winner[victim];
                if (prior < 0 || margin > advantage[victim] || (margin == advantage[victim] &&
                    string.CompareOrdinal(fighters[attacker].id, fighters[prior].id) < 0))
                { winner[victim] = attacker; advantage[victim] = margin; }
            }
            for (int i = 0; i < fighters.Length; i++) if (winner[i] >= 0)
            { fighters[i].alive = false; fighters[i].eliminatedBy = fighters[winner[i]].id; fighters[i].eliminationReason = "strength_head"; }
            var growth = new int[fighters.Length];
            for (int i = 0; i < fighters.Length; i++) if (winner[i] >= 0 && fighters[winner[i]].alive)
            {
                fighters[i].credited = true; fighters[winner[i]].kills++;
                growth[winner[i]] += Math.Max(1, fighters[i].motion.Length / 2);
            }
            if (!deferGrowth) for (int i = 0; i < fighters.Length; i++) fighters[i].motion.Grow(growth[i]);
            return growth;
        }
    }

    public readonly struct BodyBite
    {
        public readonly string attacker, defender;
        public readonly int segment, removed, attack, defense;
        public BodyBite(string attacker, string defender, int segment, int removed, int attack, int defense)
        { this.attacker = attacker; this.defender = defender; this.segment = segment; this.removed = removed; this.attack = attack; this.defense = defense; }
    }

    // Independent experimental body combat, not wired into an existing ruleset.
    // A context belongs to one match; ticks are monotonic and cooldowns are per pair.
    public sealed class StrengthBodyCombat
    {
        private const long BodyRadiusSquared = 1116L * 1116L, HeadRadiusSquared = 1440L * 1440L;
        private const int CooldownTicks = 8;
        private readonly Dictionary<string, long> nextHit = new Dictionary<string, long>();
        private long lastTick = -1;
        private readonly List<string> blockedAttackers = new List<string>();
        public IReadOnlyList<string> BlockedAttackers => blockedAttackers;
        private sealed class Candidate
        {
            public int attacker, defender, segment, count, attack, defense;
            public long advantage;
        }
        private static long DistanceSquared(CapturePoint a, CapturePoint b)
        { long x = (long)a.x - b.x, y = (long)a.y - b.y; return x * x + y * y; }
        private static string Key(string attacker, string defender) => attacker + "\0" + defender;

        // All contacts and powers are decided from the state at the start of
        // this call. Each attacker and each defender has at most one bite per
        // tick; competing bites use advantage, then stable IDs, never list order.
        // Head-on outcomes, separation, AI, feedback and rewards are future work.
        public List<BodyBite> Resolve(long tick, PveFixedFighter[] fighters, StrengthInputs[] inputs)
        {
            if (tick < 0 || tick <= lastTick || tick > long.MaxValue - CooldownTicks) throw new ArgumentOutOfRangeException(nameof(tick));
            if (fighters == null || inputs == null || fighters.Length != inputs.Length) throw new ArgumentException("Fighters and strengths must have matching lengths");
            var powers = new StrengthInputs[fighters.Length];
            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i] == null || fighters[i].id.IndexOf('\0') >= 0) throw new ArgumentException("Invalid fighter");
                for (int j = 0; j < i; j++) if (fighters[i].id == fighters[j].id) throw new ArgumentException("Duplicate fighter ID");
                powers[i] = inputs[i]; powers[i].length = fighters[i].motion.Length;
                if (!BodyStrength.TryCalculate(powers[i], 0, out _)) throw new ArgumentException("Invalid strength input");
            }
            lastTick = tick;
            blockedAttackers.Clear();
            var candidates = new List<Candidate>();
            for (int i = 0; i < fighters.Length; i++)
            {
                var attacker = fighters[i]; if (!attacker.alive || attacker.shieldTicks > 0) continue;
                BodyStrength.TryCalculate(powers[i], 0, out var attackingPower);
                for (int j = 0; j < fighters.Length; j++)
                {
                    var defender = fighters[j];
                    if (i == j || !defender.alive || defender.shieldTicks > 0 || defender.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    if (nextHit.TryGetValue(Key(attacker.id, defender.id), out long allowed) && tick < allowed) continue;
                    if (DistanceSquared(attacker.motion.Segments[0], defender.motion.Segments[0]) < HeadRadiusSquared) continue;
                    int segment = -1; long nearest = BodyRadiusSquared;
                    for (int k = 1; k < defender.motion.Length; k++)
                    {
                        long distance = DistanceSquared(attacker.motion.Segments[0], defender.motion.Segments[k]);
                        if (distance < nearest) { nearest = distance; segment = k; }
                    }
                    if (segment < 0) continue;
                    BodyStrength.TryCalculate(powers[j], segment, out var defendingPower);
                    if (!BodyStrength.CanEat(attackingPower.attack, defendingPower.defense))
                    {
                        if (!blockedAttackers.Contains(attacker.id)) blockedAttackers.Add(attacker.id);
                        continue;
                    }
                    int count = 0, max = Math.Min(3, defender.motion.Length - PveFixedMotion.MinimumLength);
                    for (int k = segment; k < defender.motion.Length && count < max; k++)
                    {
                        BodyStrength.TryCalculate(powers[j], k, out var part);
                        if (!BodyStrength.CanEat(attackingPower.attack, part.defense)) break;
                        count++;
                    }
                    if (count > 0) candidates.Add(new Candidate { attacker = i, defender = j, segment = segment, count = count,
                        attack = attackingPower.attack, defense = defendingPower.defense,
                        advantage = (long)attackingPower.attack * 100 - (long)defendingPower.defense * 105 });
                }
            }
            candidates.Sort((a, b) => {
                int order = b.advantage.CompareTo(a.advantage);
                if (order == 0) order = string.CompareOrdinal(fighters[a.attacker].id, fighters[b.attacker].id);
                if (order == 0) order = string.CompareOrdinal(fighters[a.defender].id, fighters[b.defender].id);
                return order;
            });
            var usedAttackers = new bool[fighters.Length]; var usedDefenders = new bool[fighters.Length];
            var chosen = new List<Candidate>();
            foreach (var candidate in candidates)
            {
                if (usedAttackers[candidate.attacker] || usedDefenders[candidate.defender]) continue;
                usedAttackers[candidate.attacker] = usedDefenders[candidate.defender] = true;
                chosen.Add(candidate);
            }
            var bites = new List<BodyBite>(chosen.Count);
            foreach (var c in chosen)
            {
                var attacker = fighters[c.attacker]; var defender = fighters[c.defender];
                int removed = defender.motion.RemoveBodySegments(c.segment, c.count);
                if (removed <= 0) continue;
                nextHit[Key(attacker.id, defender.id)] = tick + CooldownTicks;
                bites.Add(new BodyBite(attacker.id, defender.id, c.segment, removed, c.attack, c.defense));
            }
            // Growth is applied after all body removals, preserving snapshot decisions.
            foreach (var bite in bites)
                for (int i = 0; i < fighters.Length; i++) if (fighters[i].id == bite.attacker) { fighters[i].motion.Grow(bite.removed); break; }
            return bites;
        }
    }
}
