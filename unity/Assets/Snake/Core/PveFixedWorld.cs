#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    public readonly struct PveFixedFruit
    {
        public readonly CapturePoint position;
        public readonly int color;
        public PveFixedFruit(CapturePoint position, int color) { this.position = position; this.color = color; }
    }

    // Internal prototype replay only. The world rules are not frozen or
    // verified on the target platforms and must not authorize rewards.
    public static class PveFixedRecording
    {
        public const string ExperimentalRules = "fixed-pve-experiment-1";
        // Internal development markers, never release or rewards rulesets.
        public const string OldestExperimentalStrengthRules = "strength-pve-dev-1";
        public const string PreviousExperimentalStrengthRules = "strength-pve-dev-2";
        public const string ExperimentalStrengthRules = "strength-pve-dev-3";
        public const int MaxTicks = 90 * PveFixedWorld.TickRate;
        public static bool TryReplay(int seed, bool firstRun, PlayerProgress profile, byte[] inputs, out PveFixedWorld result)
            => TryReplayInternal(seed, firstRun, profile, inputs, false, null, out result);

        // Diagnostic only: this mutable prototype is not a versioned ruleset
        // and must not be used to verify accounts, rankings, or rewards.
        public static bool TryReplayStrength(int seed, bool firstRun, byte[] inputs, out PveFixedWorld result)
            => TryReplayInternal(seed, firstRun, null, inputs, true, null, out result);

        public static bool TryReplayStrength(int seed, StrengthEvalStartConfig snapshot, byte[] inputs, out PveFixedWorld result)
        {
            result = null;
            if (snapshot == null || !snapshot.Supported) return false;
            return TryReplayInternal(seed, snapshot.firstRun, null, inputs, true, snapshot, out result);
        }

        private static bool TryReplayInternal(int seed, bool firstRun, PlayerProgress profile, byte[] inputs, bool strength, StrengthEvalStartConfig snapshot, out PveFixedWorld result)
        {
            result = null;
            int limit = (firstRun ? 60 : 90) * PveFixedWorld.TickRate;
            if (inputs == null || inputs.Length == 0 || inputs.Length % 2 != 0 || inputs.Length > limit * 2) return false;
            for (int i = 1; i < inputs.Length; i += 2) if (inputs[i] > 1) return false;
            var world = new PveFixedWorld(seed, firstRun, profile, strengthPreview: strength, strengthSnapshot: snapshot);
            for (int i = 0; i < inputs.Length; i += 2)
            {
                if (world.Finished) return false;
                world.Step(inputs[i], inputs[i + 1] == 1);
            }
            if (!world.Finished) return false;
            result = world;
            return true;
        }
    }

    // Isolated first draft of a deterministic ordinary PvE match. This is
    // not yet a versioned release ruleset or a trusted reward verifier.
    public sealed class PveFixedWorld
    {
        public const int TickRate = 20;
        private uint random;
        private readonly int maxTicks;
        private readonly int eventType;
        private int eventTarget = -1, starHolder = -1, starTicks;
        private CapturePoint eventCenter, star;
        private bool eventActive, eventDone;
        public string EventKind => !eventActive && !eventDone ? "" : eventType == 0 ? "bounty" : eventType == 1 ? "star" : "fruit";
        public string EventStatus => eventActive ? "active" : eventDone ? "ended" : "";
        public string EventTarget => eventActive && eventTarget >= 0 ? fighters[eventTarget].id : "";
        public CapturePoint EventCenter => eventCenter;
        public CapturePoint StarPosition => star;
        public string StarHolder => eventActive && starHolder >= 0 ? fighters[starHolder].id : "";
        public int StarTicks => starTicks;
        public int EventTicksRemaining => eventActive ? Math.Max(0, 800 - Tick) : 0;
        private readonly PveFixedBonuses playerBonuses;
        private readonly PveFixedFighter[] fighters = new PveFixedFighter[8];
        private readonly PveFixedCombatWorkspace combatWorkspace = new PveFixedCombatWorkspace(8);
        private readonly StrengthBodyCombat strengthCombat = new StrengthBodyCombat();
        private readonly StrengthInputs[] strengthInputs = new StrengthInputs[8];
        private readonly StrengthValues[] strengthHeadValues = new StrengthValues[8];
        private readonly int[] strengthHeadWinners = new int[8], strengthHeadGrowth = new int[8];
        private readonly long[] strengthHeadAdvantages = new long[8];
        private readonly bool[] strengthSeparatedHeads = new bool[8];
        private readonly List<HeadTieContact> strengthHeadTies = new List<HeadTieContact>();
        public bool StrengthPreview { get; }
        public int StrengthBasePower { get; }
        public int StrengthHardness { get; }
        public int LastPlayerBiteTick { get; private set; } = -1;
        public int LastPlayerBiteCount { get; private set; }
        public bool LastPlayerWasBitten { get; private set; }
        public int LastPlayerAttackTick { get; private set; } = -1;
        public int LastPlayerAttackCount { get; private set; }
        public int LastPlayerDamageTick { get; private set; } = -1;
        public int LastPlayerDamageCount { get; private set; }
        public int PlayerSegmentsEaten { get; private set; }
        public int PlayerSegmentsLostToBites { get; private set; }
        public CapturePoint LastPlayerAttackPoint { get; private set; }
        public CapturePoint LastPlayerDamagePoint { get; private set; }
        public int LastPlayerBlockedTick { get; private set; } = -1;
        public int LastPlayerHeadTieTick { get; private set; } = -1;
        public int LastPlayerLevelUpTick { get; private set; } = -1;
        public int LastPlayerLevelUp { get; private set; }
        private readonly int[] strengthBuffTicks = new int[8];
        private readonly int[] strengthExperience = new int[8];
        private readonly int[] strengthLoopTicks = new int[8], strengthLoopCooldown = new int[8];
        private readonly CaptureLoop[] strengthLoops = new CaptureLoop[8];
        private readonly int[] strengthLoopTargets = new int[8];
        private int nextStrengthPickupTick, strengthPickupExpires, strengthPickupAppearsAt;
        public bool StrengthPickupPending { get; private set; }
        public int StrengthPickupPendingTicks => StrengthPickupPending ? Math.Max(0, strengthPickupAppearsAt - Tick) : 0;
        public bool StrengthPickupVisible { get; private set; }
        public CapturePoint StrengthPickupPosition { get; private set; }
        private readonly int[] botTimer = new int[8];
        private readonly byte[] botDirection = new byte[8];
        private readonly bool[] botBoost = new bool[8];
        private readonly bool[] previouslyAlive = new bool[8];
        private ArenaState projection;
        private int lastCaptureTick = -1, lastCaptureCount;
        private string lastCaptureOwner = "";
        private readonly List<CapturePoint> lastCapturePolygon = new List<CapturePoint>(Arena.MaximumLength + 1);
        private readonly List<PveFixedFruit> fruit = new List<PveFixedFruit>(Arena.FruitCount);
        public IReadOnlyList<PveFixedFighter> Fighters => fighters;
        public IReadOnlyList<PveFixedFruit> Fruit => fruit;
        public int Tick { get; private set; }
        public bool Finished { get; private set; }
        public string Winner { get; private set; } = "";
        public PveFixedWorld(int seed, bool firstRun, PlayerProgress profile, bool strengthPreview = false, StrengthEvalStartConfig strengthSnapshot = null)
        {
            if (strengthSnapshot != null && (!strengthPreview || !strengthSnapshot.Supported || strengthSnapshot.firstRun != firstRun))
                throw new ArgumentException("Invalid strength snapshot", nameof(strengthSnapshot));
            StrengthPreview = strengthPreview;
            StrengthBasePower = strengthSnapshot != null ? strengthSnapshot.EffectivePower : 100;
            StrengthHardness = strengthSnapshot != null ? strengthSnapshot.EffectiveHardness : 0;
            random = unchecked((uint)seed); if (random == 0) random = 0x6d2b79f5;
            maxTicks = (firstRun ? 60 : 90) * TickRate;
            uint selection = unchecked((uint)seed); eventType = (int)((selection ^ (selection >> 16)) % 3);
            // Movement remains neutral; only the isolated server snapshot can
            // supply candidate player strength. Talents and skins do not apply.
            playerBonuses = StrengthPreview ? new PveFixedBonuses() : PveFixedBonuses.From(profile);
            for (int i = 0; i < Arena.FruitCount; i++) fruit.Add(NewFruit());
            for (int i = 0; i < fighters.Length; i++)
            {
                CapturePoint head = Spawn(i);
                int heading = Next(256);
                fighters[i] = new PveFixedFighter(i == 0 ? "you" : "bot-" + i,
                    new PveFixedMotion(head.x, head.y, heading * 256, DailyMatch.CaptureStartingLength)) { shieldTicks = (firstRun && i == 0 ? 4 : 3) * TickRate };
                botDirection[i] = (byte)heading;
            }
            if (StrengthPreview) nextStrengthPickupTick = 100 + Next(61);
        }
        private int Next(int max)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return (int)(random % (uint)max);
        }
        private PveFixedFruit NewFruit() => new PveFixedFruit(new CapturePoint(Next(216001) - 108000, Next(216001) - 108000), Next(4));
        private static long DistanceSquared(int x, int y) => (long)x * x + (long)y * y;
        private CapturePoint Spawn(int index)
        {
            CapturePoint candidate = default;
            for (int attempt = 0; attempt < 80; attempt++)
            {
                if (index > 0 && index <= 2)
                {
                    byte angle = (byte)Next(256); int radius = (index == 1 ? 9000 : 12000) + Next(2001);
                    candidate = new CapturePoint(fighters[0].motion.X + PveFixedMotion.DirectionX(angle) * radius / 10000,
                        fighters[0].motion.Y + PveFixedMotion.DirectionY(angle) * radius / 10000);
                }
                else candidate = new CapturePoint(Next(72001) - 36000, Next(72001) - 36000);
                bool clear = true;
                for (int j = 0; j < index; j++)
                    if (DistanceSquared(candidate.x - fighters[j].motion.X, candidate.y - fighters[j].motion.Y) < 8000L * 8000L) { clear = false; break; }
                if (clear) break;
            }
            return new CapturePoint(Math.Max(-PveFixedMotion.Edge, Math.Min(PveFixedMotion.Edge, candidate.x)),
                Math.Max(-PveFixedMotion.Edge, Math.Min(PveFixedMotion.Edge, candidate.y)));
        }
        // Treat a nearly expired shield as an approaching hazard so bots do
        // not overlap when immunity ends. Distant shielded snakes are safe.
        internal static void AccumulateTrailAvoidance(PveFixedFighter self, IReadOnlyList<PveFixedFighter> opponents, ref long vx, ref long vy)
        {
            if (self == null || !self.alive || self.shieldTicks > TickRate) return;
            for (int i = 0; i < opponents.Count; i++)
            {
                var other = opponents[i];
                if (other == null || other == self || !other.alive || other.shieldTicks > TickRate) continue;
                for (int part = 2; part < other.motion.Length; part += 2)
                {
                    var point = other.motion.Segments[part];
                    int dx = self.motion.X - point.x, dy = self.motion.Y - point.y;
                    int distance = PveFixedMotion.Distance(dx, dy);
                    if (distance <= 10 || distance >= 5000) continue;
                    long weight = (5000 - distance) * 8L;
                    vx += dx * weight / distance; vy += dy * weight / distance;
                }
            }
        }
        // Equal or longer heads can eliminate this bot. Avoid the current
        // position, including shortly before shields expire; no future input
        // or special player immunity is involved.
        internal static void AccumulateHeadAvoidance(PveFixedFighter self, IReadOnlyList<PveFixedFighter> opponents, ref long vx, ref long vy)
        {
            if (self == null || !self.alive || self.shieldTicks > TickRate) return;
            for (int i = 0; i < opponents.Count; i++)
            {
                var other = opponents[i];
                if (other == null || other == self || !other.alive || other.shieldTicks > TickRate || other.motion.Length < self.motion.Length) continue;
                int dx = self.motion.X - other.motion.X, dy = self.motion.Y - other.motion.Y;
                int distance = PveFixedMotion.Distance(dx, dy);
                if (distance <= 10 || distance >= 7000) continue;
                long weight = (7000 - distance) * 8L;
                vx += dx * weight / distance; vy += dy * weight / distance;
            }
        }
        private byte BotHeading(int index)
        {
            if (--botTimer[index] > 0) return StrengthPreview ? AvoidLethalHead(index, botDirection[index]) : botDirection[index];
            botTimer[index] = 6 + Next(8);
            var self = fighters[index]; int nearest = -1; long closest = long.MaxValue;
            for (int i = 0; i < fighters.Length; i++)
            {
                if (i == index || !fighters[i].alive) continue;
                long distance = DistanceSquared(fighters[i].motion.X - self.motion.X, fighters[i].motion.Y - self.motion.Y);
                if (distance < closest) { closest = distance; nearest = i; }
            }
            long vx = 0, vy = 0;
            bool strengthObjective = false;
            if (nearest >= 0 && !StrengthPreview)
            {
                int sign = self.motion.Length >= fighters[nearest].motion.Length || closest > 12000L * 12000L ? 1 : -1;
                vx += (long)(fighters[nearest].motion.X - self.motion.X) * sign;
                vy += (long)(fighters[nearest].motion.Y - self.motion.Y) * sign;
            }
            if (StrengthPreview)
            {
                // Aim for the nearest currently edible body segment; do not
                // treat every opponent body as a lethal trail.
                var own = StrengthFor(self); BodyStrength.TryCalculate(own, 0, out var attack);
                long bestTarget = 18000L * 18000L;
                foreach (var other in fighters)
                {
                    if (other == self || !other.alive || other.shieldTicks > 0 || other.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    var defense = StrengthFor(other);
                    for (int part = 1; part < other.motion.Length; part++)
                    {
                        var p = other.motion.Segments[part];
                        long dx = (long)p.x - self.motion.X, dy = (long)p.y - self.motion.Y, distance = dx * dx + dy * dy;
                        if (distance >= bestTarget) continue;
                        BodyStrength.TryCalculate(defense, part, out var body);
                        if (!BodyStrength.CanEat(attack.attack, body.defense)) continue;
                        bestTarget = distance; vx = dx * 2; vy = dy * 2; strengthObjective = true;
                    }
                }
                if (StrengthPickupVisible || StrengthPickupPending)
                {
                    long dx = (long)StrengthPickupPosition.x - self.motion.X, dy = (long)StrengthPickupPosition.y - self.motion.Y;
                    if (dx * dx + dy * dy < 18000L * 18000L) { vx += dx * 2; vy += dy * 2; strengthObjective = true; }
                }
            }
            else { AccumulateTrailAvoidance(self, fighters, ref vx, ref vy); AccumulateHeadAvoidance(self, fighters, ref vx, ref vy); }
            if (nearest < 0 || closest > 10000L * 10000L || (StrengthPreview && !strengthObjective))
            {
                long closestFruit = 18000L * 18000L; int fruitIndex = -1;
                for (int i = 0; i < fruit.Count; i++)
                {
                    long d = DistanceSquared(fruit[i].position.x - self.motion.X, fruit[i].position.y - self.motion.Y);
                    if (d < closestFruit) { closestFruit = d; fruitIndex = i; }
                }
                if (fruitIndex >= 0)
                { vx += (fruit[fruitIndex].position.x - self.motion.X) / 2; vy += (fruit[fruitIndex].position.y - self.motion.Y) / 2; }
            }
            botBoost[index] = nearest >= 0 && closest < 15000L * 15000L && self.motion.Length > fighters[nearest].motion.Length + 3;
            // Two hunters favor the actual player, using only present position.
            // Their ordinary boost costs length and remains optional for the bot.
            if (!StrengthPreview && index <= 2 && fighters[0].alive && !(eventActive && eventType == 1))
            {
                long dx = (long)fighters[0].motion.X - self.motion.X, dy = (long)fighters[0].motion.Y - self.motion.Y;
                long distance = dx * dx + dy * dy;
                if (distance < 50000L * 50000L && (distance > 12000L * 12000L || self.motion.Length >= fighters[0].motion.Length))
                {
                    vx += dx * 3; vy += dy * 3;
                    if (distance > 12000L * 12000L && distance < 40000L * 40000L && self.motion.Length > 12) botBoost[index] = true;
                }
            }
            if (eventActive && eventType == 1)
            {
                if (starHolder >= 0 && starHolder != index && fighters[starHolder].alive)
                {
                    long dx = fighters[starHolder].motion.X - self.motion.X, dy = fighters[starHolder].motion.Y - self.motion.Y;
                    if (dx * dx + dy * dy < 45000L * 45000L) { vx += dx * 4; vy += dy * 4; }
                }
                else if (starHolder == index && nearest >= 0 && closest < 18000L * 18000L)
                { vx += (long)(self.motion.X - fighters[nearest].motion.X) * 4; vy += (long)(self.motion.Y - fighters[nearest].motion.Y) * 4; }
                else if (starHolder < 0 && DistanceSquared(star.x - self.motion.X, star.y - self.motion.Y) < 45000L * 45000L)
                { vx += (long)(star.x - self.motion.X) * 2; vy += (long)(star.y - self.motion.Y) * 2; }
            }
            if (Math.Abs(self.motion.X) > 95000) vx -= (long)self.motion.X * 4;
            if (Math.Abs(self.motion.Y) > 95000) vy -= (long)self.motion.Y * 4;
            byte wander = (byte)Next(256);
            vx += PveFixedMotion.DirectionX(wander) / 2; vy += PveFixedMotion.DirectionY(wander) / 2;
            long best = long.MinValue; int heading = botDirection[index];
            for (int a = 0; a < 256; a++)
            {
                long dot = vx * PveFixedMotion.DirectionX((byte)a) + vy * PveFixedMotion.DirectionY((byte)a);
                if (dot > best) { best = dot; heading = a; }
            }
            botDirection[index] = (byte)heading;
            return StrengthPreview ? AvoidLethalHead(index, botDirection[index]) : botDirection[index];
        }
        // Check every step, including between normal AI planning updates. Only
        // currently lethal head contacts cause a retreat; edible bodies remain
        // valid targets. Both sides use the same comparison as head combat.
        private byte AvoidLethalHead(int index, byte planned)
        {
            var self = fighters[index];
            if (!self.alive || self.shieldTicks > 1 || !BodyStrength.TryCalculate(StrengthFor(self), 0, out var own)) return planned;
            long vx = 0, vy = 0;
            foreach (var other in fighters)
            {
                if (other == self || !other.alive || other.shieldTicks > 1) continue;
                int dx = self.motion.X - other.motion.X, dy = self.motion.Y - other.motion.Y;
                if ((long)dx * dx + (long)dy * dy >= 3500L * 3500L) continue;
                int distance = PveFixedMotion.Distance(dx, dy);
                if (!BodyStrength.TryCalculate(StrengthFor(other), 0, out var threat)) continue;
                long incoming = (long)threat.attack * 100 - (long)own.defense * 105;
                long outgoing = (long)own.attack * 100 - (long)threat.defense * 105;
                if (incoming <= 0 || incoming <= outgoing) continue;
                byte otherHeading = (byte)(other.motion.Angle >> 8);
                long toward = (long)dx * PveFixedMotion.DirectionX(otherHeading) + (long)dy * PveFixedMotion.DirectionY(otherHeading);
                if (distance > 2200 && toward <= 0) continue;
                if (distance == 0) { dx = string.CompareOrdinal(self.id, other.id) < 0 ? -1 : 1; dy = 0; distance = 1; }
                long weight = 3500 - distance;
                vx += dx * weight / distance; vy += dy * weight / distance;
            }
            if (vx == 0 && vy == 0) return planned;
            if (Math.Abs(self.motion.X) > 105000) vx -= self.motion.X / 4;
            if (Math.Abs(self.motion.Y) > 105000) vy -= self.motion.Y / 4;
            long best = long.MinValue; int heading = planned;
            for (int a = 0; a < 256; a++)
            {
                long dot = vx * PveFixedMotion.DirectionX((byte)a) + vy * PveFixedMotion.DirectionY((byte)a);
                if (dot > best) { best = dot; heading = a; }
            }
            return (byte)heading;
        }
        private static int ClampEvent(int value) => Math.Max(-107280, Math.Min(107280, value));
        private void StartEvent()
        {
            eventActive = true;
            var player = fighters[0].motion;
            if (eventType == 0)
            {
                var candidates = new List<int>();
                for (int i = 1; i < fighters.Length; i++) if (fighters[i].alive) candidates.Add(i);
                if (candidates.Count == 0) { EndEvent(); return; }
                eventTarget = candidates[Next(candidates.Count)];
                fighters[eventTarget].motion.Grow(Math.Max(0, 20 - fighters[eventTarget].motion.Length));
            }
            else if (eventType == 1)
            {
                byte direction = (byte)Next(256);
                star = new CapturePoint(ClampEvent(player.X + PveFixedMotion.DirectionX(direction) * 9000 / 10000),
                    ClampEvent(player.Y + PveFixedMotion.DirectionY(direction) * 9000 / 10000));
                starHolder = -1; starTicks = 0;
            }
            else
            {
                byte direction = (byte)(player.Angle >> 8);
                eventCenter = new CapturePoint(ClampEvent(player.X + PveFixedMotion.DirectionX(direction)),
                    ClampEvent(player.Y + PveFixedMotion.DirectionY(direction)));
                for (int i = 0; i < Math.Min(12, fruit.Count); i++)
                {
                    byte angle = (byte)Next(256); int radius = 2000 + Next(10001);
                    fruit[i] = new PveFixedFruit(new CapturePoint(ClampEvent(eventCenter.x + PveFixedMotion.DirectionX(angle) * radius / 10000),
                        ClampEvent(eventCenter.y + PveFixedMotion.DirectionY(angle) * radius / 10000)), Next(4));
                }
            }
        }
        private void EndEvent() { eventActive = false; eventDone = true; eventTarget = -1; starHolder = -1; starTicks = 0; }
        public static void AwardEventElimination(PveFixedFighter owner, PveFixedFighter victim, string kind, string target, CapturePoint center)
        {
            if (owner == null || victim == null || !victim.credited || victim.eliminatedBy != owner.id) return;
            int points = kind == "bounty" && victim.id == target ? 2 : kind == "fruit" &&
                DistanceSquared(owner.motion.X - center.x, owner.motion.Y - center.y) <= 14000L * 14000L ? 1 : 0;
            owner.eventPoints = Math.Min(2, owner.eventPoints + points);
        }
        private void UpdateEvent()
        {
            if (!eventActive) return;
            if (Tick >= 800 || (eventType == 0 && (eventTarget < 0 || !fighters[eventTarget].alive))) { EndEvent(); return; }
            if (eventType != 1) return;
            if (starHolder >= 0)
            {
                var holder = fighters[starHolder]; star = holder.motion.Segments[0];
                if (!holder.alive) { starHolder = -1; starTicks = 0; }
                else if (++starTicks >= 8 * TickRate)
                {
                    holder.eventPoints = Math.Min(2, holder.eventPoints + 1); starTicks = 0;
                    if (holder.eventPoints >= 2) { EndEvent(); return; }
                }
            }
            if (starHolder < 0)
                for (int i = 0; i < fighters.Length; i++)
                    if (fighters[i].alive && DistanceSquared(fighters[i].motion.X - star.x, fighters[i].motion.Y - star.y) <= 1500L * 1500L)
                    { starHolder = i; break; }
        }
        // Renderer-facing projection only. Integer simulation never reads it;
        // objects are reused so a renderer may request it once per frame.
        public ArenaState RenderState()
        {
            if (projection == null)
            {
                projection = new ArenaState { halfSize = Arena.WorldHalfSize };
                for (int i = 0; i < fighters.Length; i++)
                    projection.snakes.Add(new SnakeState { id = fighters[i].id, name = i == 0 ? "玩家" : "AI " + i, bot = i != 0, color = i });
                for (int i = 0; i < fruit.Count; i++) projection.fruits.Add(new FruitState());
            }
            projection.tick = Tick; projection.remaining = Math.Max(0, maxTicks - Tick) / (float)TickRate;
            projection.phase = Finished ? "ended" : "running"; projection.winner = Winner;
            projection.eventKind = EventKind; projection.eventStatus = EventStatus; projection.eventTarget = EventTarget;
            projection.strengthPickupVisible = StrengthPreview && StrengthPickupVisible;
            projection.strengthPickupPending = StrengthPreview && !Finished && StrengthPickupPending;
            projection.strengthPickupPendingSeconds = (StrengthPickupPendingTicks + TickRate - 1) / TickRate;
            projection.strengthPickupPosition = ToPoint(StrengthPickupPosition);
            projection.eventPosition = ToPoint(eventCenter); projection.eventRadius = eventActive && eventType == 2 ? 14f : 0f;
            projection.eventRemaining = EventTicksRemaining / (float)TickRate;
            projection.starVisible = eventActive && eventType == 1; projection.starPosition = ToPoint(star);
            projection.starHolder = StarHolder; projection.starProgress = starTicks / (8f * TickRate);
            var viewer = fighters[0];
            StrengthValues viewerPower = default;
            bool canCompare = StrengthPreview && !Finished && viewer.alive && viewer.shieldTicks <= 0 &&
                BodyStrength.TryCalculate(StrengthFor(viewer), 0, out viewerPower);
            for (int i = 0; i < fighters.Length; i++)
            {
                var source = fighters[i]; var target = projection.snakes[i];
                target.alive = source.alive; target.shield = source.shieldTicks / (float)TickRate;
                target.kills = source.kills; target.starPoints = source.eventPoints;
                target.eliminatedBy = source.eliminatedBy; target.eliminationReason = source.eliminationReason;
                target.angle = source.motion.Angle * (float)(Math.PI * 2 / 65536); target.boosting = source.alive && source.motion.Boosting;
                target.rebound = source.motion.ReboundTicks / (float)TickRate;
                target.segments.Clear(); target.strengthWeakSegments.Clear(); target.strengthThreatSegments.Clear();
                bool eligible = canCompare && i != 0 && source.alive && source.shieldTicks <= 0 &&
                    source.motion.Length > PveFixedMotion.MinimumLength && !strengthCombat.IsPairCoolingDown(Tick + 1L, viewer.id, source.id);
                var defendingPower = eligible ? StrengthFor(source) : default;
                for (int j = 0; j < source.motion.Length; j++)
                {
                    target.segments.Add(ToPoint(source.motion.Segments[j]));
                    bool weak = eligible && j > 0 && BodyStrength.TryCalculate(defendingPower, j, out var part) && BodyStrength.CanEat(viewerPower.attack, part.defense);
                    if (StrengthPreview) { target.strengthWeakSegments.Add(weak); target.strengthThreatSegments.Add(false); }
                }
            }
            // Indicate only the nearest actual player-body segment for each
            // nearby opponent. This is a current-state cue, not a prediction.
            if (canCompare && viewer.motion.Length > PveFixedMotion.MinimumLength)
            {
                var ownBody = StrengthFor(viewer);
                for (int i = 1; i < fighters.Length; i++)
                {
                    var other = fighters[i];
                    if (!other.alive || other.shieldTicks > 0 || strengthCombat.IsPairCoolingDown(Tick + 1L, other.id, viewer.id)) continue;
                    long headDx = (long)other.motion.X - viewer.motion.X, headDy = (long)other.motion.Y - viewer.motion.Y;
                    if (headDx * headDx + headDy * headDy < 1440L * 1440L) continue;
                    int closest = -1; long distance = 4500L * 4500L;
                    for (int j = 1; j < viewer.motion.Length; j++)
                    {
                        var point = viewer.motion.Segments[j];
                        long dx = (long)other.motion.X - point.x, dy = (long)other.motion.Y - point.y;
                        long candidate = dx * dx + dy * dy;
                        if (candidate < distance) { distance = candidate; closest = j; }
                    }
                    if (closest >= 0 && BodyStrength.TryCalculate(StrengthFor(other), 0, out var attack) &&
                        BodyStrength.TryCalculate(ownBody, closest, out var defense) && BodyStrength.CanEat(attack.attack, defense.defense))
                        projection.snakes[0].strengthThreatSegments[closest] = true;
                }
            }
            for (int i = 0; i < fruit.Count; i++) { projection.fruits[i].position = ToPoint(fruit[i].position); projection.fruits[i].color = fruit[i].color; }
            projection.strengthLevelUpTick = StrengthPreview ? LastPlayerLevelUpTick : -1;
            projection.strengthLevelUp = LastPlayerLevelUp;
            projection.strengthAttackTick = StrengthPreview ? LastPlayerAttackTick : -1;
            projection.strengthDamageTick = StrengthPreview ? LastPlayerDamageTick : -1;
            projection.strengthAttackPoint = ToPoint(LastPlayerAttackPoint);
            projection.strengthDamagePoint = ToPoint(LastPlayerDamagePoint);
            projection.captureTick = lastCaptureTick; projection.captureCount = lastCaptureCount; projection.captureOwner = lastCaptureOwner;
            projection.capturePolygon.Clear();
            for (int i = 0; i < lastCapturePolygon.Count; i++) projection.capturePolygon.Add(ToPoint(lastCapturePolygon[i]));
            return projection;
        }
        private static Point ToPoint(CapturePoint point) => new Point(point.x / 1000f, point.y / 1000f);

        private StrengthInputs StrengthFor(PveFixedFighter fighter) => new StrengthInputs {
            basePower = fighter.id == "you" ? StrengthBasePower : 100, length = fighter.motion.Length,
            hardness = fighter.id == "you" ? StrengthHardness : 0, matchLevel = StrengthLevel(fighter.id),
            speed = fighter.motion.Boosting ? 495 : fighter.motion.ReboundTicks > 0 ? 345 : 300,
            baseSpeed = 300, boosting = fighter.motion.Boosting,
            randomFactor = StrengthBuffTicks(fighter.id) > 0 ? 1120 : 1000,
            operationFactor = StrengthLoopTicks(fighter.id) > 0 ? 1100 : 1000
        };

        public int StrengthBuffTicks(string id)
        {
            if (!StrengthPreview || string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < fighters.Length; i++)
                if (fighters[i].id == id && fighters[i].alive) return strengthBuffTicks[i];
            return 0;
        }

        private static int StrengthLevelThreshold(int level) => level * level + 4 * level;
        private int StrengthIndex(string id)
        {
            if (!StrengthPreview || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < fighters.Length; i++) if (fighters[i].id == id && fighters[i].alive) return i;
            return -1;
        }
        public int StrengthLevel(string id)
        {
            int index = StrengthIndex(id); if (index < 0) return 0;
            int level = 0;
            while (level < 20 && strengthExperience[index] >= StrengthLevelThreshold(level + 1)) level++;
            return level;
        }
        public bool TryGetStrengthLevelProgress(string id, out int level, out int current, out int required)
        {
            level = current = required = 0;
            int index = StrengthIndex(id); if (index < 0) return false;
            level = StrengthLevel(id);
            if (level == 20) return true;
            current = strengthExperience[index] - StrengthLevelThreshold(level);
            required = StrengthLevelThreshold(level + 1) - StrengthLevelThreshold(level);
            return true;
        }
        private void AwardStrengthExperience(string id, int amount)
        {
            int index = StrengthIndex(id);
            if (index < 0 || amount <= 0) return;
            int before = StrengthLevel(id);
            strengthExperience[index] = (int)Math.Min(StrengthLevelThreshold(20), (long)strengthExperience[index] + amount);
            int after = StrengthLevel(id);
            if (id == "you" && after > before) { LastPlayerLevelUpTick = Tick; LastPlayerLevelUp = after; }
        }

        public int StrengthLoopTicks(string id)
        {
            if (!StrengthPreview || string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < fighters.Length; i++)
                if (fighters[i].id == id && fighters[i].alive) return strengthLoopTicks[i];
            return 0;
        }

        // Detect closures against a shared post-movement snapshot. A closed
        // loop only qualifies when it encloses another unprotected live head.
        private void DetectStrengthLoops()
        {
            Array.Clear(strengthLoops, 0, strengthLoops.Length);
            Array.Clear(strengthLoopTargets, 0, strengthLoopTargets.Length);
            for (int i = 0; i < fighters.Length; i++)
            {
                var owner = fighters[i];
                if (!owner.alive || owner.shieldTicks > 0 || strengthLoopCooldown[i] > 0 || !owner.motion.TryCapture(out var loop)) continue;
                for (int j = 0; j < fighters.Length; j++)
                    if (i != j && fighters[j].alive && fighters[j].shieldTicks == 0 && CaptureGeometry.Contains(loop.polygon, fighters[j].motion.Segments[0]))
                        strengthLoopTargets[i] |= 1 << j;
                if (strengthLoopTargets[i] != 0) strengthLoops[i] = loop;
            }
        }

        // Applied after this tick's combat: no closure can change the combat
        // that created it, and eliminated owners or targets do not award it.
        private void ApplyStrengthLoops()
        {
            for (int i = 0; i < fighters.Length; i++)
            {
                if (strengthLoopTicks[i] > 0) strengthLoopTicks[i]--;
                if (strengthLoopCooldown[i] > 0) strengthLoopCooldown[i]--;
            }
            int shown = -1;
            for (int i = 0; i < fighters.Length; i++)
            {
                if (!fighters[i].alive || strengthLoops[i] == null) continue;
                bool targetAlive = false;
                for (int j = 0; j < fighters.Length; j++)
                    if ((strengthLoopTargets[i] & (1 << j)) != 0 && fighters[j].alive) { targetAlive = true; break; }
                if (!targetAlive) continue;
                strengthLoopTicks[i] = 80; strengthLoopCooldown[i] = 200;
                if (shown < 0 || fighters[i].id == "you" || (fighters[shown].id != "you" && string.CompareOrdinal(fighters[i].id, fighters[shown].id) < 0)) shown = i;
            }
            if (shown >= 0)
            {
                lastCaptureTick = Tick; lastCaptureOwner = fighters[shown].id; lastCaptureCount = 0;
                lastCapturePolygon.Clear(); lastCapturePolygon.AddRange(strengthLoops[shown].polygon);
            }
        }

        private void UpdateStrengthPickupBeforeMovement()
        {
            for (int i = 0; i < strengthBuffTicks.Length; i++) if (strengthBuffTicks[i] > 0) strengthBuffTicks[i]--;
            if (StrengthPickupVisible && Tick >= strengthPickupExpires)
            { StrengthPickupVisible = false; nextStrengthPickupTick = Tick + 160 + Next(81); }
            if (StrengthPickupPending && Tick >= strengthPickupAppearsAt)
            {
                StrengthPickupPending = false; StrengthPickupVisible = true; strengthPickupExpires = Tick + 200;
            }
            if (!StrengthPickupVisible && !StrengthPickupPending && Tick >= nextStrengthPickupTick && fighters[0].alive)
            {
                int living = 0;
                for (int i = 0; i < fighters.Length; i++) if (fighters[i].alive) living++;
                int choice = Next(living), anchor = 0;
                for (int i = 0; i < fighters.Length; i++)
                    if (fighters[i].alive && choice-- == 0) { anchor = i; break; }
                byte angle = (byte)Next(256); int radius = 10000 + Next(6001);
                StrengthPickupPosition = new CapturePoint(ClampEvent(fighters[anchor].motion.X + PveFixedMotion.DirectionX(angle) * radius / 10000),
                    ClampEvent(fighters[anchor].motion.Y + PveFixedMotion.DirectionY(angle) * radius / 10000));
                // Announce the actual location for two seconds before it can
                // be collected. Bots and the player see the same location.
                StrengthPickupPending = true; strengthPickupAppearsAt = Tick + 2 * TickRate;
            }
        }

        private void CollectStrengthPickup()
        {
            if (!StrengthPickupVisible) return;
            int winner = -1; long closest = 1250L * 1250L + 1;
            for (int i = 0; i < fighters.Length; i++)
            {
                if (!fighters[i].alive) continue;
                long distance = DistanceSquared(fighters[i].motion.X - StrengthPickupPosition.x, fighters[i].motion.Y - StrengthPickupPosition.y);
                if (distance > 1250L * 1250L) continue;
                if (winner < 0 || distance < closest || (distance == closest && string.CompareOrdinal(fighters[i].id, fighters[winner].id) < 0))
                { winner = i; closest = distance; }
            }
            if (winner < 0) return;
            strengthBuffTicks[winner] = 120; // Six seconds; refresh, do not stack.
            StrengthPickupVisible = false; nextStrengthPickupTick = Tick + 160 + Next(81);
        }

        // Additional internal state for development-only cross-runtime vectors.
        // Scratch buffers and renderer projections are deliberately excluded.
        internal void AppendReplayDiagnostics(Action<int> add)
        {
            add(unchecked((int)random)); add(maxTicks); add(eventType);
            add(eventTarget); add(starHolder); add(starTicks);
            add(eventCenter.x); add(eventCenter.y); add(star.x); add(star.y);
            add(eventActive ? 1 : 0); add(eventDone ? 1 : 0);
            add(nextStrengthPickupTick); add(strengthPickupExpires); add(strengthPickupAppearsAt); add(StrengthPickupPending ? 1 : 0);
            for (int i = 0; i < fighters.Length; i++)
            {
                add(botTimer[i]); add(botDirection[i]); add(botBoost[i] ? 1 : 0);
                add(previouslyAlive[i] ? 1 : 0); add(strengthExperience[i]);
                add(strengthBuffTicks[i]); add(strengthLoopTicks[i]); add(strengthLoopCooldown[i]);
                add(fighters[i].captureCooldown); add(fighters[i].eventPoints); add(fighters[i].credited ? 1 : 0);
                fighters[i].motion.AppendReplayDiagnostics(add);
            }
            strengthCombat.AppendReplayDiagnostics(fighters, add);
        }

        public bool TryGetStrength(string id, out StrengthValues head, out int tailDefense)
        {
            head = default; tailDefense = 0;
            if (!StrengthPreview || string.IsNullOrEmpty(id)) return false;
            foreach (var fighter in fighters)
            {
                if (fighter.id != id || !fighter.alive) continue;
                var input = StrengthFor(fighter);
                if (!BodyStrength.TryCalculate(input, 0, out head) || !BodyStrength.TryCalculate(input, input.length - 1, out var tail)) return false;
                tailDefense = tail.defense; return true;
            }
            return false;
        }

        // Presentation hint only: compare the same snapshot and tie band as
        // head combat. It does not predict movement or guarantee an outcome.
        public bool TryFindNearbyHeadThreat(string playerId, out string threatId)
        {
            threatId = "";
            if (!StrengthPreview || Finished || !TryGetStrength(playerId, out var ownPower, out _)) return false;
            PveFixedFighter player = null;
            foreach (var fighter in fighters) if (fighter.id == playerId) { player = fighter; break; }
            if (player == null || !player.alive || player.shieldTicks > 0) return false;
            long closest = 6000L * 6000L + 1;
            foreach (var other in fighters)
            {
                if (other == player || !other.alive || other.shieldTicks > 0 ||
                    !TryGetStrength(other.id, out var otherPower, out _)) continue;
                long dx = (long)player.motion.X - other.motion.X, dy = (long)player.motion.Y - other.motion.Y;
                long distance = dx * dx + dy * dy;
                if (distance > 6000L * 6000L) continue;
                long incoming = (long)otherPower.attack * 100 - (long)ownPower.defense * 105;
                long outgoing = (long)ownPower.attack * 100 - (long)otherPower.defense * 105;
                if (incoming <= 0 || incoming <= outgoing) continue;
                if (distance < closest || (distance == closest && string.CompareOrdinal(other.id, threatId) < 0))
                { closest = distance; threatId = other.id; }
            }
            return threatId.Length > 0;
        }

        private void RecordStrengthBite(BodyBite bite)
        {
            AwardStrengthExperience(bite.attacker, bite.removed * 2);
            if (bite.attacker == "you")
            { LastPlayerAttackTick = Tick; LastPlayerAttackCount = bite.removed; LastPlayerAttackPoint = bite.point; PlayerSegmentsEaten += bite.removed; }
            if (bite.defender == "you")
            { LastPlayerDamageTick = Tick; LastPlayerDamageCount = bite.removed; LastPlayerDamagePoint = bite.point; PlayerSegmentsLostToBites += bite.removed; }
            if (bite.attacker == "you" || bite.defender == "you")
            { LastPlayerBiteTick = Tick; LastPlayerBiteCount = bite.removed; LastPlayerWasBitten = bite.defender == "you"; }
        }

        public void Step(byte heading, bool boost)
        {
            if (Finished) return;
            Tick++;
            if (StrengthPreview) UpdateStrengthPickupBeforeMovement();
            if (!StrengthPreview && Tick == 15 * TickRate && !eventDone) StartEvent();
            for (int i = 0; i < fighters.Length; i++) previouslyAlive[i] = fighters[i].alive;
            for (int i = 0; i < fighters.Length; i++)
            {
                var fighter = fighters[i]; if (!fighter.alive) continue;
                if (fighter.shieldTicks > 0) fighter.shieldTicks--;
                fighter.motion.Step(i == 0 ? heading : BotHeading(i), i == 0 ? boost : botBoost[i],
                    i == 0 ? playerBonuses : new PveFixedBonuses());
            }
            if (StrengthPreview)
            {
                CollectStrengthPickup();
                DetectStrengthLoops();
                for (int i = 0; i < fighters.Length; i++) strengthInputs[i] = StrengthFor(fighters[i]);
                var headGrowth = StrengthHeadCombat.ResolveBuffered(fighters, strengthInputs, true, strengthHeadTies,
                    strengthHeadValues, strengthHeadWinners, strengthHeadAdvantages, strengthHeadGrowth);
                var bites = strengthCombat.ResolveBuffered(Tick, fighters, strengthInputs);
                for (int i = 0; i < fighters.Length; i++) if (fighters[i].alive) fighters[i].motion.Grow(headGrowth[i]);
                ApplyStrengthLoops();
                var separatedHeads = StrengthHeadCombat.SeparateTiesBuffered(fighters, strengthHeadTies, bites, strengthSeparatedHeads);
                if (separatedHeads[0]) LastPlayerHeadTieTick = Tick;
                var blockedContacts = strengthCombat.BlockedContacts;
                for (int contactIndex = 0; contactIndex < blockedContacts.Count; contactIndex++)
                {
                    var contact = blockedContacts[contactIndex];
                    bool bit = false;
                    foreach (var bite in bites) if (bite.attacker == contact.attacker) { bit = true; break; }
                    if (bit) continue;
                    bool headSeparated = false;
                    for (int i = 0; i < fighters.Length; i++) if (fighters[i].id == contact.attacker && separatedHeads[i]) { headSeparated = true; break; }
                    if (headSeparated) continue;
                    if (contact.attacker == "you") LastPlayerBlockedTick = Tick;
                    foreach (var fighter in fighters)
                        if (fighter.alive && fighter.id == contact.attacker) { fighter.motion.SeparateFrom(contact.point, contact.tangent); break; }
                }
                foreach (var bite in bites) RecordStrengthBite(bite);
                for (int i = 0; i < fighters.Length; i++)
                    if (previouslyAlive[i] && !fighters[i].alive && fighters[i].credited)
                        AwardStrengthExperience(fighters[i].eliminatedBy, 5);
            }
            else PveFixedCombat.Resolve(fighters, combatWorkspace);
            if (!StrengthPreview && combatWorkspace.captureOwner.Length != 0)
            {
                lastCaptureTick = Tick; lastCaptureCount = combatWorkspace.captureCount; lastCaptureOwner = combatWorkspace.captureOwner;
                lastCapturePolygon.Clear(); lastCapturePolygon.AddRange(combatWorkspace.capturePolygon);
            }
            if (eventActive)
                for (int i = 0; i < fighters.Length; i++)
                    if (previouslyAlive[i] && !fighters[i].alive && fighters[i].credited)
                        for (int j = 0; j < fighters.Length; j++)
                            if (fighters[j].id == fighters[i].eliminatedBy)
                            { AwardEventElimination(fighters[j], fighters[i], EventKind, EventTarget, eventCenter); break; }
            for (int i = 0; i < fruit.Count; i++)
                for (int j = 0; j < fighters.Length; j++)
                {
                    var fighter = fighters[j]; if (!fighter.alive) continue;
                    int radius = 1250 * (10000 + (j == 0 ? playerBonuses.pickup : 0)) / 10000;
                    if (DistanceSquared(fighter.motion.X - fruit[i].position.x, fighter.motion.Y - fruit[i].position.y) > (long)radius * radius) continue;
                    fighter.motion.Grow(1);
                    if (StrengthPreview) AwardStrengthExperience(fighter.id, 1);
                    fruit[i] = NewFruit(); break;
                }
            UpdateEvent();
            int living = 0, best = -1; bool tied = false;
            for (int i = 0; i < fighters.Length; i++)
            {
                if (!fighters[i].alive) continue;
                living++;
                if (best < 0 || fighters[i].motion.Length > fighters[best].motion.Length ||
                    (fighters[i].motion.Length == fighters[best].motion.Length && fighters[i].kills > fighters[best].kills)) { best = i; tied = false; }
                else if (fighters[i].motion.Length == fighters[best].motion.Length && fighters[i].kills == fighters[best].kills) tied = true;
            }
            if (!fighters[0].alive || living <= 1 || Tick >= maxTicks)
            { Finished = true; Winner = best >= 0 && !tied ? fighters[best].id : ""; if (eventActive) EndEvent(); }
        }
    }

    // Non-cryptographic terminal diagnostic for the local, no-reward strength
    // experiment. This is not an integrity proof or a complete simulation hash.
    public static class StrengthStateDigest
    {
        public static bool Matches(PveFixedWorld world, string digest) => !string.IsNullOrEmpty(digest) && world != null &&
            world.StrengthPreview && world.Finished && string.Equals(digest, Compute(world), StringComparison.Ordinal);
        public static string Compute(PveFixedWorld world)
        {
            if (world == null || !world.StrengthPreview || !world.Finished) throw new ArgumentException("Expected a completed strength preview");
            ulong hash = 14695981039346656037UL;
            void AddInt(int value)
            {
                unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; } }
            }
            void AddString(string value)
            {
                if (value == null) { AddInt(-1); return; }
                AddInt(value.Length); foreach (char c in value) AddInt(c);
            }
            AddInt(world.Tick); AddString(world.Winner); AddInt(world.StrengthBasePower); AddInt(world.StrengthHardness);
            AddInt(world.StrengthPickupVisible ? 1 : 0); AddInt(world.StrengthPickupPosition.x); AddInt(world.StrengthPickupPosition.y);
            AddInt(world.LastPlayerAttackTick); AddInt(world.LastPlayerAttackCount); AddInt(world.LastPlayerDamageTick); AddInt(world.LastPlayerDamageCount);
            AddInt(world.LastPlayerBlockedTick); AddInt(world.LastPlayerHeadTieTick);
            foreach (var fighter in world.Fighters)
            {
                AddString(fighter.id); AddInt(fighter.alive ? 1 : 0); AddInt(fighter.shieldTicks); AddInt(fighter.kills);
                AddString(fighter.eliminatedBy); AddString(fighter.eliminationReason); AddInt(fighter.motion.Angle);
                AddInt(fighter.motion.Length); AddInt(fighter.motion.ReboundTicks); AddInt(fighter.motion.ReboundCooldown);
                AddInt(fighter.motion.Boosting ? 1 : 0); AddInt(world.StrengthBuffTicks(fighter.id)); AddInt(world.StrengthLoopTicks(fighter.id));
                bool hasLevel = world.TryGetStrengthLevelProgress(fighter.id, out int level, out int xp, out int required);
                AddInt(hasLevel ? 1 : 0); AddInt(level); AddInt(xp); AddInt(required);
                bool hasPower = world.TryGetStrength(fighter.id, out var power, out int tail);
                AddInt(hasPower ? 1 : 0); AddInt(power.attack); AddInt(power.defense); AddInt(tail);
                foreach (var part in fighter.motion.Segments) { AddInt(part.x); AddInt(part.y); }
            }
            foreach (var fruit in world.Fruit) { AddInt(fruit.position.x); AddInt(fruit.position.y); AddInt(fruit.color); }
            return "s1-" + hash.ToString("x16");
        }
    }

    // Match summaries report only actual body bites, not boost consumption.
    public static class StrengthMatchSummary
    {
        public static string Text(PveFixedWorld world)
        {
            if (world == null || !world.StrengthPreview || !world.Finished) return "";
            return "吃到 " + world.PlayerSegmentsEaten + " 节 · 被吃 " + world.PlayerSegmentsLostToBites + " 节";
        }
    }

    // Explain only the actual minimum-length boost restriction in the
    // strength prototype; touch intent by itself is not proof of movement.
    public static class StrengthBoostNotice
    {
        public static string Text(PveFixedWorld world, ArenaState state, string playerId, bool requested, bool suspended)
        {
            if (world == null || !world.StrengthPreview || state == null || state.phase != "running" ||
                suspended || !requested || string.IsNullOrEmpty(playerId)) return "";
            var player = state.snakes.Find(s => s.id == playerId);
            if (player == null || !player.alive || player.boosting || player.segments.Count != PveFixedMotion.MinimumLength) return "";
            return "身体太短无法加速 · 吃果实恢复";
        }
    }

    // The most recent resolved contact takes precedence; simultaneous attacks
    // and losses are both shown regardless of the order of combat outcomes.
    public static class StrengthContactNotice
    {
        public static string Text(PveFixedWorld world, int tick, out bool warning)
        {
            warning = false;
            if (world == null || !world.StrengthPreview || tick < 0) return "";
            int Recent(int at) => at >= 0 && at <= tick && tick - at < 20 ? at : -1;
            int attack = Recent(world.LastPlayerAttackTick), damage = Recent(world.LastPlayerDamageTick);
            int blocked = Recent(world.LastPlayerBlockedTick), tie = Recent(world.LastPlayerHeadTieTick);
            int latest = Math.Max(Math.Max(attack, damage), Math.Max(blocked, tie));
            if (latest < 0) return "";
            if (damage == latest && attack == latest)
            { warning = true; return "互相吞食 · 吃 " + world.LastPlayerAttackCount + " 节 / 失 " + world.LastPlayerDamageCount + " 节"; }
            if (damage == latest) { warning = true; return "身体被吃掉 " + world.LastPlayerDamageCount + " 节"; }
            if (attack == latest) return "吃掉身体 " + world.LastPlayerAttackCount + " 节！";
            warning = true;
            return blocked == latest ? "强度不足 · 试试更弱的部位" : "势均力敌 · 双方弹开";
        }
    }
}
