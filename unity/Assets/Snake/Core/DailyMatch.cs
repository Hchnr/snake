#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    // The challenge uses only integer arithmetic and a specified PRNG. The Unity
    // renderer receives a projection of this state; it never drives simulation.
    public sealed class DailyMatch
    {
        public const int TickRate = 20;
        public const int MaxTicks = 60 * TickRate;
        public const int MaxScore = 1540;
        public const string LegacyRulesVersion = "daily-1";
        public const string PreviousRulesVersion = "daily-2";
        public const string RulesVersion = "daily-3";
        public const int CaptureStartingLength = 16;
        public const int CaptureMaxScore = 1460;
        public static bool SupportsRules(string version) => version == LegacyRulesVersion || version == PreviousRulesVersion || version == RulesVersion;
        public static int StartingLengthFor(string version) => version == RulesVersion ? CaptureStartingLength : Arena.StartingLength;
        public static int MaxScoreFor(string version) => version == RulesVersion ? CaptureMaxScore : MaxScore;
        private const int Unit = 1000;
        private const int Edge = 109280;
        private const int Spacing = 900;
        private const int Collision = 720;
        private readonly List<SimSnake> snakes = new List<SimSnake>();
        private readonly List<SimPoint> fruit = new List<SimPoint>();
        private readonly List<int> fruitColor = new List<int>();
        // Eight participants are fixed for each daily rule version. Buffers are
        // private to the match and cleared before reuse on each simulation step.
        private readonly SimPoint[] previousHeadBuffer = new SimPoint[8];
        private readonly int[] deathBuffer = new int[8], captureKillerBuffer = new int[8];
        private readonly CaptureLoop[] captureLoopBuffer = new CaptureLoop[8];
        private readonly List<CapturePoint> capturePathBuffer = new List<CapturePoint>(Arena.MaximumLength);
        private uint random;
        private int starDelay = 20 * TickRate;
        private SimPoint star;
        private bool starActive;
        private int starHolder = -1;
        private int starTicks;
        private int peak;
        private readonly bool legacy, captures;
        private readonly int startingLength, scoreLimit;
        private readonly int eventType;
        private bool eventActive, eventDone;
        private int eventTarget = -1;
        private SimPoint eventCenter;
        private const int EventStart = 15 * TickRate;
        private const int EventEnd = 40 * TickRate;
        private const int EventRadius = 14000;

        private static readonly int[] QuarterSin = {
            0,245,491,736,980,1224,1467,1710,1951,2191,2430,2667,2903,3137,3369,3599,
            3827,4052,4276,4496,4714,4929,5141,5350,5556,5758,5957,6152,6344,6532,6716,6895,
            7071,7242,7410,7572,7730,7883,8032,8176,8315,8449,8577,8701,8819,8932,9040,9142,
            9239,9330,9415,9495,9569,9638,9700,9757,9808,9853,9892,9925,9952,9973,9988,9997,10000
        };

        private struct SimPoint
        {
            public int x, y;
            public SimPoint(int x, int y) { this.x = x; this.y = y; }
        }

        private sealed class SimSnake
        {
            public string id;
            public bool bot;
            public bool alive = true;
            public int color, angle, target, shield = 3 * TickRate, kills, stars;
            public int botTimer, wander, boostDebt, pulse, bounceCooldown, captureCooldown;
            public bool boosting;
            public readonly List<SimPoint> segments = new List<SimPoint>();
        }

        public readonly ArenaState State = new ArenaState { halfSize = Arena.WorldHalfSize, remaining = 60, phase = "running" };
        public bool Finished => State.phase == "ended";
        public int Score => Math.Min(scoreLimit, 10 * (peak - startingLength) + 100 * snakes[0].kills + State.tick / TickRate + 30 * snakes[0].stars);
        public int PeakLength => peak;

        public DailyMatch(int seed, string rulesVersion = RulesVersion)
        {
            if (!SupportsRules(rulesVersion))
                throw new ArgumentException("Unknown daily challenge rules", nameof(rulesVersion));
            legacy = rulesVersion == LegacyRulesVersion;
            captures = rulesVersion == RulesVersion;
            startingLength = StartingLengthFor(rulesVersion); scoreLimit = MaxScoreFor(rulesVersion); peak = startingLength;
            uint selection = unchecked((uint)seed);
            eventType = (int)((selection ^ (selection >> 16)) % 3);
            random = unchecked((uint)seed);
            if (random == 0) random = 0x6d2b79f5;
            for (int i = 0; i < Arena.FruitCount; i++) { fruit.Add(NewFruit()); fruitColor.Add(Next(4)); }
            for (int i = 0; i < 8; i++) AddSnake(i);
            Sync();
        }

        // One encoded direction and boost flag per 50 ms, so the submitted
        // recording can be replayed exactly by the .NET server.
        public void Step(byte direction, bool boost)
        {
            if (Finished) return;
            State.tick++;
            if (!legacy && State.tick == EventStart) StartEvent();
            var previousHeads = captures ? previousHeadBuffer : null;
            for (int i = 0; i < snakes.Count; i++)
            {
                var snake = snakes[i];
                if (!snake.alive) continue;
                if (snake.bot) UpdateBot(i); else snake.target = direction;
                if (snake.shield > 0) snake.shield--;
                if (snake.pulse > 0) snake.pulse--;
                if (snake.bounceCooldown > 0) snake.bounceCooldown--;
                if (snake.captureCooldown > 0) snake.captureCooldown--;
                int delta = ((snake.target - snake.angle + 384) & 255) - 128;
                snake.angle = (snake.angle + Math.Max(-8, Math.Min(8, delta)) + 256) & 255;
                snake.boosting = !snake.bot && boost && snake.segments.Count > Arena.MinimumLength;
                int distance = snake.boosting ? 495 : snake.pulse > 0 ? 345 : 300;
                SimPoint head = snake.segments[0];
                if (captures) previousHeads[i] = head;
                int x = head.x + Cos(snake.angle) * distance / 10000;
                int y = head.y + Sin(snake.angle) * distance / 10000;
                bool bounced = false;
                if (x > Edge) { x = Edge - (x - Edge); snake.angle = (128 - snake.angle + 256) & 255; bounced = true; }
                else if (x < -Edge) { x = -Edge + (-Edge - x); snake.angle = (128 - snake.angle + 256) & 255; bounced = true; }
                if (y > Edge) { y = Edge - (y - Edge); snake.angle = (256 - snake.angle) & 255; bounced = true; }
                else if (y < -Edge) { y = -Edge + (-Edge - y); snake.angle = (256 - snake.angle) & 255; bounced = true; }
                if (bounced)
                {
                    snake.target = snake.angle;
                    if (snake.bounceCooldown == 0) { snake.pulse = 16; snake.bounceCooldown = 160; }
                }
                snake.segments[0] = new SimPoint(x, y);
                for (int part = 1; part < snake.segments.Count; part++)
                {
                    var a = snake.segments[part - 1]; var b = snake.segments[part];
                    int dx = a.x - b.x, dy = a.y - b.y;
                    int len = Distance(dx, dy);
                    if (len > Spacing) snake.segments[part] = new SimPoint(a.x - (int)((long)dx * Spacing / len), a.y - (int)((long)dy * Spacing / len));
                }
                if (snake.boosting && ++snake.boostDebt >= 50)
                {
                    if (snake.segments.Count > Arena.MinimumLength) snake.segments.RemoveAt(snake.segments.Count - 1);
                    snake.boostDebt = 0;
                }
            }
            ResolveCollisions();
            if (captures) ResolveCaptures(previousHeads);
            CollectFruit();
            if (legacy) UpdateStar(); else UpdateEvent();
            peak = Math.Max(peak, snakes[0].segments.Count);
            int alive = 0;
            foreach (var snake in snakes) if (snake.alive) alive++;
            if (!snakes[0].alive || alive <= 1 || State.tick >= MaxTicks)
            {
                State.phase = "ended";
                State.winner = snakes[0].alive && alive == 1 ? snakes[0].id : "";
            }
            Sync();
        }

        private void AddSnake(int index)
        {
            SimPoint head = new SimPoint();
            for (int attempt = 0; attempt < 80; attempt++)
            {
                if (!legacy && index > 0 && index <= 2)
                {
                    int direction = Next(256);
                    int radius = captures ? (index == 1 ? 9000 : 12000) + Next(2001) : (index == 1 ? 13500 : 17500) + Next(4001);
                    var player = snakes[0].segments[0];
                    head = new SimPoint(player.x + Cos(direction) * radius / 10000, player.y + Sin(direction) * radius / 10000);
                }
                else head = new SimPoint(Next(72001) - 36000, Next(72001) - 36000);
                bool clear = true;
                foreach (var other in snakes)
                    if (Distance(head.x - other.segments[0].x, head.y - other.segments[0].y) < (captures ? 8000 : 12000)) { clear = false; break; }
                if (clear) break;
            }
            int angle = Next(256);
            var snake = new SimSnake { id = index == 0 ? "you" : "bot-" + index, bot = index != 0, color = index, angle = angle, target = angle, wander = angle };
            for (int i = 0; i < startingLength; i++)
                snake.segments.Add(new SimPoint(head.x - Cos(angle) * i * Spacing / 10000, head.y - Sin(angle) * i * Spacing / 10000));
            snakes.Add(snake);
        }

        private void UpdateBot(int index)
        {
            var bot = snakes[index];
            if (--bot.botTimer > 0) return;
            bot.botTimer = 6 + Next(8);
            var head = bot.segments[0];
            long vx = 0, vy = 0;
            if (Math.Abs(head.x) > 95000 || Math.Abs(head.y) > 95000) AddVector(ref vx, ref vy, -head.x, -head.y, 5000);
            int nearest = -1, nearestDist = int.MaxValue;
            for (int i = 0; i < snakes.Count; i++)
            {
                var other = snakes[i];
                if (i == index || !other.alive) continue;
                int dist = Distance(head.x - other.segments[0].x, head.y - other.segments[0].y);
                if (dist < nearestDist) { nearest = i; nearestDist = dist; }
                for (int j = 2; j < other.segments.Count; j += 2)
                {
                    int dx = head.x - other.segments[j].x, dy = head.y - other.segments[j].y;
                    int d = Distance(dx, dy);
                    if (d > 0 && d < 5000) AddVector(ref vx, ref vy, dx, dy, (5000 - d) * 8 / 5);
                }
            }
            if (nearest >= 0)
            {
                var target = snakes[nearest];
                int weight = bot.segments.Count >= target.segments.Count || nearestDist > 12000 ? 1400 : -1800;
                AddVector(ref vx, ref vy, target.segments[0].x - head.x, target.segments[0].y - head.y, weight);
            }
            if (starActive && starHolder < 0) AddVector(ref vx, ref vy, star.x - head.x, star.y - head.y, 800);
            if (!legacy && eventActive && eventType == 2 && Dist(head, eventCenter) < 60000)
                AddVector(ref vx, ref vy, eventCenter.x - head.x, eventCenter.y - head.y, 1800);
            if (nearestDist > 10000)
            {
                int closest = -1, distance = 18000;
                for (int i = 0; i < fruit.Count; i++)
                {
                    int d = Distance(fruit[i].x - head.x, fruit[i].y - head.y);
                    if (d < distance) { closest = i; distance = d; }
                }
                if (closest >= 0) AddVector(ref vx, ref vy, fruit[closest].x - head.x, fruit[closest].y - head.y, 700);
            }
            bot.wander = (bot.wander + Next(29) - 14 + 256) & 255;
            vx += Cos(bot.wander) * 350L / 10000; vy += Sin(bot.wander) * 350L / 10000;
            if (vx != 0 || vy != 0) bot.target = ClosestDirection(vx, vy);
        }

        private void ResolveCollisions()
        {
            int[] deaths = deathBuffer;
            for (int i = 0; i < deaths.Length; i++) deaths[i] = -2;
            for (int i = 0; i < snakes.Count; i++) for (int j = i + 1; j < snakes.Count; j++)
            {
                var a = snakes[i]; var b = snakes[j];
                if (!a.alive || !b.alive || a.shield > 0 || b.shield > 0 || Dist(a.segments[0], b.segments[0]) >= Collision * 2) continue;
                if (a.segments.Count <= b.segments.Count && deaths[i] == -2) deaths[i] = a.segments.Count == b.segments.Count ? -1 : j;
                if (b.segments.Count <= a.segments.Count && deaths[j] == -2) deaths[j] = a.segments.Count == b.segments.Count ? -1 : i;
            }
            for (int i = 0; i < snakes.Count; i++)
            {
                var a = snakes[i];
                if (!a.alive || a.shield > 0 || deaths[i] != -2) continue;
                for (int j = 0; j < snakes.Count && deaths[i] == -2; j++)
                {
                    var b = snakes[j];
                    if (i == j || !b.alive || b.shield > 0) continue;
                    for (int k = 2; k < b.segments.Count; k++)
                        if (Dist(a.segments[0], b.segments[k]) < 1116) { deaths[i] = j; break; }
                }
            }
            for (int i = 0; i < snakes.Count; i++) if (deaths[i] != -2) snakes[i].alive = false;
            for (int i = 0; i < snakes.Count; i++)
            {
                int winner = deaths[i];
                if (winner < 0 || !snakes[winner].alive) continue;
                AwardKill(winner, i);
            }
            if (!legacy && eventActive && eventType == 0 && eventTarget >= 0 && !snakes[eventTarget].alive)
                FinishEvent();
        }

        private void AwardKill(int winner, int victim)
        {
            snakes[winner].kills++;
            Grow(snakes[winner], Math.Max(1, snakes[victim].segments.Count / 2));
            if (!legacy && eventActive && winner == 0)
            {
                if (eventType == 0 && victim == eventTarget) snakes[0].stars = 2;
                else if (eventType == 2 && Dist(snakes[0].segments[0], eventCenter) <= EventRadius)
                    snakes[0].stars = Math.Min(2, snakes[0].stars + 1);
            }
        }

        private void ResolveCaptures(SimPoint[] previousHeads)
        {
            var loops = captureLoopBuffer;
            Array.Clear(loops, 0, loops.Length);
            for (int i = 0; i < snakes.Count; i++)
            {
                var owner = snakes[i];
                if (!owner.alive || owner.shield > 0 || owner.captureCooldown > 0) continue;
                capturePathBuffer.Clear();
                foreach (var p in owner.segments) capturePathBuffer.Add(new CapturePoint(p.x, p.y));
                if (!CaptureGeometry.TryLoop(new CapturePoint(previousHeads[i].x, previousHeads[i].y), capturePathBuffer, out loops[i])) continue;
                owner.captureCooldown = 15;
            }

            var killer = captureKillerBuffer;
            for (int victim = 0; victim < snakes.Count; victim++)
            {
                killer[victim] = -1;
                if (!snakes[victim].alive || snakes[victim].shield > 0) continue;
                var head = snakes[victim].segments[0];
                for (int owner = 0; owner < snakes.Count; owner++)
                {
                    if (owner == victim || loops[owner] == null || !CaptureGeometry.Contains(loops[owner].polygon, new CapturePoint(head.x, head.y))) continue;
                    int previous = killer[victim];
                    if (previous < 0 || loops[owner].doubleArea < loops[previous].doubleArea ||
                        (loops[owner].doubleArea == loops[previous].doubleArea && string.CompareOrdinal(snakes[owner].id, snakes[previous].id) < 0)) killer[victim] = owner;
                }
            }
            for (int i = 0; i < snakes.Count; i++) if (killer[i] >= 0) snakes[i].alive = false;
            for (int i = 0; i < snakes.Count; i++) if (killer[i] >= 0 && snakes[killer[i]].alive) AwardKill(killer[i], i);

            int shown = loops[0] != null ? 0 : Array.FindIndex(loops, value => value != null);
            if (shown >= 0)
            {
                State.captureTick = State.tick; State.captureOwner = snakes[shown].id;
                State.captureCount = 0; State.capturePolygon.Clear();
                foreach (var p in loops[shown].polygon) State.capturePolygon.Add(new Point(p.x / (float)Unit, p.y / (float)Unit));
                foreach (int owner in killer) if (owner == shown) State.captureCount++;
            }
            if (eventActive && eventType == 0 && eventTarget >= 0 && !snakes[eventTarget].alive) FinishEvent();
        }

        private void CollectFruit()
        {
            for (int i = 0; i < fruit.Count; i++) foreach (var snake in snakes)
            {
                if (!snake.alive || Dist(snake.segments[0], fruit[i]) > 1250) continue;
                Grow(snake, 1); fruit[i] = NewFruit(); fruitColor[i] = Next(4); break;
            }
        }

        private void UpdateStar()
        {
            if (!starActive)
            {
                if (--starDelay > 0) return;
                star = new SimPoint(Next(60001) - 30000, Next(60001) - 30000);
                starActive = true; starHolder = -1; starTicks = 0;
            }
            if (starHolder >= 0)
            {
                var holder = snakes[starHolder];
                star = holder.segments[0];
                if (!holder.alive) { starHolder = -1; starTicks = 0; }
                else if (++starTicks >= 10 * TickRate)
                {
                    holder.stars = Math.Min(2, holder.stars + 1);
                    starActive = false; starHolder = -1; starTicks = 0; starDelay = 12 * TickRate;
                }
            }
            if (starActive && starHolder < 0)
                for (int i = 0; i < snakes.Count; i++)
                    if (snakes[i].alive && Dist(snakes[i].segments[0], star) <= 1500) { starHolder = i; break; }
        }

        private void StartEvent()
        {
            eventActive = true;
            var player = snakes[0].segments[0];
            if (eventType == 0)
            {
                var candidates = new List<int>();
                for (int i = 1; i < snakes.Count; i++) if (snakes[i].alive) candidates.Add(i);
                if (candidates.Count == 0) { FinishEvent(); return; }
                eventTarget = candidates[Next(candidates.Count)];
                // The bounty is visibly larger, not merely a marker on an
                // ordinary opponent. This is part of the replayed simulation.
                Grow(snakes[eventTarget], Math.Max(0, 20 - snakes[eventTarget].segments.Count));
            }
            else if (eventType == 1)
            {
                int angle = Next(256);
                star = new SimPoint(ClampEdge(player.x + Cos(angle) * 9000 / 10000), ClampEdge(player.y + Sin(angle) * 9000 / 10000));
                starActive = true; starHolder = -1; starTicks = 0;
            }
            else
            {
                int angle = snakes[0].angle;
                eventCenter = new SimPoint(ClampEdge(player.x + Cos(angle) * 10000 / 10000), ClampEdge(player.y + Sin(angle) * 10000 / 10000));
                // Relocate existing fruit rather than increasing the entity budget.
                for (int i = 0; i < Math.Min(12, fruit.Count); i++)
                {
                    int direction = Next(256), radius = 2000 + Next(10001);
                    fruit[i] = new SimPoint(ClampEdge(eventCenter.x + Cos(direction) * radius / 10000), ClampEdge(eventCenter.y + Sin(direction) * radius / 10000));
                    fruitColor[i] = Next(4);
                }
            }
        }

        private void UpdateEvent()
        {
            if (!eventActive) return;
            if (State.tick >= EventEnd) { FinishEvent(); return; }
            if (eventType != 1 || !starActive) return;
            if (starHolder >= 0)
            {
                var holder = snakes[starHolder];
                star = holder.segments[0];
                if (!holder.alive) { starHolder = -1; starTicks = 0; }
                else if (++starTicks >= 8 * TickRate)
                {
                    holder.stars = Math.Min(2, holder.stars + 1);
                    starTicks = 0;
                    if (holder.stars >= 2) FinishEvent();
                }
            }
            if (starActive && starHolder < 0)
                for (int i = 0; i < snakes.Count; i++)
                    if (snakes[i].alive && Dist(snakes[i].segments[0], star) <= 1500) { starHolder = i; break; }
        }

        private void FinishEvent()
        {
            eventActive = false; eventDone = true;
            starActive = false; starHolder = -1; starTicks = 0;
        }

        private static int ClampEdge(int value) => Math.Max(-Edge + 2000, Math.Min(Edge - 2000, value));

        private void Grow(SimSnake snake, int count)
        {
            int target = Math.Min(Arena.MaximumLength, snake.segments.Count + count);
            while (snake.segments.Count < target)
            {
                var a = snake.segments[snake.segments.Count - 1]; var b = snake.segments[snake.segments.Count - 2];
                int dx = a.x - b.x, dy = a.y - b.y, length = Distance(dx, dy);
                if (length == 0) { dx = -Cos(snake.angle); dy = -Sin(snake.angle); length = 10000; }
                snake.segments.Add(new SimPoint(a.x + (int)((long)dx * Spacing / length), a.y + (int)((long)dy * Spacing / length)));
            }
        }

        private void Sync()
        {
            State.remaining = Math.Max(0, (MaxTicks - State.tick) / (float)TickRate);
            while (State.snakes.Count < snakes.Count) State.snakes.Add(new SnakeState());
            for (int i = 0; i < snakes.Count; i++)
            {
                var source = snakes[i]; var target = State.snakes[i];
                target.id = source.id; target.name = source.bot ? "AI " + i : "玩家"; target.bot = source.bot;
                target.alive = source.alive; target.color = source.color; target.kills = source.kills;
                target.angle = source.angle * (float)(Math.PI * 2 / 256); target.shield = source.shield / (float)TickRate;
                target.boosting = source.boosting; target.starPoints = source.stars; target.rebound = source.pulse / (float)TickRate;
                target.segments.Clear();
                foreach (var p in source.segments) target.segments.Add(ToPoint(p));
            }
            // State is a mutable projection, just like snake segments. Reusing
            // fruit objects avoids 200 allocations every 50 ms on the client.
            while (State.fruits.Count < fruit.Count) State.fruits.Add(new FruitState());
            if (State.fruits.Count > fruit.Count) State.fruits.RemoveRange(fruit.Count, State.fruits.Count - fruit.Count);
            for (int i = 0; i < fruit.Count; i++)
            {
                State.fruits[i].position = ToPoint(fruit[i]);
                State.fruits[i].color = fruitColor[i];
            }
            State.starVisible = starActive; State.starPosition = ToPoint(star);
            State.starHolder = starHolder < 0 ? "" : snakes[starHolder].id;
            State.starProgress = starTicks / (float)((legacy ? 10 : 8) * TickRate);
            if (!legacy)
            {
                State.eventKind = eventType == 0 ? "bounty" : eventType == 1 ? "star" : "fruit";
                State.eventStatus = eventActive ? "active" : eventDone ? "ended" : "waiting";
                State.eventTarget = eventActive && eventTarget >= 0 ? snakes[eventTarget].id : "";
                State.eventPosition = ToPoint(eventCenter);
                State.eventRadius = eventActive && eventType == 2 ? EventRadius / (float)Unit : 0;
                State.eventRemaining = eventActive ? Math.Max(0, (EventEnd - State.tick) / (float)TickRate) : 0;
            }
        }

        private SimPoint NewFruit() => new SimPoint(Next(216001) - 108000, Next(216001) - 108000);
        private int Next(int max) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (int)(random % (uint)max); }
        private static Point ToPoint(SimPoint p) => new Point(p.x / (float)Unit, p.y / (float)Unit);
        private static int Sin(int angle)
        {
            angle &= 255;
            if (angle <= 64) return QuarterSin[angle];
            if (angle <= 128) return QuarterSin[128 - angle];
            if (angle <= 192) return -QuarterSin[angle - 128];
            return -QuarterSin[256 - angle];
        }
        private static int Cos(int angle) => Sin(angle + 64);
        private static int Dist(SimPoint a, SimPoint b) => Distance(a.x - b.x, a.y - b.y);
        private static int Distance(int x, int y)
        {
            ulong n = (ulong)((long)x * x + (long)y * y), result = 0, bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0) { if (n >= result + bit) { n -= result + bit; result = (result >> 1) + bit; } else result >>= 1; bit >>= 2; }
            return (int)result;
        }
        private static void AddVector(ref long x, ref long y, int dx, int dy, int weight)
        {
            int distance = Distance(dx, dy);
            if (distance == 0) return;
            x += (long)dx * weight / distance; y += (long)dy * weight / distance;
        }
        private static int ClosestDirection(long x, long y)
        {
            int best = 0; long bestDot = long.MinValue;
            for (int i = 0; i < 256; i++) { long dot = x * Cos(i) + y * Sin(i); if (dot > bestDot) { bestDot = dot; best = i; } }
            return best;
        }
    }
}
