using System.Collections.Generic;

namespace SnakeClub.Core
{
    // Screen coordinates use a top-left origin. No screen region is reserved.
    public sealed class DragSteering
    {
        private readonly float threshold;
        private int pointerId;
        private Point anchor;
        public bool Active { get; private set; }
        public bool Moved { get; private set; }

        public DragSteering(float threshold = 12f) { this.threshold = threshold; }

        public bool IsTracking(int id) => Active && pointerId == id;

        // Some hosts drop an end/cancel event when a finger leaves the app.
        // Never retain a vanished touch, and let real touches replace mouse emulation.
        public void ReconcileTouches(IReadOnlyList<int> activeTouchIds)
        {
            if (!Active || activeTouchIds == null) return;
            if (pointerId == -1)
            {
                if (activeTouchIds.Count > 0) Reset();
                return;
            }
            for (int i = 0; i < activeTouchIds.Count; i++) if (activeTouchIds[i] == pointerId) return;
            Reset();
        }

        public bool Begin(int id, Point position)
        {
            if (Active) return false;
            pointerId = id; anchor = position; Active = true; Moved = false;
            return true;
        }

        public bool Move(int id, Point position, out Point direction)
        {
            direction = new Point();
            if (!Active || pointerId != id) return false;
            Point delta = position - anchor;
            if (delta.Length < threshold) return false;
            anchor = position;
            direction = new Point(delta.x, -delta.y).Normalized;
            Moved = true;
            return true;
        }

        public bool End(int id)
        {
            if (!Active || pointerId != id) return false;
            bool moved = Moved;
            Reset();
            return moved;
        }

        public void Reset() { Active = false; Moved = false; }
    }
}
