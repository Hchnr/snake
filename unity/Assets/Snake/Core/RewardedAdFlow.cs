using System;
using System.Threading;

namespace SnakeClub.Core
{
    public interface IRewardedAdAdapter
    {
        void SubscribeClose(Action<bool> callback);
        void SubscribeError(Action callback);
        void UnsubscribeClose();
        void UnsubscribeError();
        void Show();
        void Destroy();
    }

    // Owns one SDK instance. A callback describes only the local display,
    // not a server-verified advertising reward.
    public static class RewardedAdFlow
    {
        public static RewardedAdAttempt Start(Func<IRewardedAdAdapter> create,
            Action<RewardedAdAttempt> track, Action<RewardedAdAttempt> release, Action<bool, string> done)
        {
            IRewardedAdAdapter ad = null;
            RewardedAdAttempt attempt = null;
            int destroyed = 0;
            void Unsubscribe()
            {
                if (ad == null) return;
                try { ad.UnsubscribeClose(); } catch (Exception) { }
                try { ad.UnsubscribeError(); } catch (Exception) { }
            }
            void DestroyAd()
            {
                if (ad == null || Interlocked.Exchange(ref destroyed, 1) != 0) return;
                Unsubscribe();
                try { ad.Destroy(); } catch (Exception) { }
            }
            attempt = new RewardedAdAttempt(done, () =>
            {
                DestroyAd();
                release?.Invoke(attempt);
            });
            try
            {
                track?.Invoke(attempt);
                if (attempt.Finished) return attempt;
                ad = create();
                if (ad == null) throw new InvalidOperationException("No rewarded ad instance");
                // Owner cancellation can happen reentrantly inside create.
                if (attempt.Finished) { DestroyAd(); return attempt; }
                ad.SubscribeClose(ended => attempt.Finish(ended, ended ? "" : "已取消观看"));
                if (attempt.Finished) { Unsubscribe(); return attempt; }
                ad.SubscribeError(() => attempt.Finish(false, "广告暂不可用"));
                if (attempt.Finished) { Unsubscribe(); return attempt; }
                ad.Show();
            }
            catch (Exception)
            {
                // A subscription can invoke a callback, then attach and throw.
                // Cleanup may have run before that late attachment completed.
                if (attempt.Finished) { Unsubscribe(); DestroyAd(); }
                else attempt.Finish(false, "广告暂不可用");
            }
            return attempt;
        }
    }
}
