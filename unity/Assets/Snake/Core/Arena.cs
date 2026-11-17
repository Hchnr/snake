#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    [Serializable]
    public struct Point
    {
        public float x;
        public float y;

        public Point(float x, float y) { this.x = x; this.y = y; }
        public float Length => (float)Math.Sqrt(x * x + y * y);
        public Point Normalized => Length > 0.00001f ? this / Length : new Point(1, 0);
        public static Point operator +(Point a, Point b) => new Point(a.x + b.x, a.y + b.y);
        public static Point operator -(Point a, Point b) => new Point(a.x - b.x, a.y - b.y);
        public static Point operator *(Point a, float n) => new Point(a.x * n, a.y * n);
        public static Point operator /(Point a, float n) => new Point(a.x / n, a.y / n);
        public static float Distance(Point a, Point b) => (a - b).Length;
    }

    [Serializable]
    public sealed class SnakeState
    {
        public string id;
        public string name;
        public bool bot;
        public bool alive = true;
        public int color;
        public int kills;
        public string eliminatedBy = "";
        public float angle;
        public float shield;
        public bool boosting;
        public int starPoints;
        public float rebound;
        public bool boss, bossRushing;
        public int bossShield;
        public float bossWindup;
        public Point bossAim;
        public List<Point> segments = new List<Point>();
    }

    [Serializable]
    public sealed class FruitState
    {
        public Point position;
        public int color;
    }

    [Serializable]
    public sealed class ArenaState
    {
        public float halfSize;
        public float remaining;
        public string phase = "waiting";
        public string winner = "";
        public int tick;
        public bool starVisible;
        public Point starPosition;
        public string starHolder = "";
        public float starProgress;
        // Presentation fields are projections of the rule simulation. They do
        // not participate in collisions or scoring.
        public string eventKind = "";
        public string eventTarget = "";
        public string eventStatus = "";
        public Point eventPosition;
        public float eventRadius;
        public float eventRemaining;
        public List<SnakeState> snakes = new List<SnakeState>();
        public List<FruitState> fruits = new List<FruitState>();
        // Transient presentation of the latest deterministic capture event.
        public int captureTick = -1, captureCount;
        public string captureOwner = "";
        public List<Point> capturePolygon = new List<Point>();
    }

    [Serializable]
    public sealed class CombatBonuses
    {
        public float turn, boostSaving, pickup;
        public float nearWallTurn, shortBoostSaving, reboundTurn;
        public float boostSavingCap = .07f;
        // Temporary depth-mode effects; never used by the normalized daily rules.
        public float captureShieldSeconds, reboundFreeSeconds;
        public int bonusGrowthPerKill;
    }

    // Shared by the Unity client and the authoritative room server. Coordinates
    // are continuous world units, independent of Unity's rendering and physics.
    public sealed class Arena
    {
        public const float WorldHalfSize = 110f;
        public const float RoundSeconds = 180f;
        public const int StartingLength = 8;
        public const int MinimumLength = 5;
        public const int MaximumLength = 80;
        public const float SegmentSpacing = 0.9f;
        public const float Speed = 6f;
        public const float BoostMultiplier = 1.65f;
        public const float SpawnShieldSeconds = 3f;
        public const float CollisionRadius = 0.72f;
        public const int FruitCount = 200;
        private const float TurnSpeed = 3.8f;

        private sealed class Control
        {
            public Point target = new Point(1, 0);
            public bool boost;
            public float boostDebt;
            public float aiTimer;
            public float wander;
            public float rebound;
            public float reboundCooldown, captureCooldown, freeBoostTime;
            public float bossReady, bossWindup, bossRush, bossHitCooldown;
            public int bossAttack;
            public Point bossDirection;
            public CombatBonuses bonuses = new CombatBonuses();
        }

        public readonly ArenaState State;
        public int FruitRevision { get; private set; }
        private readonly Dictionary<string, Control> controls = new Dictionary<string, Control>();
        private readonly Random random;
        private readonly bool nearbyOpponents, enableCaptures, rotatingEvents;
        private readonly int rotatingEventType;
        private float eventElapsed, eventTimeLeft;
        private int nextColor;
        private int participantsAtStart;
        private float starDelay = 35f;
        private float starHeld;
        private string starSpawnNear = "";

        public Arena(int seed = 1, float halfSize = WorldHalfSize, float roundSeconds = RoundSeconds, int fruitCount = FruitCount, bool nearbyOpponents = false, bool enableCaptures = false, bool rotatingEvents = false)
        {
            random = new Random(seed);
            this.nearbyOpponents = nearbyOpponents;
            this.enableCaptures = enableCaptures;
            this.rotatingEvents = rotatingEvents;
            rotatingEventType = new Random(seed ^ 0x3f71a2).Next(3);
            State = new ArenaState { halfSize = halfSize, remaining = roundSeconds };
            for (int i = 0; i < fruitCount; i++) State.fruits.Add(NewFruit());
        }

        public SnakeState AddSnake(string id, string name, bool bot)
        {
            if (string.IsNullOrWhiteSpace(id) || Find(id) != null) throw new ArgumentException("Snake ID must be unique.");
            Point head = FindSpawn();
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            Point direction = new Point((float)Math.Cos(angle), (float)Math.Sin(angle));
            var snake = new SnakeState
            {
                id = id, name = string.IsNullOrWhiteSpace(name) ? "Snake" : name,
                bot = bot, color = nextColor++ % 8, angle = angle, shield = SpawnShieldSeconds
            };
            for (int i = 0; i < (enableCaptures ? DailyMatch.CaptureStartingLength : StartingLength); i++) snake.segments.Add(head - direction * (i * SegmentSpacing));
            State.snakes.Add(snake);
            controls[id] = new Control { target = direction, wander = angle };
            if (State.phase == "running") participantsAtStart++;
            return snake;
        }

        public bool RemoveSnake(string id)
        {
            var snake = Find(id);
            if (snake == null) return false;
            controls.Remove(id);
            return State.snakes.Remove(snake);
        }

        public SnakeState Find(string id) => State.snakes.Find(s => s.id == id);

        public void Start()
        {
            if (State.phase != "waiting" || State.snakes.Count < 2) return;
            participantsAtStart = State.snakes.Count;
            State.phase = "running";
        }

        public void EndRound() { if (State.phase == "running") Finish(); }

        public void SetInput(string id, Point direction, bool boost)
        {
            Control control;
            if (!controls.TryGetValue(id, out control)) return;
            if (float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsInfinity(direction.x) || float.IsInfinity(direction.y)) return;
            if (direction.Length > 0.05f) control.target = direction.Normalized;
            control.boost = boost;
        }

        public void SetBonuses(string id, CombatBonuses bonuses)
        {
            Control control;
            if (controls.TryGetValue(id, out control)) control.bonuses = bonuses ?? new CombatBonuses();
        }

        public void SetStartingLength(string id, int length)
        {
            var snake = Find(id);
            if (snake == null || State.phase != "waiting") return;
            Grow(snake, Math.Max(0, Math.Min(MaximumLength, length) - snake.segments.Count));
        }

        public void ConfigureStar(float delay, string nearSnakeId)
        {
            if (State.phase != "waiting") return;
            starDelay = Math.Max(0, delay); starSpawnNear = nearSnakeId ?? "";
        }

        public void ConfigureBoss(string id)
        {
            var boss = Find(id);
            if (boss == null || State.phase != "waiting" || !boss.bot) return;
            boss.boss = true; boss.bossShield = 3; controls[id].bossReady = 2.5f;
        }

        public void Tick(float dt)
        {
            if (State.phase != "running" || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            dt = Math.Min(dt, 0.1f);
            State.tick++;
            State.remaining = Math.Max(0, State.remaining - dt);
            if (!rotatingEvents && State.eventStatus == "active") State.eventRemaining = State.remaining;
            var previousHeads = enableCaptures ? new Dictionary<string, Point>() : null;
            foreach (var snake in State.snakes)
            {
                if (!snake.alive) continue;
                var control = controls[snake.id];
                if (snake.bot)
                {
                    if (snake.boss) UpdateBoss(snake, control, dt);
                    else UpdateBot(snake, control, dt);
                }
                snake.shield = Math.Max(0, snake.shield - dt);
                control.rebound = Math.Max(0, control.rebound - dt);
                control.reboundCooldown = Math.Max(0, control.reboundCooldown - dt);
                control.captureCooldown = Math.Max(0, control.captureCooldown - dt);
                control.freeBoostTime = Math.Max(0, control.freeBoostTime - dt);
                control.bossHitCooldown = Math.Max(0, control.bossHitCooldown - dt);
                snake.rebound = control.rebound;
                float targetAngle = (float)Math.Atan2(control.target.y, control.target.x);
                float turnBonus = control.bonuses.turn;
                Point currentHead = snake.segments[0];
                if (enableCaptures) previousHeads[snake.id] = currentHead;
                if (Math.Abs(currentHead.x) > State.halfSize - 10f || Math.Abs(currentHead.y) > State.halfSize - 10f) turnBonus += control.bonuses.nearWallTurn;
                if (control.rebound > 0) turnBonus += control.bonuses.reboundTurn;
                snake.angle += Clamp(AngleDelta(snake.angle, targetAngle), -TurnSpeed * (1 + turnBonus) * dt, TurnSpeed * (1 + turnBonus) * dt);
                var forward = new Point((float)Math.Cos(snake.angle), (float)Math.Sin(snake.angle));
                snake.boosting = control.boost && snake.segments.Count > MinimumLength;
                float speedMultiplier = snake.boosting ? BoostMultiplier : control.rebound > 0 ? 1.15f : 1f;
                if (snake.bossWindup > 0) speedMultiplier *= .45f;
                if (snake.bossRushing) speedMultiplier *= 1.65f;
                Point next = snake.segments[0] + forward * (Speed * speedMultiplier * dt);
                // Reflect the velocity and unused movement at each arena edge. Holding
                // a direction toward a wall can never kill or move the snake outside.
                float edge = Math.Max(1f, State.halfSize - CollisionRadius);
                bool bounced = false;
                if (next.x > edge) { next.x = edge - (next.x - edge); forward.x = -Math.Abs(forward.x); bounced = true; }
                else if (next.x < -edge) { next.x = -edge + (-edge - next.x); forward.x = Math.Abs(forward.x); bounced = true; }
                if (next.y > edge) { next.y = edge - (next.y - edge); forward.y = -Math.Abs(forward.y); bounced = true; }
                else if (next.y < -edge) { next.y = -edge + (-edge - next.y); forward.y = Math.Abs(forward.y); bounced = true; }
                if (bounced)
                {
                    next.x = Clamp(next.x, -edge, edge);
                    next.y = Clamp(next.y, -edge, edge);
                    snake.angle = (float)Math.Atan2(forward.y, forward.x);
                    control.target = forward;
                    if (control.reboundCooldown <= 0)
                    {
                        control.rebound = .8f; control.reboundCooldown = 8f; snake.rebound = control.rebound;
                        control.freeBoostTime = Math.Max(control.freeBoostTime, Math.Max(0, control.bonuses.reboundFreeSeconds));
                    }
                }
                snake.segments[0] = next;
                for (int i = 1; i < snake.segments.Count; i++)
                {
                    Point delta = snake.segments[i - 1] - snake.segments[i];
                    float distance = delta.Length;
                    if (distance > SegmentSpacing) snake.segments[i] = snake.segments[i - 1] - delta * (SegmentSpacing / distance);
                }
                if (snake.boosting && control.freeBoostTime <= 0)
                {
                    float saving = control.bonuses.boostSaving + (snake.segments.Count <= 12 ? control.bonuses.shortBoostSaving : 0);
                    control.boostDebt += dt * 0.4f * (1f - Clamp(saving, 0, Clamp(control.bonuses.boostSavingCap, 0, .20f)));
                    if (control.boostDebt >= 1f && snake.segments.Count > MinimumLength)
                    {
                        snake.segments.RemoveAt(snake.segments.Count - 1);
                        control.boostDebt -= 1f;
                    }
                }
            }
            if (rotatingEvents) BeginRotatingEventIfDue(dt);
            ResolveCollisions();
            if (enableCaptures) ResolveCaptures(previousHeads);
            CollectFruits();
            if (!rotatingEvents || (State.eventStatus == "active" && State.eventKind == "star")) UpdateStar(dt);
            if (rotatingEvents) UpdateRotatingEvent(dt);
            int alive = State.snakes.FindAll(s => s.alive).Count;
            if (State.remaining <= 0 || (participantsAtStart >= 2 && alive <= 1)) Finish();
        }

        private static CapturePoint CapturePosition(Point point) => new CapturePoint((int)Math.Round(point.x * 1000), (int)Math.Round(point.y * 1000));

        private void ResolveCaptures(Dictionary<string, Point> previousHeads)
        {
            var loops = new Dictionary<string, CaptureLoop>();
            foreach (var snake in State.snakes)
            {
                if (!snake.alive || snake.shield > 0 || controls[snake.id].captureCooldown > 0 || !previousHeads.ContainsKey(snake.id)) continue;
                var path = new List<CapturePoint>(snake.segments.Count);
                foreach (var point in snake.segments) path.Add(CapturePosition(point));
                if (!CaptureGeometry.TryLoop(CapturePosition(previousHeads[snake.id]), path, out var loop)) continue;
                loops[snake.id] = loop; controls[snake.id].captureCooldown = .75f;
            }
            var deaths = new Dictionary<string, string>();
            foreach (var victim in State.snakes)
            {
                if (!victim.alive || victim.shield > 0) continue;
                foreach (var entry in loops)
                {
                    if (entry.Key == victim.id || !CaptureGeometry.Contains(entry.Value.polygon, CapturePosition(victim.segments[0]))) continue;
                    if (!deaths.TryGetValue(victim.id, out var previous) || entry.Value.doubleArea < loops[previous].doubleArea ||
                        (entry.Value.doubleArea == loops[previous].doubleArea && string.CompareOrdinal(entry.Key, previous) < 0)) deaths[victim.id] = entry.Key;
                }
            }
            ApplyBossDefense(deaths);
            foreach (var pair in deaths)
            {
                var victim = Find(pair.Key); victim.alive = false; victim.boosting = false; victim.eliminatedBy = pair.Value;
            }
            foreach (var pair in deaths)
            {
                var victim = Find(pair.Key); var killer = Find(pair.Value);
                if (killer != null && killer.alive)
                {
                    var bonus = controls[killer.id].bonuses;
                    AwardEventPoints(killer, victim);
                    killer.kills++; Grow(killer, Math.Max(1, victim.segments.Count / 2) + Math.Max(0, bonus.bonusGrowthPerKill));
                    killer.shield = Math.Max(killer.shield, Math.Max(0, bonus.captureShieldSeconds));
                }
            }
            string shown = loops.ContainsKey("you") ? "you" : null;
            if (shown == null) foreach (var entry in loops) { shown = entry.Key; break; }
            if (shown == null) return;
            State.captureTick = State.tick; State.captureOwner = shown; State.captureCount = 0; State.capturePolygon.Clear();
            foreach (var point in loops[shown].polygon) State.capturePolygon.Add(new Point(point.x / 1000f, point.y / 1000f));
            foreach (var pair in deaths) if (pair.Value == shown) State.captureCount++;
        }

        private void ResolveCollisions()
        {
            var deaths = new Dictionary<string, string>();
            // Compare heads once, before applying any deaths, so equal clashes are fair.
            for (int i = 0; i < State.snakes.Count; i++)
            {
                var a = State.snakes[i];
                if (!a.alive || a.shield > 0) continue;
                for (int j = i + 1; j < State.snakes.Count; j++)
                {
                    var b = State.snakes[j];
                    if (!b.alive || b.shield > 0 || Point.Distance(a.segments[0], b.segments[0]) >= CollisionRadius * 2f) continue;
                    if (a.segments.Count <= b.segments.Count && !deaths.ContainsKey(a.id)) deaths[a.id] = a.segments.Count == b.segments.Count ? "" : b.id;
                    if (b.segments.Count <= a.segments.Count && !deaths.ContainsKey(b.id)) deaths[b.id] = a.segments.Count == b.segments.Count ? "" : a.id;
                }
            }
            foreach (var attacker in State.snakes)
            {
                if (!attacker.alive || attacker.shield > 0 || deaths.ContainsKey(attacker.id)) continue;
                foreach (var defender in State.snakes)
                {
                    if (defender == attacker || !defender.alive || defender.shield > 0) continue;
                    for (int i = 2; i < defender.segments.Count; i++)
                    {
                        if (Point.Distance(attacker.segments[0], defender.segments[i]) >= CollisionRadius * 1.55f) continue;
                        deaths[attacker.id] = defender.id;
                        break;
                    }
                    if (deaths.ContainsKey(attacker.id)) break;
                }
            }
            ApplyBossDefense(deaths);
            foreach (var pair in deaths)
            {
                var victim = Find(pair.Key);
                if (victim != null) { victim.alive = false; victim.boosting = false; victim.eliminatedBy = pair.Value ?? ""; }
            }
            foreach (var pair in deaths)
            {
                var victim = Find(pair.Key);
                var killer = Find(pair.Value);
                if (victim == null || killer == null || !killer.alive) continue;
                AwardEventPoints(killer, victim);
                killer.kills++;
                Grow(killer, Math.Max(1, victim.segments.Count / 2) + Math.Max(0, controls[killer.id].bonuses.bonusGrowthPerKill));
            }
        }

        private void ApplyBossDefense(Dictionary<string, string> deaths)
        {
            var protectedBosses = new List<string>();
            foreach (var pair in deaths)
            {
                var victim = Find(pair.Key);
                if (victim == null || !victim.boss || victim.bossShield <= 0) continue;
                var control = controls[victim.id];
                if (control.bossHitCooldown <= 0) { victim.bossShield--; control.bossHitCooldown = 1f; }
                if (victim.bossShield > 0) protectedBosses.Add(victim.id);
            }
            foreach (string id in protectedBosses) deaths.Remove(id);
        }

        private FruitState NewFruit()
        {
            float edge = Math.Max(1f, State.halfSize - 2f);
            return new FruitState
            {
                position = new Point((float)(random.NextDouble() * 2 - 1) * edge, (float)(random.NextDouble() * 2 - 1) * edge),
                color = random.Next(4)
            };
        }

        private void CollectFruits()
        {
            for (int i = 0; i < State.fruits.Count; i++)
            {
                foreach (var snake in State.snakes)
                {
                    if (!snake.alive || Point.Distance(snake.segments[0], State.fruits[i].position) > 1.25f * (1f + controls[snake.id].bonuses.pickup)) continue;
                    Grow(snake, 1);
                    State.fruits[i] = NewFruit();
                    FruitRevision++;
                    break;
                }
            }
        }

        private void BeginRotatingEventIfDue(float dt)
        {
            if (!string.IsNullOrEmpty(State.eventStatus)) return;
            eventElapsed += dt;
            if (eventElapsed < 15f || State.remaining <= 0) return;
            var player = State.snakes.Find(s => !s.bot && s.alive);
            var candidates = State.snakes.FindAll(s => s.bot && s.alive);
            if (player == null || candidates.Count == 0) return;
            State.eventKind = rotatingEventType == 0 ? "bounty" : rotatingEventType == 1 ? "star" : "fruit";
            State.eventStatus = "active";
            eventTimeLeft = 25f;
            State.eventRemaining = eventTimeLeft;
            if (rotatingEventType == 0)
            {
                var target = candidates[random.Next(candidates.Count)];
                Grow(target, Math.Max(0, 20 - target.segments.Count));
                State.eventTarget = target.id;
            }
            else if (rotatingEventType == 1)
            {
                starDelay = 0; starSpawnNear = player.id;
            }
            else
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                float edge = Math.Max(1f, State.halfSize - 2f);
                Point head = player.segments[0];
                State.eventPosition = new Point(Clamp(head.x + (float)Math.Cos(angle) * 10f, -edge, edge),
                    Clamp(head.y + (float)Math.Sin(angle) * 10f, -edge, edge));
                State.eventRadius = 14f;
                for (int i = 0; i < Math.Min(40, State.fruits.Count); i++)
                {
                    float direction = (float)(random.NextDouble() * Math.PI * 2);
                    float radius = (float)Math.Sqrt(random.NextDouble()) * State.eventRadius;
                    State.fruits[i].position = new Point(Clamp(State.eventPosition.x + (float)Math.Cos(direction) * radius, -edge, edge),
                        Clamp(State.eventPosition.y + (float)Math.Sin(direction) * radius, -edge, edge));
                }
                FruitRevision++;
            }
        }

        private void AwardEventPoints(SnakeState killer, SnakeState victim)
        {
            if (!rotatingEvents || State.eventStatus != "active") return;
            if (State.eventKind == "bounty" && victim.id == State.eventTarget)
                killer.starPoints = Math.Min(2, killer.starPoints + 2);
            else if (State.eventKind == "fruit" && Point.Distance(killer.segments[0], State.eventPosition) <= State.eventRadius)
                killer.starPoints = Math.Min(2, killer.starPoints + 1);
        }

        private void UpdateRotatingEvent(float dt)
        {
            if (State.eventStatus != "active") return;
            eventTimeLeft = Math.Max(0, eventTimeLeft - dt);
            State.eventRemaining = eventTimeLeft;
            var target = State.eventKind == "bounty" ? Find(State.eventTarget) : null;
            if (eventTimeLeft <= 0 || (State.eventKind == "bounty" && (target == null || !target.alive))) EndRotatingEvent();
        }

        private void EndRotatingEvent()
        {
            State.eventStatus = "ended"; State.eventRemaining = 0; State.eventTarget = "";
            State.eventRadius = 0; State.starVisible = false; State.starHolder = ""; State.starProgress = 0;
        }

        private void UpdateStar(float dt)
        {
            if (!State.starVisible)
            {
                starDelay -= dt;
                if (starDelay > 0) return;
                var anchor = Find(starSpawnNear);
                if (anchor != null && anchor.alive)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    float edge = Math.Max(1f, State.halfSize - 2f);
                    State.starPosition = new Point(Clamp(anchor.segments[0].x + (float)Math.Cos(angle) * 9f, -edge, edge),
                        Clamp(anchor.segments[0].y + (float)Math.Sin(angle) * 9f, -edge, edge));
                }
                else State.starPosition = new Point((float)(random.NextDouble() * 60 - 30), (float)(random.NextDouble() * 60 - 30));
                State.starVisible = true; State.starHolder = ""; starHeld = 0;
            }
            if (!string.IsNullOrEmpty(State.starHolder))
            {
                var holder = Find(State.starHolder);
                if (holder == null) { State.starHolder = ""; starHeld = 0; }
                else
                {
                    State.starPosition = holder.segments[0];
                    if (!holder.alive) { State.starHolder = ""; starHeld = 0; }
                    else
                    {
                        starHeld += dt;
                        if (starHeld >= (rotatingEvents ? 8f : 10f))
                        {
                            holder.starPoints = Math.Min(2, holder.starPoints + 1);
                            starHeld = 0;
                            if (rotatingEvents) { if (holder.starPoints >= 2) EndRotatingEvent(); }
                            else { State.starVisible = false; State.starHolder = ""; starDelay = 12f; }
                        }
                    }
                }
            }
            if (State.starVisible && string.IsNullOrEmpty(State.starHolder))
                foreach (var snake in State.snakes)
                    if (snake.alive && Point.Distance(snake.segments[0], State.starPosition) <= 1.5f) { State.starHolder = snake.id; break; }
            State.starProgress = starHeld / (rotatingEvents ? 8f : 10f);
        }

        private static void Grow(SnakeState snake, int amount)
        {
            int target = Math.Min(MaximumLength, snake.segments.Count + amount);
            while (snake.segments.Count < target)
            {
                Point tail = snake.segments[snake.segments.Count - 1];
                Point previous = snake.segments[snake.segments.Count - 2];
                Point away = (tail - previous).Normalized;
                snake.segments.Add(tail + away * SegmentSpacing);
            }
        }

        private void Finish()
        {
            if (rotatingEvents && State.eventStatus == "active") EndRotatingEvent();
            State.phase = "ended";
            SnakeState best = null;
            bool tie = false;
            foreach (var snake in State.snakes)
            {
                if (!snake.alive) continue;
                if (best == null || snake.segments.Count > best.segments.Count ||
                    (snake.segments.Count == best.segments.Count && snake.kills > best.kills)) { best = snake; tie = false; }
                else if (snake.segments.Count == best.segments.Count && snake.kills == best.kills) tie = true;
            }
            State.winner = best != null && !tie ? best.id : "";
        }

        private Point FindSpawn()
        {
            Point candidate = new Point(0, 0);
            float spread = Math.Min(State.halfSize * 0.55f, 36f);
            for (int attempt = 0; attempt < 80; attempt++)
            {
                if (nearbyOpponents && State.snakes.Count > 0 && State.snakes.Count <= 2 && State.halfSize >= 30)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    float distance = enableCaptures ? (State.snakes.Count == 1 ? 9f : 12f) + (float)random.NextDouble() * 2f : 13.5f + (float)random.NextDouble() * 4f;
                    Point player = State.snakes[0].segments[0];
                    candidate = new Point(Clamp(player.x + (float)Math.Cos(angle) * distance, -State.halfSize + 2, State.halfSize - 2),
                        Clamp(player.y + (float)Math.Sin(angle) * distance, -State.halfSize + 2, State.halfSize - 2));
                }
                else candidate = new Point((float)(random.NextDouble() * 2 - 1) * spread, (float)(random.NextDouble() * 2 - 1) * spread);
                bool clear = true;
                foreach (var snake in State.snakes)
                    if (snake.alive && Point.Distance(candidate, snake.segments[0]) < (enableCaptures ? 8f : 12f)) { clear = false; break; }
                if (clear) break;
            }
            return candidate;
        }

        private void UpdateBoss(SnakeState snake, Control control, float dt)
        {
            snake.bossRushing = false;
            if (control.bossRush > 0)
            {
                control.bossRush = Math.Max(0, control.bossRush - dt);
                snake.bossRushing = control.bossRush > 0;
                control.target = control.bossDirection; snake.bossAim = control.bossDirection;
                return;
            }
            if (control.bossWindup > 0)
            {
                control.bossWindup = Math.Max(0, control.bossWindup - dt);
                snake.bossWindup = control.bossWindup; snake.bossAim = control.bossDirection;
                control.target = control.bossDirection;
                if (control.bossWindup <= 0) { control.bossRush = 1.2f; snake.bossRushing = true; }
                return;
            }
            snake.bossWindup = 0;
            control.bossReady -= dt;
            if (control.bossReady > 0) { UpdateBot(snake, control, dt); return; }
            SnakeState target = State.snakes.Find(s => s.alive && !s.bot);
            if (target == null) target = State.snakes.Find(s => s.alive && s != snake);
            if (target == null) return;
            Point aim = (target.segments[0] - snake.segments[0]).Normalized;
            // Alternate a direct charge and a diagonal sweep; both expose the
            // committed direction throughout the windup.
            if ((control.bossAttack++ & 1) != 0)
                aim = new Point(aim.x * .8660254f - aim.y * .5f, aim.x * .5f + aim.y * .8660254f);
            control.bossDirection = aim; control.bossWindup = 1.2f; control.bossReady = 4.5f;
            snake.bossAim = aim; snake.bossWindup = control.bossWindup; control.target = aim;
        }

        private void UpdateBot(SnakeState snake, Control control, float dt)
        {
            control.aiTimer -= dt;
            if (control.aiTimer > 0) return;
            control.aiTimer = 0.3f + (float)random.NextDouble() * 0.35f;
            Point head = snake.segments[0];
            Point desired = new Point(0, 0);
            float margin = State.halfSize - 15f;
            if (Math.Abs(head.x) > margin || Math.Abs(head.y) > margin) desired = (new Point(0, 0) - head).Normalized * 5f;
            SnakeState nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var other in State.snakes)
            {
                if (other == snake || !other.alive) continue;
                float distance = Point.Distance(head, other.segments[0]);
                if (distance < nearestDistance) { nearest = other; nearestDistance = distance; }
                for (int i = 2; i < other.segments.Count; i += 2)
                {
                    Point away = head - other.segments[i];
                    float d = away.Length;
                    if (d > 0.01f && d < 5f) desired += away.Normalized * ((5f - d) * 1.6f);
                }
            }
            if (nearest != null)
            {
                Point toward = (nearest.segments[0] - head).Normalized;
                desired += toward * (snake.segments.Count >= nearest.segments.Count || nearestDistance > 12f ? 1.4f : -1.8f);
            }
            if (State.starVisible && string.IsNullOrEmpty(State.starHolder))
                desired += (State.starPosition - head).Normalized * 0.8f;
            if (rotatingEvents && State.eventStatus == "active" && State.eventKind == "fruit" && Point.Distance(head, State.eventPosition) < 60f)
                desired += (State.eventPosition - head).Normalized * 1.8f;
            // Fruit gives the bots something to seek without replacing opponents
            // as their main objective.
            if (nearestDistance > 10f)
            {
                FruitState closest = null;
                float distance = 18f;
                foreach (var fruit in State.fruits)
                {
                    float candidate = Point.Distance(head, fruit.position);
                    if (candidate < distance) { closest = fruit; distance = candidate; }
                }
                if (closest != null) desired += (closest.position - head).Normalized * 0.7f;
            }
            control.wander += (float)(random.NextDouble() - 0.5) * 0.7f;
            desired += new Point((float)Math.Cos(control.wander), (float)Math.Sin(control.wander)) * 0.35f;
            control.target = desired.Normalized;
            control.boost = nearest != null && nearestDistance < 15f && snake.segments.Count > nearest.segments.Count + 3;
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static float AngleDelta(float from, float to)
        {
            float delta = (to - from) % ((float)Math.PI * 2f);
            if (delta > Math.PI) delta -= (float)Math.PI * 2f;
            if (delta < -Math.PI) delta += (float)Math.PI * 2f;
            return delta;
        }
    }
}
