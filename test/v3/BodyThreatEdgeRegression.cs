using System;
using System.Collections.Generic;
using System.Linq;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    public static class BodyThreatEdgeRegression
    {
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        private static SnakeState Player(params Point[] segments)
        {
            var snake = new SnakeState { id = "you", alive = true, segments = new List<Point>(segments) };
            foreach (var _ in segments) snake.strengthThreatSegments.Add(false);
            return snake;
        }

        public static void Run()
        {
            Point Project(Point p) => new Point(540 + p.x * 100, 960 - p.y * 100);
            var player = Player(new Point(0, 0), new Point(-8, 0), new Point(-10, 0));
            player.strengthThreatSegments[1] = player.strengthThreatSegments[2] = true;
            var world = new ArenaState { phase = "running", snakes = new List<SnakeState> { player } };
            var viewport = new WeakBodyViewport(0, 0, 1080, 1920);
            bool Select(int previous, out bool offscreen, out BodyThreatEdgeTarget target)
                => BodyThreatEdgeIndicator.TrySelect(world, "you", Project, viewport, previous, out offscreen, out target);
            Check(Select(-1, out bool off, out var target) && off && target.segment == 1 && target.position.x == -8 && target.screen.x == -260,
                "did not select actual nearest offscreen threatened body");
            Check(Select(2, out off, out target) && target.segment == 2, "near-equal previous threat switched too easily");
            player.segments[2] = new Point(-12, 0);
            Check(Select(2, out off, out target) && target.segment == 1, "clearly closer threat did not replace previous segment");
            player.segments[2] = new Point(0, 3);
            Check(!Select(-1, out off, out _) && !off, "offscreen warning appeared with a visible marked body segment");
            player.strengthThreatSegments[2] = false;
            player.segments[1] = new Point(-5.7f, 0); // x=-30, outside screen but inside entry margin
            Check(!Select(-1, out off, out _) && off, "near-edge invisible threat was described as visible");
            Check(Select(1, out off, out _) && off, "previous arrow was not retained within entry margin");
            player.segments[1] = new Point(-5.5f, 0); // x=-10, within the stay margin
            Check(!Select(1, out off, out _) && off, "near-edge pointer did not clear or offscreen status was lost");
            player.segments[1] = new Point(-5.4f, 0); // x=0, on the screen boundary
            Check(!Select(-1, out off, out _) && !off, "visible boundary marked as offscreen");
            player.segments[1] = new Point(0, 10.6f);
            Check(Select(-1, out off, out _) && off &&
                !BodyThreatEdgeIndicator.TrySelect(world, "you", Project, new WeakBodyViewport(0, -200, 1080, 2120), -1, out off, out _) && !off,
                "tall safe area did not include visible threat");
            player.segments[1] = new Point(-40, 0);
            Check(Select(-1, out off, out target) && off && target.segment == 1, "long body threat was wrongly limited to 30 world units");
            player.shield = 1; Check(!Select(-1, out off, out _) && !off, "protected player received warning"); player.shield = 0;
            player.alive = false; Check(!Select(-1, out off, out _) && !off, "dead player received warning"); player.alive = true;
            world.phase = "ended"; Check(!Select(-1, out off, out _) && !off, "ended round received warning"); world.phase = "running";
            player.strengthThreatSegments[1] = false; Check(!Select(-1, out off, out _) && !off, "unmarked segment received warning");
            player.strengthThreatSegments[1] = true;
            Check(!BodyThreatEdgeIndicator.TrySelect(world, "you", _ => new Point(float.NaN, 0), viewport, -1, out off, out _) && !off,
                "invalid projection claimed an offscreen threat");
            Check(!BodyThreatEdgeIndicator.TrySelect(world, "you", Project, new WeakBodyViewport(0, 0, 0, 1), -1, out off, out _) && !off,
                "invalid viewport claimed an offscreen threat");
            player.segments[1] = new Point(float.PositiveInfinity, 0);
            Check(!Select(-1, out off, out _) && !off, "invalid world coordinate claimed a threat");

            var live = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningFood: true,
                localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
                localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true,
                localMultiBite: true, localHeadTieAfterBite: true, localOpeningBoost: true, localFreeBoost: true,
                localOpeningDefense: true, localOpeningFruit: true);
            var fighters = (PveFixedFighter[])live.Fighters;
            fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
            fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-11700, -400, 49152, 80));
            for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
            var state = live.RenderState(); var before = StrengthPveReplayVector.HiddenFingerprint(live);
            Check(state.snakes[0].strengthThreatSegments.Any(x => x) &&
                BodyThreatEdgeIndicator.TrySelect(state, "you", p => new Point(1800 + p.x, 2500 + p.y), viewport, -1, out off, out target) && off &&
                state.snakes[0].strengthThreatSegments[target.segment] && target.position.x == state.snakes[0].segments[target.segment].x &&
                target.position.y == state.snakes[0].segments[target.segment].y, "pointer did not follow a real marked player segment");
            Check(StrengthPveReplayVector.HiddenFingerprint(live) == before, "presentation changed simulation state");
            fighters[1].shieldTicks = 1; state = live.RenderState();
            Check(!BodyThreatEdgeIndicator.TrySelect(state, "you", p => new Point(1800 + p.x, 2500 + p.y), viewport, -1, out off, out _) && !off,
                "protected attacker retained a threat warning");
        }
    }
}
