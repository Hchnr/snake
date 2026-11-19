using System;
using System.Threading;

namespace SnakeClub.Core
{
    // A single presentation, not a proof of a verified platform reward.
    public sealed class RewardedAdAttempt
    {
        private readonly Action<bool, string> done;
        private readonly Action cleanup;
        // 0 active, 1 finishing/cleaning, 2 completed, 3 cancelled.
        private int state;
        public bool Finished => Volatile.Read(ref state) != 0;

        public RewardedAdAttempt(Action<bool, string> done, Action cleanup)
        {
            this.done = done ?? throw new ArgumentNullException(nameof(done));
            this.cleanup = cleanup;
        }

        public void Finish(bool completed, string error)
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
            try { cleanup?.Invoke(); } catch (Exception) { }
            // The owner may have been destroyed reentrantly during cleanup.
            if (Interlocked.CompareExchange(ref state, 2, 1) == 1) done(completed, error ?? "");
        }

        public void Cancel()
        {
            while (true)
            {
                int old = Volatile.Read(ref state);
                if (old == 2 || old == 3) return;
                if (Interlocked.CompareExchange(ref state, 3, old) != old) continue;
                if (old == 0) try { cleanup?.Invoke(); } catch (Exception) { }
                return;
            }
        }
    }
}
