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
        public string eliminatedBy = "", eliminationReason = "";
        public float angle;
        public float shield;
        public bool boosting;
        public int starPoints;
        public float rebound;
        // Local depth-only presentation; not sent with online room snapshots.
        internal bool reboundBoostReady, reboundBoostWaitRelease;
        internal float reboundBoostTime;
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
        // captureCount includes only eliminations credited to a surviving owner.
        public int captureTick = -1, captureCount;
        public string captureOwner = "";
        public List<Point> capturePolygon = new List<Point>();
    }

    // Describes only outcomes established by the simulation. Unknown or
    // older states deliberately receive a generic message.
    public static class MatchOutcome
    {
        public static bool IsVictory(ArenaState state, string playerId)
        {
            if (state == null || state.phase != "ended" || string.IsNullOrEmpty(playerId) || state.winner != playerId) return false;
            var player = state.snakes.Find(s => s.id == playerId);
            return player != null && player.alive;
        }

        public static string Explain(ArenaState state, string playerId)
        {
            if (state == null || state.phase != "ended" || string.IsNullOrEmpty(playerId)) return "";
            var player = state.snakes.Find(s => s.id == playerId);
            if (player == null) return "";
            if (player.alive)
            {
                if (state.remaining <= 0) return "时间到，你成功存活到最后";
                return state.winner == playerId ? "你赢得了本局" : "本局已结束";
            }
            switch (player.eliminationReason)
            {
                case "trail": return "撞上对手轨迹，被淘汰";
                case "head": return "正面相撞时长度较短，被淘汰";
                case "head_tie": return "正面相撞且长度相同，双方出局";
                case "capture": return "被对手收圈捕获";
                default: return "本局已出局";
            }
        }
    }

    // Experimental local PvE recording. It is deliberately not a reward
    // verifier until rules and bonus snapshots are versioned and determinism
    // is verified across the actual Unity and server runtimes.
    public static class PveRecording
    {
        public const int MaxTicks = 90 * 20;
        public static Point Direction(byte heading)
        {
            double angle = heading * (Math.PI * 2 / 256);
            return new Point((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
        public static bool TryReplay(int seed, bool firstRun, CombatBonuses bonuses, byte[] input, out Arena arena)
        {
            arena = null;
            if (input == null || input.Length == 0 || input.Length % 2 != 0 || input.Length > MaxTicks * 2) return false;
            for (int i = 1; i < input.Length; i += 2) if (input[i] > 1) return false;
            var replay = Arena.CreateStandardPve(seed, firstRun, "玩家");
            replay.SetBonuses("you", bonuses);
            for (int i = 0; i < input.Length; i += 2)
            {
                if (replay.State.phase != "running") return false;
                replay.SetInput("you", Direction(input[i]), input[i + 1] == 1);
                replay.Tick(.05f);
            }
            arena = replay;
            return true;
        }
    }

    // Presentation time freezes independently of simulation and Unity's
    // global clock, including when a platform suspends the game.
    public sealed class ArenaVisualClock
    {
        public float Time { get; private set; }
        public void Advance(float delta, bool paused)
        {
            if (!paused && delta > 0 && !float.IsNaN(delta) && !float.IsInfinity(delta)) Time += delta;
        }
    }

    public static class RoundTimerPresentation
    {
        public static bool IsUrgent(ArenaState state) => state != null && state.phase == "running" &&
            state.remaining > 0 && state.remaining <= 10;
    }

    // Short first-run prompts follow observed actions rather than completing
    // automatically as the timer advances. They do not affect input or rules.
    public static class FirstRunHints
    {
        // Presentation only: show a hint when the head is approaching a
        // non-adjacent part of its actual body. This does not predict a valid
        // capture, whose area and crossing are checked by CaptureGeometry.
        public static bool ApproachingOwnTrail(SnakeState snake)
        {
            if (snake == null || !snake.alive || snake.shield > 0 || snake.segments == null ||
                snake.segments.Count <= CaptureGeometry.FirstBodySegment + 1) return false;
            Point head = snake.segments[0];
            Point forward = new Point((float)Math.Cos(snake.angle), (float)Math.Sin(snake.angle));
            for (int i = CaptureGeometry.FirstBodySegment; i + 1 < snake.segments.Count; i++)
            {
                Point a = snake.segments[i], b = snake.segments[i + 1], ab = b - a;
                float lengthSquared = ab.x * ab.x + ab.y * ab.y;
                if (lengthSquared < .000001f) continue;
                Point fromA = head - a;
                float t = Math.Max(0, Math.Min(1, (fromA.x * ab.x + fromA.y * ab.y) / lengthSquared));
                Point delta = a + ab * t - head;
                float distanceSquared = delta.x * delta.x + delta.y * delta.y;
                if (distanceSquared > .01f && distanceSquared <= 4f && delta.x * forward.x + delta.y * forward.y > 0) return true;
            }
            return false;
        }

        public static string Select(int tick, bool steered, bool closedLoop, bool rebounded, int kills, bool nearTrail = false, bool boosted = false)
        {
            int seconds = Math.Max(0, tick) / 20;
            if (seconds < 2 || seconds >= 30) return "";
            if (!steered) return "在屏幕任意位置拖动，控制方向";
            if (!closedLoop && nearTrail) return "绕回自己的轨迹，尝试闭合";
            if (seconds >= 5 && seconds < 8 && !boosted) return "第二根手指按住加速，会消耗长度";
            if (seconds >= 8 && seconds < 16 && !closedLoop) return "绕住对手，再回到自己的轨迹";
            if (seconds >= 16 && seconds < 24 && !rebounded) return "撞墙不会失败，还会短暂加速";
            if (seconds >= 24 && kills <= 0) return "让对手撞上你的轨迹，淘汰后立即成长";
            return "";
        }
    }

    // Records only a witnessed active-to-ended event. This is presentation
    // state, separate from scoring and from the deterministic replay.
    public sealed class EventOutcomeNotice
    {
        private bool active;
        private string kind = "", notice = "";
        private int untilTick = -1;
        public void Reset() { active = false; kind = ""; notice = ""; untilTick = -1; }
        public void Observe(ArenaState world, string playerId)
        {
            if (world == null) return;
            if (world.eventStatus == "active")
            {
                if (world.eventKind == "bounty" || world.eventKind == "star" || world.eventKind == "fruit")
                { active = true; kind = world.eventKind; }
                return;
            }
            if (!active || world.eventStatus != "ended") return;
            active = false;
            var player = world.snakes.Find(s => s.id == playerId);
            if (player == null) return;
            int points = Math.Max(0, player.starPoints);
            string name = kind == "bounty" ? "巨蛇悬赏" : kind == "star" ? "星核争夺" : "果潮混战";
            notice = name + (points > 0 ? "结束 · 获得 " + points + " 事件点" : "结束 · 本次未得事件点");
            untilTick = world.tick + 60;
        }
        public string Current(ArenaState world)
            => world != null && world.phase == "running" && world.tick < untilTick ? notice : "";
    }

    public readonly struct ResultPose
    {
        public readonly float Lift, Tilt;
        public ResultPose(float lift, float tilt) { Lift = lift; Tilt = tilt; }
    }

    public static class ResultMotion
    {
        // A brief, bounded celebration applies only to an actual winner.
        public static ResultPose Sample(bool won, bool reduced, float elapsed)
        {
            if (!won || reduced || float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0 || elapsed >= 1f)
                return new ResultPose(0, 0);
            double envelope = Math.Sin(Math.PI * elapsed);
            return new ResultPose((float)(.16 * envelope), (float)(4 * Math.Sin(2 * Math.PI * elapsed) * envelope));
        }
    }

    // Visual state only: the simulation remains the source of angle and boost.
    // Uniform scaling preserves the source character's proportions.
    public sealed class CharacterMotion
    {
        private bool initialized;
        private int lastTick;
        private float lastAngle, targetLean, lean, boost;
        public float LeanDegrees => lean;
        public float Scale => 1f + .08f * boost;

        public void Update(int tick, float angle, bool boosting, float deltaTime, bool reduced, bool active)
        {
            if (float.IsNaN(angle) || float.IsInfinity(angle)) return;
            if (!initialized || tick < lastTick)
            {
                initialized = true; lastTick = tick; lastAngle = angle;
                targetLean = lean = boost = 0;
            }
            else if (tick > lastTick)
            {
                float turn = (float)Math.Atan2(Math.Sin(angle - lastAngle), Math.Cos(angle - lastAngle));
                float elapsed = Math.Max(.05f, (tick - lastTick) * .05f);
                targetLean = Math.Max(-8f, Math.Min(8f, -turn / elapsed * 2f));
                lastTick = tick; lastAngle = angle;
            }
            if (reduced) { targetLean = lean = boost = 0; return; }
            if (!active) targetLean = 0;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) deltaTime = 0;
            float dt = Math.Max(0, Math.Min(.1f, deltaTime));
            float blend = (float)(1 - Math.Exp(-10 * dt));
            lean += ((active ? targetLean : 0) - lean) * blend;
            boost += ((active && boosting ? 1 : 0) - boost) * blend;
        }
    }

    // Presentation-only transition: only a previously displayed living
    // character can produce an exit; resets prevent events leaking between matches.
    public sealed class EliminationTransition
    {
        public const float Duration = .35f;
        private readonly HashSet<string> living = new HashSet<string>();
        private readonly Dictionary<string, float> exits = new Dictionary<string, float>();
        public void Reset() { living.Clear(); exits.Clear(); }
        public void ObserveAlive(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            living.Add(id); exits.Remove(id);
        }
        public bool ObserveDead(string id, float now, out float progress, out bool started)
        {
            progress = 0; started = false;
            if (string.IsNullOrEmpty(id)) return false;
            if (living.Remove(id)) { exits[id] = now; started = true; }
            if (!exits.TryGetValue(id, out float start)) return false;
            progress = Math.Max(0, (now - start) / Duration);
            if (progress >= 1) { exits.Remove(id); return false; }
            return true;
        }
    }

    // Shows only an observed transition caused by the local player. A dead
    // owner cannot receive a kill in the simulation, so suppress that case.
    public sealed class EliminationFeedback
    {
        private readonly HashSet<string> living = new HashSet<string>();
        private int lastTick = -1, untilTick = -1;
        private string text = "";
        public void Reset() { living.Clear(); lastTick = -1; untilTick = -1; text = ""; }
        public void Observe(ArenaState world, string playerId)
        {
            if (world == null || string.IsNullOrEmpty(playerId)) return;
            if (world.tick < lastTick) Reset();
            lastTick = world.tick;
            var player = world.snakes.Find(s => s.id == playerId);
            int count = 0; string firstName = "", commonAction = null;
            foreach (var snake in world.snakes)
            {
                if (string.IsNullOrEmpty(snake.id)) continue;
                if (snake.alive) { living.Add(snake.id); continue; }
                if (!living.Remove(snake.id) || snake.id == playerId || snake.eliminatedBy != playerId || player == null || !player.alive) continue;
                string action = snake.eliminationReason == "capture" ? "收圈捕获" : snake.eliminationReason == "trail" ? "轨迹拦截" : snake.eliminationReason == "head" ? "正面胜出" : "";
                if (action.Length == 0) continue;
                string name = string.IsNullOrEmpty(snake.name) ? "对手" : snake.name;
                if (name.Length > 12) name = name.Substring(0, 12) + "…";
                if (count == 0) { firstName = name; commonAction = action; }
                else if (commonAction != action) commonAction = "";
                count++;
            }
            if (count == 1) { text = commonAction + " · " + firstName; untilTick = world.tick + 40; }
            else if (count > 1) { text = (string.IsNullOrEmpty(commonAction) ? "淘汰" : commonAction) + " · " + count + " 个对手"; untilTick = world.tick + 40; }
        }
        public string Current(ArenaState world) => world != null && world.phase == "running" && world.tick <= untilTick ? text : "";
    }

    // Captures a real change in the player's kill count and observed length.
    // Tick-based expiry freezes when the simulation is paused.
    public sealed class KillFeedback
    {
        private string player = "", message = "";
        private int tick = -1, kills, length, untilTick = -1;
        private bool baseline = true;
        public void Reset() { player = ""; message = ""; tick = -1; kills = length = 0; untilTick = -1; baseline = true; }
        public void Suspend() { baseline = true; }
        public void Observe(ArenaState world, string playerId)
        {
            if (world == null || string.IsNullOrEmpty(playerId)) return;
            var snake = world.snakes.Find(s => s.id == playerId);
            if (snake == null) return;
            if (world.tick < tick || player != playerId) Reset();
            if (!baseline && snake.kills > kills)
            {
                message = snake.segments.Count > length ? "围猎成功  ·  变长！" : "围猎成功！";
                untilTick = world.tick + 22;
            }
            player = playerId; tick = world.tick; kills = snake.kills; length = snake.segments.Count; baseline = false;
        }
        public string Current(ArenaState world) => world != null && world.phase == "running" && world.tick < untilTick ? message : "";
    }

    // The UI may indicate an off-screen objective, but never invents one.
    public static class EventTargetIndicator
    {
        public static bool TryTarget(ArenaState world, string playerId, out Point target, out string label)
        {
            target = default; label = "";
            if (world == null || world.phase != "running") return false;
            if (world.eventStatus == "active" && world.eventKind == "bounty" && !string.IsNullOrEmpty(world.eventTarget))
            {
                var snake = world.snakes.Find(s => s.id == world.eventTarget && s.alive && s.segments.Count > 0);
                if (snake == null) return false;
                target = snake.segments[0]; label = snake.boss ? "首领" : "悬赏"; return true;
            }
            if (world.starVisible && world.starHolder != playerId)
            { target = world.starPosition; label = "星核"; return true; }
            if (world.eventStatus == "active" && world.eventKind == "fruit" && world.eventRadius > 0)
            { target = world.eventPosition; label = "果潮"; return true; }
            return false;
        }

        // Intersect the center-to-target ray with the usable screen rectangle.
        public static bool TryEdge(Point target, float left, float top, float right, float bottom, out Point edge)
        {
            edge = default;
            if (float.IsNaN(target.x) || float.IsNaN(target.y) || float.IsInfinity(target.x) || float.IsInfinity(target.y) || right <= left || bottom <= top) return false;
            if (target.x >= left && target.x <= right && target.y >= top && target.y <= bottom) return false;
            float cx = (left + right) * .5f, cy = (top + bottom) * .5f;
            float dx = target.x - cx, dy = target.y - cy;
            float tx = dx == 0 ? float.PositiveInfinity : (right - left) * .5f / Math.Abs(dx);
            float ty = dy == 0 ? float.PositiveInfinity : (bottom - top) * .5f / Math.Abs(dy);
            float scale = Math.Min(tx, ty);
            edge = new Point(cx + dx * scale, cy + dy * scale);
            return true;
        }
    }

    // A single nearby off-screen opponent marker, based only on real head positions.
    public static class OpponentEdgeIndicator
    {
        public const float MaxDistance = 30f;
        // Avoid rapid switching between similarly distant off-screen actors.
        public static bool KeepPrevious(float previousDistanceSquared, float closestDistanceSquared)
        {
            if (float.IsNaN(previousDistanceSquared) || float.IsNaN(closestDistanceSquared) ||
                float.IsInfinity(previousDistanceSquared) || float.IsInfinity(closestDistanceSquared) ||
                previousDistanceSquared < 0 || closestDistanceSquared < 0) return false;
            double limit = Math.Sqrt(closestDistanceSquared) + 3;
            return previousDistanceSquared <= limit * limit;
        }
        public static bool TryCandidate(ArenaState world, string playerId, SnakeState other, Point screen,
            out float distanceSquared, out Point edge)
        {
            distanceSquared = 0; edge = default;
            if (world == null || world.phase != "running" || string.IsNullOrEmpty(playerId) || other == null ||
                !other.alive || string.IsNullOrEmpty(other.id) || other.id == playerId || other.segments == null || other.segments.Count == 0) return false;
            var player = world.snakes.Find(s => s.id == playerId);
            if (player == null || !player.alive || player.segments == null || player.segments.Count == 0) return false;
            if (world.eventStatus == "active" && world.eventKind == "bounty" && world.eventTarget == other.id) return false;
            if (world.starVisible && !string.IsNullOrEmpty(world.starHolder) && world.starHolder == other.id) return false;
            float dx = other.segments[0].x - player.segments[0].x, dy = other.segments[0].y - player.segments[0].y;
            distanceSquared = dx * dx + dy * dy;
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared > MaxDistance * MaxDistance) return false;
            if (float.IsNaN(screen.x) || float.IsNaN(screen.y) || float.IsInfinity(screen.x) || float.IsInfinity(screen.y)) return false;
            // Do not call a visible character off-screen merely because it is near the HUD.
            if (screen.x >= 0 && screen.x <= 1080 && screen.y >= 0 && screen.y <= 1920) return false;
            return EventTargetIndicator.TryEdge(screen, 110, 410, 970, 1450, out edge);
        }

        public static Point BoxOrigin(Point edge)
        {
            float x = Math.Max(30, Math.Min(910, edge.x - 70));
            float y = Math.Max(250, Math.Min(1450, edge.y - 27));
            // Avoid both the minimap and the first-run tutorial panel.
            if (x + 140 > 760 && y < 545) y = 660;
            if (y < 656 && y + 54 > 544 && x + 140 > 94 && x < 986) y = 660;
            return new Point(x, y);
        }
    }

    // Places short nameplates without overlapping one another or the minimap.
    // It is presentation only; anchors are projected above actual heads.
    public sealed class NameplateLayout
    {
        public const float Width = 210f, Height = 48f;
        private const int HoldTicks = 12, ForgetTicks = 40;
        private const float ReturnClearance = 12f;
        private static readonly float[] VerticalOffsets = { 0, -56, -112, -168, -224, -280, -336, -392 };
        private struct ReservedArea { public float x, y, width, height; }
        private struct PreviousSlot { public int index, changedAt, seenAt; }
        private readonly List<Point> placed = new List<Point>(8);
        private readonly List<Point> anchors = new List<Point>(8);
        private readonly List<ReservedArea> reserved = new List<ReservedArea>(2);
        private readonly Dictionary<string, PreviousSlot> history = new Dictionary<string, PreviousSlot>();
        private readonly List<string> expired = new List<string>(8);
        private int tick = -1;

        // Reset clears only this draw's geometry; ClearHistory is used on a new match.
        public void Reset() { placed.Clear(); anchors.Clear(); reserved.Clear(); }
        public void ClearHistory() { Reset(); history.Clear(); tick = -1; }
        public void BeginFrame(int currentTick)
        {
            Reset();
            if (currentTick < tick) history.Clear();
            tick = currentTick;
            expired.Clear();
            foreach (var entry in history)
                if ((long)tick - entry.Value.seenAt > ForgetTicks) expired.Add(entry.Key);
            foreach (var id in expired) history.Remove(id);
        }
        public void Reserve(float x, float y, float width, float height)
        {
            if (width > 0 && height > 0) reserved.Add(new ReservedArea { x = x, y = y, width = width, height = height });
        }
        public void AddAnchor(Point anchor)
        {
            if (!float.IsNaN(anchor.x) && !float.IsNaN(anchor.y) && !float.IsInfinity(anchor.x) && !float.IsInfinity(anchor.y)) anchors.Add(anchor);
        }

        public bool TryPlace(Point anchor, out Point topLeft) => TryPlace(null, anchor, out topLeft);
        public bool TryPlace(string id, Point anchor, out Point topLeft)
        {
            topLeft = default;
            if (float.IsNaN(anchor.x) || float.IsNaN(anchor.y) || float.IsInfinity(anchor.x) || float.IsInfinity(anchor.y) ||
                anchor.x < 90 || anchor.x > 990 || anchor.y < 260 || anchor.y > 1430) return false;
            PreviousSlot old = default;
            bool remembered = !string.IsNullOrEmpty(id) && history.TryGetValue(id, out old);
            int chosen = -1;
            if (remembered && Candidate(anchor, old.index, 0, out topLeft))
            {
                chosen = old.index;
                // Return to a better slot only after the hold and with extra clearance.
                if ((long)tick - old.changedAt >= HoldTicks)
                    for (int i = 0; i < old.index; i++)
                        if (Candidate(anchor, i, ReturnClearance, out var preferred))
                        { chosen = i; topLeft = preferred; break; }
            }
            if (chosen < 0)
                for (int i = 0; i < VerticalOffsets.Length; i++)
                    if (Candidate(anchor, i, 0, out topLeft)) { chosen = i; break; }
            if (chosen < 0)
            {
                if (remembered) { old.seenAt = tick; history[id] = old; }
                topLeft = default; return false;
            }
            placed.Add(topLeft);
            if (!string.IsNullOrEmpty(id))
                history[id] = new PreviousSlot { index = chosen, changedAt = !remembered || chosen != old.index ? tick : old.changedAt, seenAt = tick };
            return true;
        }

        private bool Candidate(Point anchor, int index, float extra, out Point topLeft)
        {
            topLeft = default;
            if (index < 0 || index >= VerticalOffsets.Length) return false;
            float x = Math.Max(30, Math.Min(840, anchor.x - Width / 2));
            float y = Math.Max(400, Math.Min(1450 - Height, anchor.y - 68 + VerticalOffsets[index]));
            // The anchor sits above the avatar; never move a plate down onto its face.
            if (y + Height > anchor.y - 12) return false;
            if (x + Width + extra > 760 && y - extra < 545) return false;
            foreach (var area in reserved)
                if (x < area.x + area.width + 6 + extra && x + Width + 6 + extra > area.x &&
                    y < area.y + area.height + 6 + extra && y + Height + 6 + extra > area.y) return false;
            foreach (var head in anchors)
            {
                // A label's own anchor naturally sits only 20 units below it.
                float margin = head.x == anchor.x && head.y == anchor.y ? 8 : 8 + extra;
                if (head.x >= x - margin && head.x <= x + Width + margin &&
                    head.y >= y - margin && head.y <= y + Height + margin) return false;
            }
            foreach (var other in placed)
                if (x < other.x + Width + 6 + extra && x + Width + 6 + extra > other.x &&
                    y < other.y + Height + 6 + extra && y + Height + 6 + extra > other.y) return false;
            topLeft = new Point(x, y); return true;
        }
    }

    // Presentation summary sampled from resolved simulation ticks. It never
    // affects scores, rewards, collisions or replay verification.
    public sealed class MatchHighlights
    {
        public int PeakLength { get; private set; }
        public int BestCapture { get; private set; }
        private int lastCaptureTick = -1;

        public void Reset() { PeakLength = 0; BestCapture = 0; lastCaptureTick = -1; }

        public void Observe(ArenaState state, string playerId)
        {
            if (state == null || string.IsNullOrEmpty(playerId)) return;
            var player = state.snakes.Find(s => s.id == playerId);
            if (player == null) return;
            PeakLength = Math.Max(PeakLength, player.segments.Count);
            if (state.captureTick < 0 || state.captureTick == lastCaptureTick) return;
            lastCaptureTick = state.captureTick;
            // Captors eliminated simultaneously receive no kill or growth.
            if (player.alive && state.captureOwner == playerId && state.captureCount > 0)
                BestCapture = Math.Max(BestCapture, state.captureCount);
        }
    }

    // Presentation-only proximity query: use the same head/body radii and
    // shield rules as the simulation, without affecting movement or outcomes.
    public static class DangerProbe
    {
        public const float WarningClearance = 2.5f;

        public static bool Find(ArenaState state, string playerId, out Point position)
            => FindInternal(state, playerId, false, out position);

        // The warning reflects only current geometry; it does not predict movement.
        public static bool FindBeforeProtectionEnds(ArenaState state, string playerId, out Point position)
            => FindInternal(state, playerId, true, out position);

        private static bool FindInternal(ArenaState state, string playerId, bool endingProtection, out Point position)
        {
            position = default;
            if (state == null || state.phase != "running" || string.IsNullOrEmpty(playerId)) return false;
            var player = state.snakes.Find(s => s.id == playerId);
            if (player == null || !player.alive || player.segments.Count == 0) return false;
            if (endingProtection ? player.shield <= 0 || player.shield > 1f : player.shield > 0) return false;
            float closest = WarningClearance;
            bool found = false;
            foreach (var opponent in state.snakes)
            {
                if (opponent == player || !opponent.alive || opponent.shield > (endingProtection ? 1f : 0f) || opponent.segments.Count == 0) continue;
                for (int i = 0; i < opponent.segments.Count; i++)
                {
                    if (i == 1 || (i == 0 && player.segments.Count > opponent.segments.Count)) continue;
                    float radius = i == 0 ? Arena.CollisionRadius * 2f : Arena.CollisionRadius * 1.55f;
                    float clearance = Point.Distance(player.segments[0], opponent.segments[i]) - radius;
                    if (clearance >= closest) continue;
                    closest = clearance; position = opponent.segments[i]; found = true;
                }
            }
            return found;
        }
    }

    // A compact original placeholder loop. It has no external audio asset and
    // can be replaced by properly licensed tracks without changing gameplay.
    public static class MusicComposer
    {
        public const int SampleRate = 22050;
        public const int Seconds = 8;
        private static readonly int[] HomeNotes = { 0, 4, 7, 4, 9, 7, 4, 2, 5, 9, 12, 9, 7, 5, 4, 2,
            0, 4, 7, 12, 11, 7, 4, 2, 5, 7, 9, 7, 4, 2, 0, 0 };
        private static readonly int[] BattleNotes = { 0, 7, 4, 7, 9, 7, 4, 7, 5, 9, 5, 9, 7, 4, 2, 4,
            0, 7, 12, 7, 11, 7, 4, 7, 5, 9, 12, 9, 7, 4, 2, 0 };
        private static readonly int[] Bass = { -12, -7, -10, -7 };

        public static float[] Create(bool battle)
        {
            var notes = battle ? BattleNotes : HomeNotes;
            var samples = new float[SampleRate * Seconds];
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)SampleRate;
                int eighth = Math.Min(31, (int)(t * 4));
                double noteTime = t - eighth * .25;
                double noteEnvelope = Math.Min(1, noteTime * 60) * Math.Exp(-noteTime * (battle ? 9 : 11));
                double frequency = 261.625565 * Math.Pow(2, notes[eighth] / 12.0);
                double lead = Math.Sin(2 * Math.PI * frequency * noteTime) + .24 * Math.Sin(4 * Math.PI * frequency * noteTime);
                int beat = Math.Min(15, (int)(t * 2));
                double bassTime = t - beat * .5;
                double bassFrequency = 261.625565 * Math.Pow(2, Bass[beat / 4] / 12.0);
                double bass = Math.Sin(2 * Math.PI * bassFrequency * bassTime) * Math.Exp(-bassTime * 5);
                double boundary = Math.Min(1, Math.Min(t * 40, (Seconds - t) * 40));
                samples[i] = (float)((lead * noteEnvelope * .15 + bass * .12) * Math.Max(0, boundary));
            }
            return samples;
        }
    }

    public static class AudioLevels
    {
        // Five explicit levels make the setting usable without a tiny slider.
        public static int Next(int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            return value <= 0 ? 100 : Math.Max(0, ((value - 1) / 25) * 25);
        }
    }

    // Rebase event counters after suspension so stale events are not replayed.
    public sealed class FeedbackCursor
    {
        private bool baseline = true;
        private int tick = -1;
        public bool PendingBaseline => baseline;
        public void Suspend() => baseline = true;
        public bool Observe(int currentTick)
        {
            bool reset = baseline || currentTick < tick;
            baseline = false; tick = currentTick;
            return reset;
        }
    }

    public static class MusicPolicy
    {
        public static bool ShouldPlay(bool enabled, bool interacted, bool focused, bool paused, bool adShowing) =>
            enabled && interacted && focused && !paused && !adShowing;
    }

    [Serializable]
    public sealed class CombatBonuses
    {
        public float turn, boostSaving, pickup;
        public float nearWallTurn, shortBoostSaving, reboundTurn;
        public float boostSavingCap = .07f;
        // Temporary depth-mode effects; never used by the normalized daily rules.
        public float captureShieldSeconds, reboundFreeSeconds, boostCostPenalty;
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
            public bool reboundBoostReady, reboundBoostWaitRelease;
            public float bossReady, bossWindup, bossRush, bossHitCooldown;
            public int bossAttack;
            public Point bossDirection;
            public CombatBonuses bonuses = new CombatBonuses();
        }

        public readonly ArenaState State;
        public int FruitRevision { get; private set; }
        private readonly Dictionary<string, Control> controls = new Dictionary<string, Control>();
        // These buffers belong to one arena and are cleared before each use.
        // No simulation state or completed capture polygon references them.
        private readonly Dictionary<string, Point> previousHeadsBuffer = new Dictionary<string, Point>();
        private readonly Dictionary<string, CaptureLoop> captureLoopsBuffer = new Dictionary<string, CaptureLoop>();
        private readonly Dictionary<string, string> captureDeathsBuffer = new Dictionary<string, string>();
        private readonly Dictionary<string, string> collisionDeathsBuffer = new Dictionary<string, string>();
        private readonly Dictionary<string, string> collisionReasonsBuffer = new Dictionary<string, string>();
        private readonly List<CapturePoint> capturePathBuffer = new List<CapturePoint>(MaximumLength);
        private readonly List<string> protectedBossesBuffer = new List<string>();
        private readonly Random random;
        private readonly bool nearbyOpponents, enableCaptures, rotatingEvents;
        private bool starEnabled = true;
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

        // Shared initialization for ordinary PvE and its future replay verifier.
        // Rules still use floating point and System.Random; cross-runtime replay
        // must be demonstrated before this can be used to validate rewards.
        public static Arena CreateStandardPve(int seed, bool firstRun, string playerName)
        {
            var arena = new Arena(seed, roundSeconds: firstRun ? 60f : 90f,
                nearbyOpponents: true, enableCaptures: true, rotatingEvents: true);
            arena.AddSnake("you", playerName, false);
            for (int i = 0; i < 7; i++) arena.AddSnake("bot-" + i, "AI " + (i + 1), true);
            arena.Start();
            return arena;
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

        public void DisableStar()
        {
            if (State.phase == "waiting") starEnabled = false;
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
            var previousHeads = previousHeadsBuffer;
            previousHeads.Clear();
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
                if (!control.boost) control.reboundBoostWaitRelease = false;
                if (control.reboundBoostReady && snake.boosting && !control.reboundBoostWaitRelease)
                {
                    control.freeBoostTime = Math.Max(0, control.bonuses.reboundFreeSeconds);
                    control.reboundBoostReady = false;
                }
                if (!snake.boosting) control.freeBoostTime = 0;
                bool freeBoost = snake.boosting && control.freeBoostTime > 0;
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
                        if (control.bonuses.reboundFreeSeconds > 0)
                        {
                            control.reboundBoostReady = true;
                            control.reboundBoostWaitRelease = control.boost;
                        }
                    }
                }
                snake.segments[0] = next;
                for (int i = 1; i < snake.segments.Count; i++)
                {
                    Point delta = snake.segments[i - 1] - snake.segments[i];
                    float distance = delta.Length;
                    if (distance > SegmentSpacing) snake.segments[i] = snake.segments[i - 1] - delta * (SegmentSpacing / distance);
                }
                if (snake.boosting && !freeBoost)
                {
                    float saving = control.bonuses.boostSaving + (snake.segments.Count <= 12 ? control.bonuses.shortBoostSaving : 0);
                    control.boostDebt += dt * 0.4f * (1f - Clamp(saving, 0, Clamp(control.bonuses.boostSavingCap, 0, .20f))) *
                        (1f + Clamp(control.bonuses.boostCostPenalty, 0, .5f));
                    if (control.boostDebt >= 1f && snake.segments.Count > MinimumLength)
                    {
                        snake.segments.RemoveAt(snake.segments.Count - 1);
                        control.boostDebt -= 1f;
                    }
                }
                if (freeBoost) control.freeBoostTime = Math.Max(0, control.freeBoostTime - dt);
                snake.reboundBoostReady = control.reboundBoostReady;
                snake.reboundBoostWaitRelease = control.reboundBoostReady && control.reboundBoostWaitRelease;
                snake.reboundBoostTime = control.freeBoostTime;
            }
            if (rotatingEvents) BeginRotatingEventIfDue(dt);
            ResolveCollisions();
            if (enableCaptures) ResolveCaptures(previousHeads);
            CollectFruits();
            if (starEnabled && (!rotatingEvents || (State.eventStatus == "active" && State.eventKind == "star"))) UpdateStar(dt);
            if (rotatingEvents) UpdateRotatingEvent(dt);
            int alive = 0;
            foreach (var snake in State.snakes) if (snake.alive) alive++;
            if (State.remaining <= 0 || (participantsAtStart >= 2 && alive <= 1)) Finish();
        }

        private static CapturePoint CapturePosition(Point point) => new CapturePoint((int)Math.Round(point.x * 1000), (int)Math.Round(point.y * 1000));

        private void ResolveCaptures(Dictionary<string, Point> previousHeads)
        {
            var loops = captureLoopsBuffer; loops.Clear();
            foreach (var snake in State.snakes)
            {
                if (!snake.alive || snake.shield > 0 || controls[snake.id].captureCooldown > 0 || !previousHeads.ContainsKey(snake.id)) continue;
                capturePathBuffer.Clear();
                foreach (var point in snake.segments) capturePathBuffer.Add(CapturePosition(point));
                if (!CaptureGeometry.TryLoop(CapturePosition(previousHeads[snake.id]), capturePathBuffer, out var loop)) continue;
                loops[snake.id] = loop; controls[snake.id].captureCooldown = .75f;
            }
            var deaths = captureDeathsBuffer; deaths.Clear();
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
                var victim = Find(pair.Key); victim.alive = false; victim.boosting = false; victim.eliminatedBy = pair.Value; victim.eliminationReason = "capture";
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
            if (Find(shown).alive)
                foreach (var pair in deaths) if (pair.Value == shown) State.captureCount++;
        }

        private void ResolveCollisions()
        {
            var deaths = collisionDeathsBuffer; deaths.Clear();
            var reasons = collisionReasonsBuffer; reasons.Clear();
            // Compare heads once, before applying any deaths, so equal clashes are fair.
            for (int i = 0; i < State.snakes.Count; i++)
            {
                var a = State.snakes[i];
                if (!a.alive || a.shield > 0) continue;
                for (int j = i + 1; j < State.snakes.Count; j++)
                {
                    var b = State.snakes[j];
                    if (!b.alive || b.shield > 0 || Point.Distance(a.segments[0], b.segments[0]) >= CollisionRadius * 2f) continue;
                    if (a.segments.Count <= b.segments.Count && !deaths.ContainsKey(a.id))
                    { deaths[a.id] = a.segments.Count == b.segments.Count ? "" : b.id; reasons[a.id] = a.segments.Count == b.segments.Count ? "head_tie" : "head"; }
                    if (b.segments.Count <= a.segments.Count && !deaths.ContainsKey(b.id))
                    { deaths[b.id] = a.segments.Count == b.segments.Count ? "" : a.id; reasons[b.id] = a.segments.Count == b.segments.Count ? "head_tie" : "head"; }
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
                        reasons[attacker.id] = "trail";
                        break;
                    }
                    if (deaths.ContainsKey(attacker.id)) break;
                }
            }
            ApplyBossDefense(deaths);
            foreach (var pair in deaths)
            {
                var victim = Find(pair.Key);
                if (victim != null) { victim.alive = false; victim.boosting = false; victim.eliminatedBy = pair.Value ?? ""; victim.eliminationReason = reasons[pair.Key]; }
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
            var protectedBosses = protectedBossesBuffer; protectedBosses.Clear();
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

        internal static Point HeadAvoidance(SnakeState snake, SnakeState other)
        {
            if (snake == null || other == null || !snake.alive || !other.alive || snake.shield > 1f || other.shield > 1f ||
                snake.segments.Count == 0 || other.segments.Count < snake.segments.Count) return new Point();
            Point away = snake.segments[0] - other.segments[0]; float distance = away.Length;
            return distance > .01f && distance < 7f ? away.Normalized * ((7f - distance) * 1.6f) : new Point();
        }

        internal static Point PlayerPursuit(SnakeState snake, SnakeState player, bool starEvent)
        {
            if (snake == null || player == null || !snake.alive || !player.alive || player.bot ||
                (snake.id != "bot-0" && snake.id != "bot-1") || starEvent ||
                snake.segments.Count == 0 || player.segments.Count == 0) return new Point();
            Point toward = player.segments[0] - snake.segments[0]; float distance = toward.Length;
            return distance < 50f && (distance > 12f || snake.segments.Count >= player.segments.Count)
                ? toward.Normalized * 4.2f : new Point();
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
                if (rotatingEvents) desired += HeadAvoidance(snake, other);
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
            // Two ordinary PvE opponents keep the player in their search,
            // using only the current position. Event pursuit can override it.
            if (rotatingEvents && (snake.id == "bot-0" || snake.id == "bot-1"))
            {
                var quarry = State.snakes.Find(s => !s.bot && s.alive);
                desired += PlayerPursuit(snake, quarry, State.eventStatus == "active" && State.eventKind == "star");
            }
            if (State.starVisible && string.IsNullOrEmpty(State.starHolder))
                desired += (State.starPosition - head).Normalized * 0.8f;
            if (State.starVisible && !string.IsNullOrEmpty(State.starHolder))
            {
                var carrier = Find(State.starHolder);
                if (carrier != null && carrier.alive && carrier.segments.Count > 0)
                {
                    if (carrier != snake && Point.Distance(head, carrier.segments[0]) < 45f)
                        desired += (carrier.segments[0] - head).Normalized * 2.6f;
                    else if (carrier == snake && nearest != null && nearestDistance < 18f)
                        desired += (head - nearest.segments[0]).Normalized * 2f;
                }
            }
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
