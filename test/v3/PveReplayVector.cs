using System;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    // Diagnostic only: raw float bits deliberately expose any runtime drift.
    public static class PveReplayVector
    {
        private static ulong hash;
        private static void Add(int value)
        {
            unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; } }
        }
        private static void Add(float value) => Add(BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        private static void Add(string value)
        {
            if (value == null) { Add(-1); return; }
            Add(value.Length); foreach (char c in value) Add((int)c);
        }
        public static string Compute()
        {
            hash = 14695981039346656037UL;
            var rng = new Random(12345);
            for (int i = 0; i < 20; i++) { Add(rng.Next()); Add((float)rng.NextDouble()); }
            string rngHash = hash.ToString("x16");
            hash = 14695981039346656037UL;
            var initial = Arena.CreateStandardPve(12345, false, "玩家").State;
            foreach (var snake in initial.snakes)
            {
                Add(snake.angle); foreach (var point in snake.segments) { Add(point.x); Add(point.y); }
            }
            foreach (var fruit in initial.fruits) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
            string initialHash = hash.ToString("x16");
            hash = 14695981039346656037UL;
            foreach (var snake in initial.snakes) Add(snake.angle);
            string angleHash = hash.ToString("x16");
            hash = 14695981039346656037UL;
            foreach (var snake in initial.snakes) { Add(snake.segments[0].x); Add(snake.segments[0].y); }
            string headHash = hash.ToString("x16");
            string headBits = "";
            foreach (var snake in initial.snakes)
            {
                var head = snake.segments[0];
                if (headBits.Length != 0) headBits += ",";
                headBits += BitConverter.ToInt32(BitConverter.GetBytes(head.x), 0).ToString("x8") + ":" +
                    BitConverter.ToInt32(BitConverter.GetBytes(head.y), 0).ToString("x8");
            }
            hash = 14695981039346656037UL;
            foreach (var snake in initial.snakes) foreach (var point in snake.segments) { Add(point.x); Add(point.y); }
            string bodyHash = hash.ToString("x16");
            hash = 14695981039346656037UL;
            foreach (var fruit in initial.fruits) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
            string fruitHash = hash.ToString("x16");
            hash = 14695981039346656037UL;
            hash = 14695981039346656037UL;
            foreach (int seed in new[] { 0, 1, 12345, int.MaxValue })
            {
                var fixedMotion = new PveFixedMotion(seed % 10000, -1000, seed, 16);
                var fixedBonuses = PveFixedBonuses.From(new PlayerProgress { selectedSkin = 1, talents = new[] { 3, 2, 1, 3, 1, 2, 0, 0, 0 } });
                for (int i = 0; i < 1800; i++)
                {
                    fixedMotion.Step((byte)(i * 17 + seed), i % 100 >= 35, fixedBonuses);
                    if (i % 180 == 0) fixedMotion.Grow(1);
                    Add(fixedMotion.X); Add(fixedMotion.Y); Add(fixedMotion.Angle); Add(fixedMotion.Length); Add(fixedMotion.ReboundTicks);
                    foreach (var point in fixedMotion.Segments) { Add(point.x); Add(point.y); }
                    Add(fixedMotion.TryCapture(out var loop) ? (int)(loop.doubleArea % int.MaxValue) : 0);
                }
            }
            string fixedHash = hash.ToString("x16");
            hash = 14695981039346656037UL;
            string outcomes = "";
            foreach (int seed in new[] { 0, 1, 12345, int.MaxValue })
            foreach (bool first in new[] { true, false })
            {
                ulong outcomeHash = 14695981039346656037UL;
                var arena = Arena.CreateStandardPve(seed, first, "玩家");
                var profile = new PlayerProgress { selectedSkin = 2, talents = new[] { 1, 2, 1, 0, 1, 0, 0, 0, 0 } };
                arena.SetBonuses("you", Progression.Bonuses(profile));
                for (int tick = 0; tick < 500 && arena.State.phase == "running"; tick++)
                {
                    byte heading = (byte)(tick / 53 * 37 + seed);
                    arena.SetInput("you", PveRecording.Direction(heading), tick % 120 >= 95);
                    arena.Tick(.05f);
                    var state = arena.State;
                    Add(state.tick); Add(state.remaining); Add(state.phase); Add(state.winner);
                    Add(state.eventKind); Add(state.eventStatus); Add(state.starHolder); Add(state.starProgress);
                    foreach (var snake in state.snakes)
                    {
                        Add(snake.id); Add(snake.alive ? 1 : 0); Add(snake.angle); Add(snake.shield); Add(snake.kills); Add(snake.starPoints); Add(snake.segments.Count);
                        foreach (var point in snake.segments) { Add(point.x); Add(point.y); }
                    }
                    foreach (var fruit in state.fruits) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
                    ulong rawHash = hash; hash = outcomeHash;
                    Add(state.tick); Add(state.phase); Add(state.winner); Add(state.eventKind); Add(state.eventStatus);
                    Add(state.eventTarget); Add(state.starHolder); Add(state.captureTick); Add(state.captureOwner); Add(state.captureCount);
                    foreach (var snake in state.snakes)
                    {
                        Add(snake.id); Add(snake.alive ? 1 : 0); Add(snake.kills); Add(snake.starPoints);
                        Add(snake.segments.Count); Add(snake.eliminatedBy); Add(snake.eliminationReason);
                    }
                    outcomeHash = hash; hash = rawHash;
                }
                if (outcomes.Length != 0) outcomes += ",";
                outcomes += seed + ":" + (first ? "first" : "normal") + ":" + outcomeHash.ToString("x16");
            }
            return "fixed=" + fixedHash + ";random=" + rngHash + ";initial=" + initialHash + ";angles=" + angleHash + ";heads=" + headHash + ";headbits=" + headBits + ";bodies=" + bodyHash + ";fruits=" + fruitHash + ";replay=" + hash.ToString("x16") + ";outcomes=" + outcomes;
        }
    }
}
