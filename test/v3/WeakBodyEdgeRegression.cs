using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    public static class WeakBodyEdgeRegression
    {
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

        private static SnakeState Snake(string id, params Point[] segments)
        {
            var snake = new SnakeState { id = id, alive = true, segments = new List<Point>(segments) };
            for (int i = 0; i < segments.Length; i++) snake.strengthWeakSegments.Add(false);
            return snake;
        }

        private static SnakeState Opponent(string id, float x, float y = 0)
            => Snake(id, new Point(x, y), new Point(x, y), new Point(x, y),
                new Point(x, y), new Point(x, y), new Point(x, y));

        public static void Run()
        {
            Point Project(Point p) => new Point(540 + p.x * 100, 960 - p.y * 100);
            var me = Snake("you", new Point(0, 0));
            var a = Opponent("a", 10); var b = Opponent("b", 8);
            a.strengthWeakSegments[3] = true; b.strengthWeakSegments[4] = true;
            var world = new ArenaState { phase = "running", snakes = new List<SnakeState> { me, a, b } };
            var viewport = new WeakBodyViewport(0, 0, 1080, 1920);
            bool Select(string previous, int segment, out WeakBodyEdgeTarget target)
                => WeakBodyEdgeIndicator.TrySelect(world, "you", Project, viewport, previous, segment, out target);

            Check(Select("", -1, out var selected) && selected.id == "b" && selected.segment == 4 &&
                selected.position.x == 8 && selected.screen.x == 1340, "did not point to the nearest actual weak body segment");
            Check(Select("a", 3, out selected) && selected.id == "a", "near-equal existing target switched too easily");
            b.segments[4] = new Point(6.5f, 0);
            Check(Select("a", 3, out selected) && selected.id == "b", "clearly closer target did not replace the previous one");
            b.segments[4] = new Point(10, 0);
            Check(Select("", -1, out selected) && selected.id == "a", "equal-distance targets were not ordered stably");
            world.snakes.Reverse();
            Check(Select("", -1, out selected) && selected.id == "a", "target depended on list order");

            b.segments[4] = new Point(0, 3);
            Check(!Select("", -1, out _), "edge pointer appeared while a marked weak segment was on screen");
            b.strengthWeakSegments[4] = false;
            a.segments[3] = new Point(5.7f, 0); // x=1110: between the entry and exit margins
            Check(!Select("", -1, out _) && Select("a", 3, out _), "screen-edge hysteresis did not prevent flicker");
            a.segments[3] = new Point(5.5f, 0); // x=1090: inside the retained-pointer margin
            Check(!Select("a", 3, out _), "pointer remained after weak segment reached the screen edge");
            a.segments[3] = new Point(0, 10.6f); // y=-100: outside standard viewport but inside a tall safe area
            Check(Select("", -1, out _) &&
                !WeakBodyEdgeIndicator.TrySelect(world, "you", Project, new WeakBodyViewport(0, -200, 1080, 2120), "", -1, out _),
                "visible weak point in a tall safe area was treated as off-screen");
            Check(!WeakBodyEdgeIndicator.TrySelect(world, "you", Project, new WeakBodyViewport(0, 0, 0, 1), "", -1, out _),
                "invalid viewport produced a pointer");
            a.segments[3] = new Point(31, 0);
            Check(!Select("", -1, out _), "distant target was offered as nearby");
            a.segments[3] = new Point(30, 0);
            Check(Select("", -1, out _), "distance boundary excluded a real target");

            a.shield = 1; Check(!Select("", -1, out _), "protected target retained a pointer"); a.shield = 0;
            me.shield = 1; Check(!Select("", -1, out _), "protected player received a pointer"); me.shield = 0;
            world.strengthHeadThreat = "b"; Check(!Select("", -1, out _), "nearby lethal head warning did not take precedence"); world.strengthHeadThreat = "";
            a.alive = false; Check(!Select("a", 3, out _), "dead target retained a pointer"); a.alive = true;
            me.alive = false; Check(!Select("a", 3, out _), "dead player retained a pointer"); me.alive = true;
            world.phase = "ended"; Check(!Select("", -1, out _), "finished round retained a pointer"); world.phase = "running";
            a.segments.RemoveAt(5); Check(!Select("", -1, out _), "minimum-length target retained a pointer");
            a.segments.Add(new Point(10, 0)); a.strengthWeakSegments[3] = false;
            Check(!Select("", -1, out _), "unmarked or cooling-down target retained a pointer");
            a.strengthWeakSegments[3] = true;
            Check(!WeakBodyEdgeIndicator.TrySelect(world, "you", _ => new Point(float.NaN, 0), viewport, "", -1, out _), "invalid projection produced a pointer");
            Check(StrengthTutorialHints.Select(80, false, false).Contains("弱点箭头") &&
                StrengthTutorialHints.Select(80, true, true).Contains("可尝试吞食"), "tutorial did not explain the non-guaranteed weak target");
            a.segments[3] = new Point(float.PositiveInfinity, 0);
            Check(!Select("", -1, out _), "invalid world coordinate produced a pointer");

            var live = new PveFixedWorld(11, false, null, true, localAdvantage: true, localOpeningFood: true,
                localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
                localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true,
                localMultiBite: true, localHeadTieAfterBite: true, localOpeningBoost: true, localFreeBoost: true);
            while (!live.Finished && live.Tick < 60) live.Step(0, false, true);
            var actual = live.RenderState();
            string before = StrengthPveReplayVector.HiddenFingerprint(live);
            Check(WeakBodyEdgeIndicator.TrySelect(actual, "you", p => new Point(1800 + p.x, 2500 + p.y), viewport, "", -1, out selected),
                "actual v14 protection expiry did not expose a real weak segment in the controlled projection");
            var selectedSnake = actual.snakes.Find(s => s.id == selected.id);
            Check(selectedSnake != null && selectedSnake.strengthWeakSegments[selected.segment] &&
                selected.position.x == selectedSnake.segments[selected.segment].x && selected.position.y == selectedSnake.segments[selected.segment].y,
                "pointer did not follow the simulation's actual marked segment");
            Check(StrengthPveReplayVector.HiddenFingerprint(live) == before, "presentation changed simulation state");

            var danger = new PveFixedWorld(7, false, null, true, localAdvantage: true, localOpeningFood: true,
                localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
                localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true,
                localMultiBite: true, localHeadTieAfterBite: true, localOpeningBoost: true, localFreeBoost: true);
            var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(danger);
            fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
            fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(5000, 0, 0, 80));
            fighters[2] = new PveFixedFighter("bot-2", new PveFixedMotion(-20000, 0, 0, 16));
            for (int i = 3; i < fighters.Length; i++) fighters[i].alive = false;
            var threatened = danger.RenderState();
            Check(threatened.strengthHeadThreat == "bot-1" && threatened.snakes.Skip(1).Any(s => s.strengthWeakSegments.Contains(true)) &&
                !WeakBodyEdgeIndicator.TrySelect(threatened, "you", p => new Point(1800 + p.x, 2500 + p.y), viewport, "", -1, out _),
                "actual lethal head did not suppress the separate weak target");
            fighters[1].shieldTicks = 5; threatened = danger.RenderState();
            Check(string.IsNullOrEmpty(threatened.strengthHeadThreat) &&
                WeakBodyEdgeIndicator.TrySelect(threatened, "you", p => new Point(1800 + p.x, 2500 + p.y), viewport, "", -1, out _),
                "safe separate weak target did not return after the head threat cleared");
        }
    }
}
