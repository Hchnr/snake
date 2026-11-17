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

    // Isolated first draft of a deterministic ordinary PvE match. This is
    // not yet a versioned release ruleset or a trusted reward verifier.
    public sealed class PveFixedWorld
    {
        public const int TickRate = 20;
        private uint random;
        private readonly int maxTicks;
        private readonly PveFixedBonuses playerBonuses;
        private readonly PveFixedFighter[] fighters = new PveFixedFighter[8];
        private readonly int[] botTimer = new int[8];
        private readonly byte[] botDirection = new byte[8];
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
            playerBonuses = PveFixedBonuses.From(profile);
            for (int i = 0; i < Arena.FruitCount; i++) fruit.Add(NewFruit());
            for (int i = 0; i < fighters.Length; i++)
            {
                CapturePoint head = Spawn(i);
                int heading = Next(256);
                fighters[i] = new PveFixedFighter(i == 0 ? "you" : "bot-" + i,
                    new PveFixedMotion(head.x, head.y, heading * 256, DailyMatch.CaptureStartingLength)) { shieldTicks = 3 * TickRate };
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
        public void Step(byte heading, bool boost)
        {
            if (Finished) return;
            Tick++;
            for (int i = 0; i < fighters.Length; i++)
            {
                var fighter = fighters[i]; if (!fighter.alive) continue;
                if (fighter.shieldTicks > 0) fighter.shieldTicks--;
                fighter.motion.Step(i == 0 ? heading : BotHeading(i), i == 0 && boost,
                    i == 0 ? playerBonuses : new PveFixedBonuses());
            }
            PveFixedCombat.Resolve(fighters);
            for (int i = 0; i < fruit.Count; i++)
                for (int j = 0; j < fighters.Length; j++)
                {
                    var fighter = fighters[j]; if (!fighter.alive) continue;
                    int radius = 1250 * (10000 + (j == 0 ? playerBonuses.pickup : 0)) / 10000;
                    if (DistanceSquared(fighter.motion.X - fruit[i].position.x, fighter.motion.Y - fruit[i].position.y) > (long)radius * radius) continue;
                    fighter.motion.Grow(1); fruit[i] = NewFruit(); break;
                }
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
            { Finished = true; Winner = best >= 0 && !tied ? fighters[best].id : ""; }
        }
    }
}
