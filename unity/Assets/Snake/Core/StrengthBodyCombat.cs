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
                if (!a.alive || !b.alive || a.ProtectionTicks > 0 || b.ProtectionTicks > 0) continue;
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
        // Resolve ties after head eliminations and body bites from the same snapshot.
        // Pairs use stable order; the v12 preview checks actual positions again.
        public static bool[] SeparateTies(PveFixedFighter[] fighters, List<HeadTieContact> ties, List<BodyBite> bites, bool separateAfterBite = false)
            => SeparateTiesBuffered(fighters, ties, bites, new bool[fighters.Length], separateAfterBite);
        internal static bool[] SeparateTiesBuffered(PveFixedFighter[] fighters, List<HeadTieContact> ties, List<BodyBite> bites, bool[] used, bool separateAfterBite = false)
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
                if (involved && !separateAfterBite) continue;
                int dx = separateAfterBite ? a.motion.X - b.motion.X : tie.firstPosition.x - tie.secondPosition.x;
                int dy = separateAfterBite ? a.motion.Y - b.motion.Y : tie.firstPosition.y - tie.secondPosition.y;
                if (separateAfterBite && (a.ProtectionTicks > 0 || b.ProtectionTicks > 0 || (long)dx * dx + (long)dy * dy >= 1440L * 1440L)) continue;
                if (separateAfterBite)
                {
                    if (!TrySafeTiePositions(fighters, tie.first, tie.second, dx, dy, out int ax, out int ay, out int bx, out int by, out int directionX, out int directionY)) continue;
                    a.motion.PlaceSeparatedHead(ax, ay, directionX, directionY);
                    b.motion.PlaceSeparatedHead(bx, by, -directionX, -directionY);
                    used[tie.first] = used[tie.second] = true;
                    continue;
                }
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
        private static void PreviewHeadMove(PveFixedMotion motion, int x, int y, Span<CapturePoint> preview)
        {
            preview[0] = new CapturePoint(x, y);
            for (int i = 1; i < motion.Length; i++)
            {
                var a = preview[i - 1]; var b = motion.Segments[i];
                int dx = a.x - b.x, dy = a.y - b.y, length = PveFixedMotion.Distance(dx, dy);
                preview[i] = length > PveFixedMotion.Spacing
                    ? new CapturePoint(a.x - (int)((long)dx * PveFixedMotion.Spacing / length), a.y - (int)((long)dy * PveFixedMotion.Spacing / length)) : b;
            }
        }
        private static bool IntroducesBodyContact(PveFixedMotion changed, Span<CapturePoint> preview, PveFixedMotion other)
        {
            const long radius = 1116L * 1116L;
            var otherHead = other.Segments[0]; var oldHead = changed.Segments[0]; var newHead = preview[0];
            for (int i = 1; i < changed.Length; i++)
            {
                var before = changed.Segments[i]; var after = preview[i];
                long x = (long)before.x - otherHead.x, y = (long)before.y - otherHead.y;
                long nx = (long)after.x - otherHead.x, ny = (long)after.y - otherHead.y;
                if (nx * nx + ny * ny < Math.Min(radius, x * x + y * y)) return true;
            }
            for (int i = 1; i < other.Length; i++)
            {
                var body = other.Segments[i];
                long x = (long)oldHead.x - body.x, y = (long)oldHead.y - body.y;
                long nx = (long)newHead.x - body.x, ny = (long)newHead.y - body.y;
                if (nx * nx + ny * ny < Math.Min(radius, x * x + y * y)) return true;
            }
            return false;
        }
        // For the separating pair, head contacts take precedence over body
        // contacts on this tick. Avoid creating the first body contact, but do
        // not prevent an existing contact from getting closer. Nearest segment
        // identity and strength may change; this is not a whole-frame guarantee.
        private static bool PairCreatesBodyContact(PveFixedMotion body, Span<CapturePoint> bodyPreview, PveFixedMotion head, Span<CapturePoint> headPreview)
        {
            const long radius = 1116L * 1116L;
            var oldHead = head.Segments[0]; var newHead = headPreview[0];
            long oldNearest = long.MaxValue, newNearest = long.MaxValue;
            for (int i = 1; i < body.Length; i++)
            {
                var before = body.Segments[i]; var after = bodyPreview[i];
                long x = (long)before.x - oldHead.x, y = (long)before.y - oldHead.y;
                long nx = (long)after.x - newHead.x, ny = (long)after.y - newHead.y;
                oldNearest = Math.Min(oldNearest, x * x + y * y);
                newNearest = Math.Min(newNearest, nx * nx + ny * ny);
            }
            return oldNearest >= radius && newNearest < radius;
        }
        // Try the original separation first. Alternate offsets use a fixed
        // order so the same snapshot yields the same result on each runtime.
        private static readonly int[] TieAngles = { 0, 16, -16, 32, -32, 48, -48, 64, -64, 80, -80, 96, -96, 112, -112, 128 };
        private static bool TrySafeTiePositions(PveFixedFighter[] fighters, int first, int second, int dx, int dy,
            out int ax, out int ay, out int bx, out int by, out int directionX, out int directionY)
        {
            ax = ay = bx = by = directionX = directionY = 0;
            if (dx == 0 && dy == 0) dx = 1;
            var a = fighters[first].motion; var b = fighters[second].motion;
            Span<CapturePoint> previewA = stackalloc CapturePoint[Arena.MaximumLength];
            Span<CapturePoint> previewB = stackalloc CapturePoint[Arena.MaximumLength];
            int edge = PveFixedMotion.Edge;
            foreach (int offset in TieAngles)
            {
                int cosine = PveFixedMotion.DirectionX(unchecked((byte)offset));
                int sine = PveFixedMotion.DirectionY(unchecked((byte)offset));
                // Keep precision for coincident heads, whose fallback vector is 1.
                int vx = (int)((long)dx * cosine - (long)dy * sine);
                int vy = (int)((long)dx * sine + (long)dy * cosine);
                int length = PveFixedMotion.Distance(vx, vy);
                if (length == 0) continue;
                int ox, oy;
                if (offset == 0)
                {
                    int legacyLength = PveFixedMotion.Distance(dx, dy);
                    ox = (int)((long)dx * 760 / legacyLength); oy = (int)((long)dy * 760 / legacyLength);
                }
                else { ox = (int)((long)vx * 760 / length); oy = (int)((long)vy * 760 / length); }
                int mx = Math.Max(-edge + Math.Abs(ox), Math.Min(edge - Math.Abs(ox), (a.X + b.X) / 2));
                int my = Math.Max(-edge + Math.Abs(oy), Math.Min(edge - Math.Abs(oy), (a.Y + b.Y) / 2));
                int x1 = mx + ox, y1 = my + oy, x2 = mx - ox, y2 = my - oy;
                long pairX = (long)x1 - x2, pairY = (long)y1 - y2;
                if (pairX * pairX + pairY * pairY < 1440L * 1440L) continue;
                bool clear = true;
                for (int i = 0; i < fighters.Length; i++)
                {
                    if (i == first || i == second || !fighters[i].alive) continue;
                    var other = fighters[i].motion;
                    long x = (long)x1 - other.X, y = (long)y1 - other.Y;
                    long z = (long)x2 - other.X, w = (long)y2 - other.Y;
                    long oldAx = (long)a.X - other.X, oldAy = (long)a.Y - other.Y;
                    long oldBx = (long)b.X - other.X, oldBy = (long)b.Y - other.Y;
                    long aLimit = Math.Min(1440L * 1440L, oldAx * oldAx + oldAy * oldAy);
                    long bLimit = Math.Min(1440L * 1440L, oldBx * oldBx + oldBy * oldBy);
                    if (x * x + y * y < aLimit || z * z + w * w < bLimit) { clear = false; break; }
                }
                if (!clear) continue;
                PreviewHeadMove(a, x1, y1, previewA); PreviewHeadMove(b, x2, y2, previewB);
                if (PairCreatesBodyContact(a, previewA, b, previewB) || PairCreatesBodyContact(b, previewB, a, previewA)) continue;
                for (int i = 0; i < fighters.Length && clear; i++)
                {
                    if (i == first || i == second || !fighters[i].alive) continue;
                    var other = fighters[i].motion;
                    if (IntroducesBodyContact(a, previewA, other) || IntroducesBodyContact(b, previewB, other)) clear = false;
                }
                if (!clear) continue;
                ax = x1; ay = y1; bx = x2; by = y2; directionX = offset == 0 ? dx : vx; directionY = offset == 0 ? dy : vy; return true;
            }
            return false;
        }
    }

    public readonly struct BodyBite
    {
        public readonly string attacker, defender;
        public readonly int segment, removed, attack, defense;
        public readonly CapturePoint point;
        public BodyBite(string attacker, string defender, int segment, int removed, int attack, int defense, CapturePoint point = default)
        { this.attacker = attacker; this.defender = defender; this.segment = segment; this.removed = removed; this.attack = attack; this.defense = defense; this.point = point; }
    }

    public readonly struct BlockedBodyContact
    {
        public readonly string attacker, defender;
        public readonly CapturePoint point, tangent;
        public readonly long distanceSquared;
        public readonly int segment;
        public BlockedBodyContact(string attacker, string defender, CapturePoint point, CapturePoint tangent, long distanceSquared, int segment = -1)
        { this.attacker = attacker; this.defender = defender; this.point = point; this.tangent = tangent; this.distanceSquared = distanceSquared; this.segment = segment; }
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
        private readonly List<BlockedBodyContact> allBlockedContacts = new List<BlockedBodyContact>();
        internal IReadOnlyList<BlockedBodyContact> AllBlockedContacts => allBlockedContacts;
        private sealed class Candidate
        {
            public int attacker, defender, segment, count, attack, defense;
            public CapturePoint point;
            public long advantage;
        }
        private StrengthInputs[] powersBuffer = Array.Empty<StrengthInputs>();
        private bool[] usedAttackersBuffer = Array.Empty<bool>(), usedDefendersBuffer = Array.Empty<bool>();
        private bool[][] masksBuffer = Array.Empty<bool[]>();
        private int[] budgetsBuffer = Array.Empty<int>();
        private readonly List<Candidate> candidatesBuffer = new List<Candidate>();
        private readonly List<Candidate> chosenBuffer = new List<Candidate>();
        private readonly List<BodyBite> bitesBuffer = new List<BodyBite>();
        private static long DistanceSquared(CapturePoint a, CapturePoint b)
        { long x = (long)a.x - b.x, y = (long)a.y - b.y; return x * x + y * y; }
        private static (string attacker, string defender) Key(string attacker, string defender) => (attacker, defender);
        internal bool IsPairCoolingDown(long tick, string attacker, string defender)
            => nextHit.TryGetValue(Key(attacker, defender), out long allowed) && tick < allowed;

        // Stable fighter order avoids depending on dictionary enumeration order.
        internal void AppendReplayDiagnostics(PveFixedFighter[] fighters, Action<int> add)
        {
            add(unchecked((int)lastTick)); add(unchecked((int)(lastTick >> 32)));
            foreach (var attacker in fighters) foreach (var defender in fighters)
            {
                bool found = nextHit.TryGetValue(Key(attacker.id, defender.id), out long allowed);
                add(found ? 1 : 0); add(unchecked((int)allowed)); add(unchecked((int)(allowed >> 32)));
            }
        }

        // All contacts and powers are decided from the state at the start of
        // this call. Each attacker has at most one bite per tick; v11 permits
        // different attackers on disjoint body segments. Competing bites use
        // advantage, then stable IDs, never list order.
        // The caller applies contact separation after all snapshot outcomes.
        public List<BodyBite> Resolve(long tick, PveFixedFighter[] fighters, StrengthInputs[] inputs)
            => new List<BodyBite>(ResolveBuffered(tick, fighters, inputs));

        // The result belongs to this match and remains valid until its next step.
        internal List<BodyBite> ResolveBuffered(long tick, PveFixedFighter[] fighters, StrengthInputs[] inputs, bool collectAllBlocked = false, bool multi = false)
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
            allBlockedContacts.Clear();
            var candidates = candidatesBuffer; candidates.Clear();
            for (int i = 0; i < fighters.Length; i++)
            {
                var attacker = fighters[i]; if (!attacker.alive || attacker.ProtectionTicks > 0) continue;
                BodyStrength.TryCalculate(powers[i], 0, out var attackingPower);
                for (int j = 0; j < fighters.Length; j++)
                {
                    var defender = fighters[j];
                    if (i == j || !defender.alive || defender.ProtectionTicks > 0 || defender.motion.Length <= PveFixedMotion.MinimumLength) continue;
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
                            new CapturePoint(after.x - before.x, after.y - before.y), nearest, segment);
                        if (collectAllBlocked) allBlockedContacts.Add(contact);
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
                        attack = attackingPower.attack, defense = defendingPower.defense, point = defender.motion.Segments[segment],
                        advantage = (long)attackingPower.attack * 100 - (long)defendingPower.defense * 105 });
                }
            }
            if (collectAllBlocked) allBlockedContacts.Sort((a, b) => {
                int order = string.CompareOrdinal(a.attacker, b.attacker);
                if (order == 0) order = a.distanceSquared.CompareTo(b.distanceSquared);
                return order == 0 ? string.CompareOrdinal(a.defender, b.defender) : order;
            });
            if (candidates.Count > 1) candidates.Sort((a, b) => {
                int order = b.advantage.CompareTo(a.advantage);
                if (order == 0) order = string.CompareOrdinal(fighters[a.attacker].id, fighters[b.attacker].id);
                if (order == 0) order = string.CompareOrdinal(fighters[a.defender].id, fighters[b.defender].id);
                return order;
            });
            var usedAttackers = usedAttackersBuffer; var usedDefenders = usedDefendersBuffer;
            Array.Clear(usedAttackers, 0, usedAttackers.Length); Array.Clear(usedDefenders, 0, usedDefenders.Length);
            var chosen = chosenBuffer; chosen.Clear();
            var masks = masksBuffer; var budgets = budgetsBuffer;
            if (multi)
            {
                if (masks.Length != fighters.Length)
                {
                    masks = masksBuffer = new bool[fighters.Length][];
                    budgets = budgetsBuffer = new int[fighters.Length];
                    for (int i = 0; i < fighters.Length; i++) masks[i] = new bool[Arena.MaximumLength];
                }
                for (int i = 0; i < fighters.Length; i++)
                {
                    Array.Clear(masks[i], 0, masks[i].Length);
                    budgets[i] = fighters[i].motion.Length - PveFixedMotion.MinimumLength;
                }
            }
            foreach (var candidate in candidates)
            {
                if (usedAttackers[candidate.attacker] || (!multi && usedDefenders[candidate.defender])) continue;
                if (multi)
                {
                    int count = 0; var mask = masks[candidate.defender];
                    while (count < candidate.count && count < budgets[candidate.defender] && !mask[candidate.segment + count]) count++;
                    if (count == 0) continue;
                    candidate.count = count;
                    for (int i = 0; i < count; i++) mask[candidate.segment + i] = true;
                    budgets[candidate.defender] -= count;
                }
                usedAttackers[candidate.attacker] = usedDefenders[candidate.defender] = true;
                chosen.Add(candidate);
            }
            var bites = bitesBuffer; bites.Clear();
            if (multi)
                for (int i = 0; i < fighters.Length; i++)
                    if (budgets[i] < fighters[i].motion.Length - PveFixedMotion.MinimumLength)
                        fighters[i].motion.RemoveBodySegmentsBatch(masks[i]);
            foreach (var c in chosen)
            {
                var attacker = fighters[c.attacker]; var defender = fighters[c.defender];
                int removed = multi ? c.count : defender.motion.RemoveBodySegments(c.segment, c.count);
                if (removed <= 0) continue;
                nextHit[Key(attacker.id, defender.id)] = tick + CooldownTicks;
                bites.Add(new BodyBite(attacker.id, defender.id, c.segment, removed, c.attack, c.defense, c.point));
            }
            // Growth is applied after all body removals, preserving snapshot decisions.
            foreach (var bite in bites)
                for (int i = 0; i < fighters.Length; i++) if (fighters[i].id == bite.attacker) { fighters[i].motion.Grow(bite.removed); break; }
            return bites;
        }
    }

    // v8 only: the blocked decision is frozen before bites, but a deleted or
    // displaced segment cannot cause an imaginary rebound after resolution.
    internal static class StableBlockedSeparation
    {
        private const long RadiusSquared = 1116L * 1116L;
        internal static bool Apply(PveFixedFighter[] fighters, IReadOnlyList<BodyBite> bites,
            IReadOnlyList<BlockedBodyContact> contacts, bool[] separatedHeads)
            => Apply(fighters, bites, contacts, separatedHeads, out _);

        internal static bool Apply(PveFixedFighter[] fighters, IReadOnlyList<BodyBite> bites,
            IReadOnlyList<BlockedBodyContact> contacts, bool[] separatedHeads, out string playerDefender, bool multi = false)
        {
            playerDefender = null;
            string resolved = ""; bool hasResolved = false, playerBlocked = false;
            foreach (var contact in contacts)
            {
                if (hasResolved && contact.attacker == resolved) continue;
                int ai = -1, di = -1;
                for (int i = 0; i < fighters.Length; i++)
                { if (fighters[i].id == contact.attacker) ai = i; if (fighters[i].id == contact.defender) di = i; }
                if (ai < 0 || di < 0 || !fighters[ai].alive || !fighters[di].alive || fighters[ai].ProtectionTicks > 0 ||
                    fighters[di].ProtectionTicks > 0 || fighters[di].motion.Length <= PveFixedMotion.MinimumLength || separatedHeads[ai]) continue;
                int segment = contact.segment;
                if (multi)
                {
                    int beforeCount = 0;
                    foreach (var bite in bites) if (bite.defender == contact.defender)
                    {
                        if (contact.segment >= bite.segment && contact.segment < bite.segment + bite.removed)
                        { segment = -1; break; }
                        if (bite.segment + bite.removed <= contact.segment) beforeCount += bite.removed;
                    }
                    if (segment >= 0) segment -= beforeCount;
                }
                else foreach (var bite in bites) if (bite.defender == contact.defender)
                {
                    if (segment >= bite.segment && segment < bite.segment + bite.removed) { segment = -1; break; }
                    if (segment >= bite.segment + bite.removed) segment -= bite.removed;
                }
                var motion = fighters[di].motion;
                if (segment < 1 || segment >= motion.Length) continue;
                var point = motion.Segments[segment];
                long dx = (long)fighters[ai].motion.X - point.x, dy = (long)fighters[ai].motion.Y - point.y;
                if (dx * dx + dy * dy >= RadiusSquared) continue;
                var before = motion.Segments[segment - 1]; var after = motion.Segments[Math.Min(segment + 1, motion.Length - 1)];
                fighters[ai].motion.SeparateFrom(point, new CapturePoint(after.x - before.x, after.y - before.y));
                resolved = contact.attacker; hasResolved = true;
                if (resolved == "you") { playerBlocked = true; playerDefender = contact.defender; }
            }
            return playerBlocked;
        }
    }
}
