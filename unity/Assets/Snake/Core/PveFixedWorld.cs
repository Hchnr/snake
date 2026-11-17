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
        public const int MaxTicks = 90 * PveFixedWorld.TickRate;
        public static bool TryReplay(int seed, bool firstRun, PlayerProgress profile, byte[] inputs, out PveFixedWorld result)
        {
            result = null;
            int limit = (firstRun ? 60 : 90) * PveFixedWorld.TickRate;
            if (inputs == null || inputs.Length == 0 || inputs.Length % 2 != 0 || inputs.Length > limit * 2) return false;
            for (int i = 1; i < inputs.Length; i += 2) if (inputs[i] > 1) return false;
            var world = new PveFixedWorld(seed, firstRun, profile);
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
        public PveFixedWorld(int seed, bool firstRun, PlayerProgress profile)
        {
            random = unchecked((uint)seed); if (random == 0) random = 0x6d2b79f5;
            maxTicks = (firstRun ? 60 : 90) * TickRate;
            uint selection = unchecked((uint)seed); eventType = (int)((selection ^ (selection >> 16)) % 3);
            playerBonuses = PveFixedBonuses.From(profile);
            for (int i = 0; i < Arena.FruitCount; i++) fruit.Add(NewFruit());
            for (int i = 0; i < fighters.Length; i++)
            {
                CapturePoint head = Spawn(i);
                int heading = Next(256);
                fighters[i] = new PveFixedFighter(i == 0 ? "you" : "bot-" + i,
                    new PveFixedMotion(head.x, head.y, heading * 256, DailyMatch.CaptureStartingLength)) { shieldTicks = (firstRun && i == 0 ? 4 : 3) * TickRate };
                botDirection[i] = (byte)heading;
            }
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
            if (--botTimer[index] > 0) return botDirection[index];
            botTimer[index] = 6 + Next(8);
            var self = fighters[index]; int nearest = -1; long closest = long.MaxValue;
            for (int i = 0; i < fighters.Length; i++)
            {
                if (i == index || !fighters[i].alive) continue;
                long distance = DistanceSquared(fighters[i].motion.X - self.motion.X, fighters[i].motion.Y - self.motion.Y);
                if (distance < closest) { closest = distance; nearest = i; }
            }
            long vx = 0, vy = 0;
            if (nearest >= 0)
            {
                int sign = self.motion.Length >= fighters[nearest].motion.Length || closest > 12000L * 12000L ? 1 : -1;
                vx += (long)(fighters[nearest].motion.X - self.motion.X) * sign;
                vy += (long)(fighters[nearest].motion.Y - self.motion.Y) * sign;
            }
            AccumulateTrailAvoidance(self, fighters, ref vx, ref vy);
            AccumulateHeadAvoidance(self, fighters, ref vx, ref vy);
            if (nearest < 0 || closest > 10000L * 10000L)
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
            if (index <= 2 && fighters[0].alive && !(eventActive && eventType == 1))
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
            return botDirection[index] = (byte)heading;
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
            projection.eventPosition = ToPoint(eventCenter); projection.eventRadius = eventActive && eventType == 2 ? 14f : 0f;
            projection.eventRemaining = EventTicksRemaining / (float)TickRate;
            projection.starVisible = eventActive && eventType == 1; projection.starPosition = ToPoint(star);
            projection.starHolder = StarHolder; projection.starProgress = starTicks / (8f * TickRate);
            for (int i = 0; i < fighters.Length; i++)
            {
                var source = fighters[i]; var target = projection.snakes[i];
                target.alive = source.alive; target.shield = source.shieldTicks / (float)TickRate;
                target.kills = source.kills; target.starPoints = source.eventPoints;
                target.eliminatedBy = source.eliminatedBy; target.eliminationReason = source.eliminationReason;
                target.angle = source.motion.Angle * (float)(Math.PI * 2 / 65536); target.boosting = source.alive && source.motion.Boosting;
                target.rebound = source.motion.ReboundTicks / (float)TickRate;
                target.segments.Clear();
                for (int j = 0; j < source.motion.Length; j++) target.segments.Add(ToPoint(source.motion.Segments[j]));
            }
            for (int i = 0; i < fruit.Count; i++) { projection.fruits[i].position = ToPoint(fruit[i].position); projection.fruits[i].color = fruit[i].color; }
            projection.captureTick = lastCaptureTick; projection.captureCount = lastCaptureCount; projection.captureOwner = lastCaptureOwner;
            projection.capturePolygon.Clear();
            for (int i = 0; i < lastCapturePolygon.Count; i++) projection.capturePolygon.Add(ToPoint(lastCapturePolygon[i]));
            return projection;
        }
        private static Point ToPoint(CapturePoint point) => new Point(point.x / 1000f, point.y / 1000f);

        public void Step(byte heading, bool boost)
        {
            if (Finished) return;
            Tick++;
            if (Tick == 15 * TickRate && !eventDone) StartEvent();
            for (int i = 0; i < fighters.Length; i++) previouslyAlive[i] = fighters[i].alive;
            for (int i = 0; i < fighters.Length; i++)
            {
                var fighter = fighters[i]; if (!fighter.alive) continue;
                if (fighter.shieldTicks > 0) fighter.shieldTicks--;
                fighter.motion.Step(i == 0 ? heading : BotHeading(i), i == 0 ? boost : botBoost[i],
                    i == 0 ? playerBonuses : new PveFixedBonuses());
            }
            PveFixedCombat.Resolve(fighters, combatWorkspace);
            if (combatWorkspace.captureOwner.Length != 0)
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
                    fighter.motion.Grow(1); fruit[i] = NewFruit(); break;
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
}
