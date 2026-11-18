using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    public readonly struct HeadTieContact
    {
        public readonly int first, second;
        public readonly CapturePoint firstPosition, secondPosition;
        public readonly long distanceSquared;
        public HeadTieContact(int first, int second, CapturePoint firstPosition, CapturePoint secondPosition, long distanceSquared)
        { this.first = first; this.second = second; this.firstPosition = firstPosition; this.secondPosition = secondPosition; this.distanceSquared = distanceSquared; }
    }

    // Head-to-head is resolved before body bites from a shared snapshot.
    // Near-equal or mutually tied strength causes no elimination in the preview.
    public static class StrengthHeadCombat
    {
        public static int[] Resolve(PveFixedFighter[] fighters, StrengthInputs[] inputs, bool deferGrowth = false, List<HeadTieContact> ties = null)
        {
            if (fighters == null) throw new ArgumentException("Invalid head combat inputs");
            return ResolveBuffered(fighters, inputs, deferGrowth, ties, new StrengthValues[fighters.Length],
                new int[fighters.Length], new long[fighters.Length], new int[fighters.Length]);
        }
        // Buffers belong to one world and are reused only after the caller has
        // consumed the previous step's results.
        internal static int[] ResolveBuffered(PveFixedFighter[] fighters, StrengthInputs[] inputs, bool deferGrowth,
            List<HeadTieContact> ties, StrengthValues[] values, int[] winner, long[] advantage, int[] growth)
        {
            if (fighters == null || inputs == null || fighters.Length != inputs.Length) throw new ArgumentException("Invalid head combat inputs");
            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i] == null) throw new ArgumentException("Invalid fighter");
                for (int j = 0; j < i; j++) if (fighters[i].id == fighters[j].id) throw new ArgumentException("Duplicate fighter ID");
                var power = inputs[i]; power.length = fighters[i].motion.Length;
                if (!BodyStrength.TryCalculate(power, 0, out values[i])) throw new ArgumentException("Invalid strength input");
            }
            ties?.Clear();
            for (int i = 0; i < winner.Length; i++) { winner[i] = -1; advantage[i] = 0; growth[i] = 0; }
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
                else if (ties != null)
                {
                    int first = string.CompareOrdinal(a.id, b.id) < 0 ? i : j, second = first == i ? j : i;
                    ties.Add(new HeadTieContact(first, second, fighters[first].motion.Segments[0], fighters[second].motion.Segments[0], dx * dx + dy * dy));
                }
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
            for (int i = 0; i < fighters.Length; i++) if (winner[i] >= 0 && fighters[winner[i]].alive)
            {
                fighters[i].credited = true; fighters[winner[i]].kills++;
                growth[winner[i]] += Math.Max(1, fighters[i].motion.Length / 2);
            }
            if (!deferGrowth) for (int i = 0; i < fighters.Length; i++) fighters[i].motion.Grow(growth[i]);
            return growth;
        }
        // Resolve ties only after head eliminations and body bites have been
        // decided from the original snapshot. Disjoint pairs use stable order.
        public static bool[] SeparateTies(PveFixedFighter[] fighters, List<HeadTieContact> ties, List<BodyBite> bites)
            => SeparateTiesBuffered(fighters, ties, bites, new bool[fighters.Length]);
        internal static bool[] SeparateTiesBuffered(PveFixedFighter[] fighters, List<HeadTieContact> ties, List<BodyBite> bites, bool[] used)
        {
            Array.Clear(used, 0, used.Length);
            if (ties.Count == 0) return used;
            ties.Sort((a, b) => {
                int order = a.distanceSquared.CompareTo(b.distanceSquared);
                if (order == 0) order = string.CompareOrdinal(fighters[a.first].id, fighters[b.first].id);
                if (order == 0) order = string.CompareOrdinal(fighters[a.second].id, fighters[b.second].id);
                return order;
            });
            foreach (var tie in ties)
            {
                var a = fighters[tie.first]; var b = fighters[tie.second];
                if (!a.alive || !b.alive || used[tie.first] || used[tie.second]) continue;
                bool involved = false;
                foreach (var bite in bites)
                    if (bite.attacker == a.id || bite.attacker == b.id || bite.defender == a.id || bite.defender == b.id) { involved = true; break; }
                if (involved) continue;
                int dx = tie.firstPosition.x - tie.secondPosition.x, dy = tie.firstPosition.y - tie.secondPosition.y;
                int length = PveFixedMotion.Distance(dx, dy);
                if (length == 0) { dx = 1; dy = 0; length = 1; }
                int ox = (int)((long)dx * 760 / length), oy = (int)((long)dy * 760 / length);
                int edge = PveFixedMotion.Edge;
                int mx = Math.Max(-edge + Math.Abs(ox), Math.Min(edge - Math.Abs(ox), (tie.firstPosition.x + tie.secondPosition.x) / 2));
                int my = Math.Max(-edge + Math.Abs(oy), Math.Min(edge - Math.Abs(oy), (tie.firstPosition.y + tie.secondPosition.y) / 2));
                a.motion.PlaceSeparatedHead(mx + ox, my + oy, dx, dy);
                b.motion.PlaceSeparatedHead(mx - ox, my - oy, -dx, -dy);
                used[tie.first] = used[tie.second] = true;
            }
            return used;
        }
    }

    public readonly struct BodyBite
    {
        public readonly string attacker, defender;
        public readonly int segment, removed, attack, defense;
        public BodyBite(string attacker, string defender, int segment, int removed, int attack, int defense)
        { this.attacker = attacker; this.defender = defender; this.segment = segment; this.removed = removed; this.attack = attack; this.defense = defense; }
    }

    public readonly struct BlockedBodyContact
    {
        public readonly string attacker, defender;
        public readonly CapturePoint point, tangent;
        public readonly long distanceSquared;
        public BlockedBodyContact(string attacker, string defender, CapturePoint point, CapturePoint tangent, long distanceSquared)
        { this.attacker = attacker; this.defender = defender; this.point = point; this.tangent = tangent; this.distanceSquared = distanceSquared; }
    }

    // Independent experimental body combat for the isolated preview.
    // A context belongs to one match; ticks are monotonic and cooldowns are per pair.
    public sealed class StrengthBodyCombat
    {
        private const long BodyRadiusSquared = 1116L * 1116L, HeadRadiusSquared = 1440L * 1440L;
        private const int CooldownTicks = 8;
        private readonly Dictionary<(string attacker, string defender), long> nextHit = new Dictionary<(string, string), long>();
        private long lastTick = -1;
        private readonly List<string> blockedAttackers = new List<string>();
        public IReadOnlyList<string> BlockedAttackers => blockedAttackers;
        private readonly List<BlockedBodyContact> blockedContacts = new List<BlockedBodyContact>();
        public IReadOnlyList<BlockedBodyContact> BlockedContacts => blockedContacts;
        private sealed class Candidate
        {
            public int attacker, defender, segment, count, attack, defense;
            public long advantage;
        }
        private StrengthInputs[] powersBuffer = Array.Empty<StrengthInputs>();
        private bool[] usedAttackersBuffer = Array.Empty<bool>(), usedDefendersBuffer = Array.Empty<bool>();
        private readonly List<Candidate> candidatesBuffer = new List<Candidate>();
        private readonly List<Candidate> chosenBuffer = new List<Candidate>();
        private readonly List<BodyBite> bitesBuffer = new List<BodyBite>();
        private static long DistanceSquared(CapturePoint a, CapturePoint b)
        { long x = (long)a.x - b.x, y = (long)a.y - b.y; return x * x + y * y; }
        private static (string attacker, string defender) Key(string attacker, string defender) => (attacker, defender);
        internal bool IsPairCoolingDown(long tick, string attacker, string defender)
            => nextHit.TryGetValue(Key(attacker, defender), out long allowed) && tick < allowed;

        // All contacts and powers are decided from the state at the start of
        // this call. Each attacker and each defender has at most one bite per
        // tick; competing bites use advantage, then stable IDs, never list order.
        // The caller applies contact separation after all snapshot outcomes.
        public List<BodyBite> Resolve(long tick, PveFixedFighter[] fighters, StrengthInputs[] inputs)
            => new List<BodyBite>(ResolveBuffered(tick, fighters, inputs));

        // The result belongs to this match and remains valid until its next step.
        internal List<BodyBite> ResolveBuffered(long tick, PveFixedFighter[] fighters, StrengthInputs[] inputs)
        {
            if (tick < 0 || tick <= lastTick || tick > long.MaxValue - CooldownTicks) throw new ArgumentOutOfRangeException(nameof(tick));
            if (fighters == null || inputs == null || fighters.Length != inputs.Length) throw new ArgumentException("Fighters and strengths must have matching lengths");
            if (powersBuffer.Length != fighters.Length)
            {
                powersBuffer = new StrengthInputs[fighters.Length];
                usedAttackersBuffer = new bool[fighters.Length]; usedDefendersBuffer = new bool[fighters.Length];
            }
            var powers = powersBuffer;
            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i] == null || fighters[i].id.IndexOf('\0') >= 0) throw new ArgumentException("Invalid fighter");
                for (int j = 0; j < i; j++) if (fighters[i].id == fighters[j].id) throw new ArgumentException("Duplicate fighter ID");
                powers[i] = inputs[i]; powers[i].length = fighters[i].motion.Length;
                if (!BodyStrength.TryCalculate(powers[i], 0, out _)) throw new ArgumentException("Invalid strength input");
            }
            lastTick = tick;
            blockedAttackers.Clear();
            blockedContacts.Clear();
            var candidates = candidatesBuffer; candidates.Clear();
            for (int i = 0; i < fighters.Length; i++)
            {
                var attacker = fighters[i]; if (!attacker.alive || attacker.shieldTicks > 0) continue;
                BodyStrength.TryCalculate(powers[i], 0, out var attackingPower);
                for (int j = 0; j < fighters.Length; j++)
                {
                    var defender = fighters[j];
                    if (i == j || !defender.alive || defender.shieldTicks > 0 || defender.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    if (IsPairCoolingDown(tick, attacker.id, defender.id)) continue;
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
                        var before = defender.motion.Segments[segment - 1];
                        var after = defender.motion.Segments[Math.Min(segment + 1, defender.motion.Length - 1)];
                        var contact = new BlockedBodyContact(attacker.id, defender.id, defender.motion.Segments[segment],
                            new CapturePoint(after.x - before.x, after.y - before.y), nearest);
                        int existing = blockedContacts.FindIndex(c => c.attacker == attacker.id);
                        if (existing < 0) blockedContacts.Add(contact);
                        else if (nearest < blockedContacts[existing].distanceSquared ||
                            (nearest == blockedContacts[existing].distanceSquared && string.CompareOrdinal(defender.id, blockedContacts[existing].defender) < 0))
                            blockedContacts[existing] = contact;
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
            if (candidates.Count > 1) candidates.Sort((a, b) => {
                int order = b.advantage.CompareTo(a.advantage);
                if (order == 0) order = string.CompareOrdinal(fighters[a.attacker].id, fighters[b.attacker].id);
                if (order == 0) order = string.CompareOrdinal(fighters[a.defender].id, fighters[b.defender].id);
                return order;
            });
            var usedAttackers = usedAttackersBuffer; var usedDefenders = usedDefendersBuffer;
            Array.Clear(usedAttackers, 0, usedAttackers.Length); Array.Clear(usedDefenders, 0, usedDefenders.Length);
            var chosen = chosenBuffer; chosen.Clear();
            foreach (var candidate in candidates)
            {
                if (usedAttackers[candidate.attacker] || usedDefenders[candidate.defender]) continue;
                usedAttackers[candidate.attacker] = usedDefenders[candidate.defender] = true;
                chosen.Add(candidate);
            }
            var bites = bitesBuffer; bites.Clear();
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
