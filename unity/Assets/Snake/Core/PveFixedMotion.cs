#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    // Experimental movement primitive, not a complete match or reward verifier.
    // World positions use thousandths, angles use 65536 steps per turn and
    // bonuses use basis points. No floating point affects its state.
    public sealed class PveFixedMotion
    {
        private static readonly int[] QuarterSin = {
            0,245,491,736,980,1224,1467,1710,1951,2191,2430,2667,2903,3137,3369,3599,
            3827,4052,4276,4496,4714,4929,5141,5350,5556,5758,5957,6152,6344,6532,6716,6895,
            7071,7242,7410,7572,7730,7883,8032,8176,8315,8449,8577,8701,8819,8932,9040,9142,
            9239,9330,9415,9495,9569,9638,9700,9757,9808,9853,9892,9925,9952,9973,9988,9997,10000
        };
        public const int Edge = 109280, MinimumLength = 5;
        private const int TurnStep = 1982, Spacing = 900;
        private readonly List<CapturePoint> segments = new List<CapturePoint>(Arena.MaximumLength);
        public IReadOnlyList<CapturePoint> Segments => segments;
        public CapturePoint PreviousHead { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Angle { get; private set; }
        public int Length => segments.Count;
        public int ReboundTicks { get; private set; }
        public int ReboundCooldown { get; private set; }
        public bool Boosting { get; private set; }
        private long boostDebt;
        public PveFixedMotion(int x, int y, int angle, int length)
        {
            X = Clamp(x, -Edge, Edge); Y = Clamp(y, -Edge, Edge);
            Angle = angle & 65535;
            int initialLength = Clamp(length, MinimumLength, Arena.MaximumLength);
            for (int i = 0; i < initialLength; i++)
                segments.Add(new CapturePoint(X - (int)((long)Sin(Angle + 16384) * i * Spacing / 10000),
                    Y - (int)((long)Sin(Angle) * i * Spacing / 10000)));
            PreviousHead = segments[0];
        }
        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        private static int Sin8(int angle)
        {
            angle &= 255;
            if (angle <= 64) return QuarterSin[angle];
            if (angle <= 128) return QuarterSin[128 - angle];
            if (angle <= 192) return -QuarterSin[angle - 128];
            return -QuarterSin[256 - angle];
        }
        private static int Sin(int angle)
        {
            angle &= 65535;
            int lower = angle >> 8, fraction = angle & 255;
            int a = Sin8(lower), b = Sin8(lower + 1);
            return a + (b - a) * fraction / 256;
        }
        // Direction vectors are scaled by 10000 and shared by AI/spawning.
        public static int DirectionX(byte heading) => Sin(heading * 256 + 16384);
        public static int DirectionY(byte heading) => Sin(heading * 256);
        // Integer square root gives the same segment relaxation in each runtime.
        internal static int Distance(int x, int y)
        {
            ulong n = (ulong)((long)x * x + (long)y * y), result = 0, bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0) { if (n >= result + bit) { n -= result + bit; result = (result >> 1) + bit; } else result >>= 1; bit >>= 2; }
            return (int)result;
        }
        public void Grow(int amount)
        {
            int count = Math.Min(Arena.MaximumLength - Length, Math.Max(0, amount));
            for (int i = 0; i < count; i++)
            {
                var tail = segments[Length - 1]; var previous = segments[Length - 2];
                int dx = tail.x - previous.x, dy = tail.y - previous.y;
                int distance = Distance(dx, dy);
                if (distance == 0) { dx = 1; dy = 0; distance = 1; }
                segments.Add(new CapturePoint(tail.x + (int)((long)dx * Spacing / distance),
                    tail.y + (int)((long)dy * Spacing / distance)));
            }
        }
        // Used only by the isolated strength combat prototype. The head and
        // minimum body remain intact; surviving segments keep their order.
        internal int RemoveBodySegments(int start, int count)
        {
            if (start < 1 || start >= Length || count <= 0) return 0;
            int removed = Math.Min(Math.Min(count, Length - start), Length - MinimumLength);
            if (removed > 0) segments.RemoveRange(start, removed);
            return removed;
        }
        // Detects geometry only: cooldowns, immunity and victim resolution
        // belong to the future match rules, not to this movement primitive.
        public bool TryCapture(out CaptureLoop loop) => CaptureGeometry.TryLoop(PreviousHead, segments, out loop);

        public void Step(byte target, bool boost, PveFixedBonuses bonuses)
        {
            PreviousHead = segments[0];
            if (ReboundTicks > 0) ReboundTicks--;
            if (ReboundCooldown > 0) ReboundCooldown--;
            int extraTurn = bonuses.turn;
            if (Math.Abs(X) > 100000 || Math.Abs(Y) > 100000) extraTurn += bonuses.nearWallTurn;
            if (ReboundTicks > 0) extraTurn += bonuses.reboundTurn;
            int turn = TurnStep * (10000 + Clamp(extraTurn, 0, 2000)) / 10000;
            int delta = ((target * 256 - Angle + 98304) & 65535) - 32768;
            Angle = (Angle + Clamp(delta, -turn, turn)) & 65535;
            bool boosting = boost && Length > MinimumLength;
            Boosting = boosting;
            int distance = boosting ? 495 : ReboundTicks > 0 ? 345 : 300;
            int x = X + Sin(Angle + 16384) * distance / 10000;
            int y = Y + Sin(Angle) * distance / 10000;
            bool bounced = false;
            if (x > Edge) { x = Edge - (x - Edge); Angle = (32768 - Angle) & 65535; bounced = true; }
            else if (x < -Edge) { x = -Edge + (-Edge - x); Angle = (32768 - Angle) & 65535; bounced = true; }
            if (y > Edge) { y = Edge - (y - Edge); Angle = (-Angle) & 65535; bounced = true; }
            else if (y < -Edge) { y = -Edge + (-Edge - y); Angle = (-Angle) & 65535; bounced = true; }
            X = Clamp(x, -Edge, Edge); Y = Clamp(y, -Edge, Edge);
            segments[0] = new CapturePoint(X, Y);
            for (int i = 1; i < segments.Count; i++)
            {
                var a = segments[i - 1]; var b = segments[i];
                int dx = a.x - b.x, dy = a.y - b.y, length = Distance(dx, dy);
                if (length > Spacing) segments[i] = new CapturePoint(a.x - (int)((long)dx * Spacing / length),
                    a.y - (int)((long)dy * Spacing / length));
            }
            if (bounced && ReboundCooldown == 0) { ReboundTicks = 16; ReboundCooldown = 160; }
            if (boosting)
            {
                int saving = Clamp(bonuses.boostSaving + (Length <= 12 ? bonuses.shortBoostSaving : 0), 0, 700);
                boostDebt += 200L * (10000 - saving);
                if (boostDebt >= 100000000L && Length > MinimumLength) { segments.RemoveAt(Length - 1); boostDebt -= 100000000L; }
            }
        }
    }

    public struct PveFixedBonuses
    {
        public int turn, boostSaving, pickup, nearWallTurn, shortBoostSaving, reboundTurn;
        public static PveFixedBonuses From(PlayerProgress profile)
        {
            if (profile == null) return new PveFixedBonuses();
            int[] ranks = profile.talents != null && profile.talents.Length == 9 ? profile.talents : new int[9];
            int total = 0;
            for (int i = 0; i < 6; i++) total += Math.Max(0, Math.Min(3, ranks[i]));
            int denominator = Math.Max(600, total * 50);
            int Value(int i) => Math.Max(0, Math.Min(3, ranks[i])) * 50 * 600 / denominator;
            var result = new PveFixedBonuses { turn = Value(0), boostSaving = Value(1), pickup = Value(2),
                nearWallTurn = Value(3), shortBoostSaving = Value(4), reboundTurn = Value(5) };
            if (profile.selectedSkin == 0) result.turn += 100;
            if (profile.selectedSkin == 1) result.boostSaving += 100;
            if (profile.selectedSkin == 2) result.pickup += 100;
            return result;
        }
    }
}
