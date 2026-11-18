#nullable disable
using System;
using System.Collections.Generic;

namespace SnakeClub.Core
{
    // Integer geometry is shared by the client and replay verifier. A loop is
    // made from the visible body, not from an invisible history of positions.
    public readonly struct CapturePoint
    {
        public readonly int x, y;
        public CapturePoint(int x, int y) { this.x = x; this.y = y; }
    }

    public sealed class CaptureLoop
    {
        public readonly List<CapturePoint> polygon;
        public readonly long doubleArea;
        public CaptureLoop(List<CapturePoint> polygon, long doubleArea) { this.polygon = polygon; this.doubleArea = doubleArea; }
    }

    public static class CaptureGeometry
    {
        public const int FirstBodySegment = 5;
        public const long MinimumDoubleArea = 8000000;

        private static long Cross(CapturePoint a, CapturePoint b, CapturePoint c) =>
            (long)(b.x - a.x) * (c.y - a.y) - (long)(b.y - a.y) * (c.x - a.x);

        private static bool Intersection(CapturePoint start, CapturePoint end, CapturePoint a, CapturePoint b,
            out CapturePoint point, out long numerator, out long denominator)
        {
            long rx = (long)end.x - start.x, ry = (long)end.y - start.y;
            long sx = (long)b.x - a.x, sy = (long)b.y - a.y;
            denominator = rx * sy - ry * sx;
            numerator = ((long)a.x - start.x) * sy - ((long)a.y - start.y) * sx;
            long u = ((long)a.x - start.x) * ry - ((long)a.y - start.y) * rx;
            point = default;
            if (denominator == 0) return false; // Parallel and collinear overlaps do not close a loop.
            if (denominator < 0) { denominator = -denominator; numerator = -numerator; u = -u; }
            if (numerator <= 0 || numerator > denominator || u < 0 || u > denominator) return false;
            point = new CapturePoint(start.x + (int)(rx * numerator / denominator), start.y + (int)(ry * numerator / denominator));
            return true;
        }

        public static bool TryLoop(CapturePoint previousHead, IReadOnlyList<CapturePoint> segments, out CaptureLoop loop)
        {
            loop = null;
            if (segments == null || segments.Count <= FirstBodySegment + 1) return false;
            long bestNumerator = 0, bestDenominator = 1, bestArea = 0;
            int bestSegment = -1;
            CapturePoint bestHit = default;
            for (int i = FirstBodySegment; i + 1 < segments.Count; i++)
            {
                if (!Intersection(previousHead, segments[0], segments[i], segments[i + 1], out var hit, out long n, out long d)) continue;
                // The polygon is hit, followed by segments 0..i. Compute its
                // area directly so rejected intersections allocate nothing.
                long twiceArea = (long)hit.x * segments[0].y - (long)segments[0].x * hit.y;
                for (int j = 0; j < i; j++)
                    twiceArea += (long)segments[j].x * segments[j + 1].y - (long)segments[j + 1].x * segments[j].y;
                twiceArea += (long)segments[i].x * hit.y - (long)hit.x * segments[i].y;
                twiceArea = Math.Abs(twiceArea);
                if (twiceArea < MinimumDoubleArea || (bestSegment >= 0 && n * bestDenominator >= bestNumerator * d)) continue;
                bestSegment = i; bestHit = hit; bestArea = twiceArea; bestNumerator = n; bestDenominator = d;
            }
            if (bestSegment < 0) return false;
            var polygon = new List<CapturePoint>(bestSegment + 2) { bestHit };
            for (int j = 0; j <= bestSegment; j++) polygon.Add(segments[j]);
            loop = new CaptureLoop(polygon, bestArea);
            return true;
        }

        // Even-odd scanline intersections match Contains for concave and
        // self-intersecting loops. Used only for non-authoritative feedback.
        public static void Scanline(IReadOnlyList<Point> polygon, float y, List<float> intersections)
        {
            intersections.Clear();
            if (polygon == null || polygon.Count < 3) return;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[j]; var b = polygon[i];
                if ((a.y > y) == (b.y > y)) continue;
                intersections.Add(a.x + (b.x - a.x) * (y - a.y) / (b.y - a.y));
            }
            intersections.Sort();
        }

        public static bool Contains(IReadOnlyList<CapturePoint> polygon, CapturePoint point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[j]; var b = polygon[i];
                long cross = Cross(a, b, point);
                if (cross == 0 && point.x >= Math.Min(a.x, b.x) && point.x <= Math.Max(a.x, b.x)
                    && point.y >= Math.Min(a.y, b.y) && point.y <= Math.Max(a.y, b.y)) return true;
                if ((a.y > point.y) != (b.y > point.y) && (b.y > a.y ? cross > 0 : cross < 0)) inside = !inside;
            }
            return inside;
        }
    }
}
