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
            foreach (int seed in new[] { 0, 1, 12345, int.MaxValue })
            foreach (bool first in new[] { true, false })
            {
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
                }
            }
            return "random=" + rngHash + ";initial=" + initialHash + ";replay=" + hash.ToString("x16");
        }
    }
}
