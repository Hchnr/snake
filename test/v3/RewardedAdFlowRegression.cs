using System;
using SnakeClub.Core;

sealed class FakeRewardedAd : IRewardedAdAdapter
{
    public Action<bool> Closed; public Action Error; public Action<bool> StaleClose;
    public string ThrowAt = ""; public bool SyncClose, SyncError, ReenterOnDestroy, CallbackDuringSubscribe, CallbackBeforeCloseAttach, CallbackBeforeErrorAttach;
    public Action DuringDestroy;
    public int Shows, Destroys, CloseUnsubscribes, ErrorUnsubscribes;
    private void Throw(string phase) { if (ThrowAt == phase) throw new InvalidOperationException(phase); }
    public void SubscribeClose(Action<bool> callback) { StaleClose = callback; if (CallbackBeforeCloseAttach) callback(false); Closed = callback; if (CallbackDuringSubscribe) callback(false); Throw("close-subscribe"); }
    public void SubscribeError(Action callback) { if (CallbackBeforeErrorAttach) callback(); Error = callback; Throw("error-subscribe"); }
    public void UnsubscribeClose() { CloseUnsubscribes++; Closed = null; Throw("close-unsubscribe"); }
    public void UnsubscribeError() { ErrorUnsubscribes++; Error = null; Throw("error-unsubscribe"); }
    public void Show() { Shows++; if (SyncClose) Closed?.Invoke(true); if (SyncError) Error?.Invoke(); Throw("show"); }
    public void Destroy() { Destroys++; DuringDestroy?.Invoke(); if (ReenterOnDestroy) StaleClose?.Invoke(true); Throw("destroy"); }
}

public static class RewardedAdFlowRegression
{
    static int calls, releases; static bool rewarded; static string message; static RewardedAdAttempt tracked;
    static void Require(bool condition, string label) { if (!condition) throw new Exception(label); }
    static void Reset() { calls = 0; releases = 0; rewarded = false; message = null; tracked = null; }
    static RewardedAdAttempt Start(Func<IRewardedAdAdapter> create) => RewardedAdFlow.Start(create, a => tracked = a,
        a => { releases++; if (ReferenceEquals(tracked,a)) tracked = null; }, (ok, msg) => { calls++; rewarded = ok; message = msg; });
    public static void Run()
    {
        foreach (string phase in new[] { "create", "close-subscribe", "error-subscribe", "show" })
        {
            Reset(); var fake = new FakeRewardedAd { ThrowAt = phase };
            var attempt = Start(() => phase == "create" ? throw new InvalidOperationException(phase) : fake);
            fake.StaleClose?.Invoke(true);
            Require(attempt.Finished && calls == 1 && !rewarded && releases == 1 && tracked == null &&
                (phase == "create" || fake.Destroys == 1), "synchronous failure at " + phase + " completes once and ignores late success");
        }
        Reset(); var ok = new FakeRewardedAd { SyncClose = true }; Start(() => ok);
        ok.StaleClose(true); Require(calls == 1 && rewarded && ok.Destroys == 1 && releases == 1, "synchronous close success once");
        Reset(); var syncError = new FakeRewardedAd { SyncError = true, ReenterOnDestroy = true }; Start(() => syncError);
        Require(calls == 1 && !rewarded && syncError.Destroys == 1 && releases == 1, "synchronous error plus reentrant destroy cannot award");
        Reset(); var subscribe = new FakeRewardedAd { CallbackDuringSubscribe = true }; Start(() => subscribe);
        Require(calls == 1 && !rewarded && subscribe.Shows == 0 && subscribe.Closed == null && subscribe.Destroys == 1, "synchronous close during subscription never shows");
        Reset(); var badDestroy = new FakeRewardedAd { ThrowAt = "destroy" }; var a = Start(() => badDestroy); badDestroy.Error();
        Require(a.Finished && calls == 1 && !rewarded && releases == 1 && tracked == null, "destroy exception still completes");
        Reset(); var badUnsubscribe = new FakeRewardedAd { ThrowAt = "close-unsubscribe" }; Start(() => badUnsubscribe); badUnsubscribe.Error();
        Require(calls == 1 && !rewarded && badUnsubscribe.ErrorUnsubscribes >= 1 && badUnsubscribe.Destroys == 1 && releases == 1, "one unsubscribe exception does not skip cleanup");
        Reset(); var cancel = new FakeRewardedAd(); var old = Start(() => cancel); old.Cancel(); cancel.StaleClose(true);
        Require(old.Finished && calls == 0 && releases == 1 && cancel.Destroys == 1, "owner destruction cancels without business callback");
        var next = new FakeRewardedAd { SyncClose = true }; Start(() => next); cancel.StaleClose(true);
        Require(calls == 1 && rewarded && next.Destroys == 1 && releases == 2, "stale callback cannot affect later attempt");
        Reset(); var reentrantOwner = new FakeRewardedAd { ReenterOnDestroy = false };
        RewardedAdAttempt owned = null;
        var completion = RewardedAdFlow.Start(() => reentrantOwner, a => owned = a,
            a => { releases++; owned.Cancel(); owned = null; }, (ok,msg) => { calls++; rewarded=ok; });
        reentrantOwner.Closed(true);
        Require(completion.Finished && calls == 0 && reentrantOwner.Destroys == 1 && releases == 1, "owner destroyed during completion cleanup suppresses business callback");
        Reset(); var createdDuringCancel = new FakeRewardedAd();
        var createCancelled = RewardedAdFlow.Start(() => { tracked.Cancel(); return createdDuringCancel; }, a => tracked=a,
            a => { releases++; if (ReferenceEquals(tracked,a)) tracked=null; }, (ok,msg) => { calls++; rewarded=ok; });
        Require(createCancelled.Finished && calls == 0 && releases == 1 && createdDuringCancel.Destroys == 1 && createdDuringCancel.Shows == 0,
            "owner cancellation during create destroys the late SDK instance");
        Reset(); bool created = false;
        var trackCancelled = RewardedAdFlow.Start(() => { created=true; return new FakeRewardedAd(); }, a => { tracked=a; a.Cancel(); },
            a => { releases++; if (ReferenceEquals(tracked,a)) tracked=null; }, (ok,msg) => { calls++; rewarded=ok; });
        Require(trackCancelled.Finished && !created && calls == 0 && releases == 1, "owner cancellation during track prevents SDK creation");
        Reset(); var closeBeforeAttach = new FakeRewardedAd { CallbackBeforeCloseAttach = true }; Start(() => closeBeforeAttach);
        Require(calls == 1 && !rewarded && closeBeforeAttach.Closed == null && closeBeforeAttach.Destroys == 1 && closeBeforeAttach.Shows == 0,
            "synchronous close before subscription finishes is detached without duplicate destroy");
        Reset(); var errorBeforeAttach = new FakeRewardedAd { CallbackBeforeErrorAttach = true }; Start(() => errorBeforeAttach);
        Require(calls == 1 && !rewarded && errorBeforeAttach.Error == null && errorBeforeAttach.Destroys == 1 && errorBeforeAttach.Shows == 0,
            "synchronous error before subscription finishes is detached without duplicate destroy");
        Reset(); var closeBeforeThrow = new FakeRewardedAd { CallbackBeforeCloseAttach = true, ThrowAt = "close-subscribe" }; Start(() => closeBeforeThrow);
        Require(calls == 1 && !rewarded && closeBeforeThrow.Closed == null && closeBeforeThrow.Destroys == 1 && closeBeforeThrow.Shows == 0,
            "close callback before attach then throw unhooks without duplicate destroy");
        Reset(); var errorBeforeThrow = new FakeRewardedAd { CallbackBeforeErrorAttach = true, ThrowAt = "error-subscribe" }; Start(() => errorBeforeThrow);
        Require(calls == 1 && !rewarded && errorBeforeThrow.Error == null && errorBeforeThrow.Closed == null && errorBeforeThrow.Destroys == 1 && errorBeforeThrow.Shows == 0,
            "error callback before attach then throw unhooks without duplicate destroy");
        Reset(); var destroyOwner = new FakeRewardedAd(); RewardedAdAttempt owner = null;
        destroyOwner.DuringDestroy = () => owner.Cancel();
        var ownerCase = RewardedAdFlow.Start(() => destroyOwner, a => owner=a, a=>releases++, (ok,msg)=>{calls++;rewarded=ok;});
        destroyOwner.Closed(true);
        Require(ownerCase.Finished && calls == 0 && destroyOwner.Destroys == 1 && releases == 1,
            "owner destroyed during SDK Destroy suppresses already finishing callback");
        Reset(); var silent = new FakeRewardedAd(); var unresolved = Start(() => silent);
        unresolved.AdvanceTimeout(1000, false); unresolved.AdvanceTimeout(1000, true);
        for (int i = 0; i < 715; i++) unresolved.AdvanceTimeout(.25f, true);
        unresolved.AdvanceTimeout(float.NaN, true); unresolved.AdvanceTimeout(-1, true);
        Require(!unresolved.Finished && calls == 0 && tracked != null, "silent SDK timed out too early");
        for (int i = 0; i < 4; i++) unresolved.AdvanceTimeout(.25f, true);
        silent.StaleClose(true);
        Require(unresolved.Finished && calls == 1 && !rewarded && message.Contains("超时") &&
            releases == 1 && silent.Destroys == 1 && tracked == null, "timeout did not fail once or allowed a late reward");
        Reset(); var timely = new FakeRewardedAd(); var timelyAttempt = Start(() => timely);
        for (int i = 0; i < 719; i++) timelyAttempt.AdvanceTimeout(.25f, true);
        timely.Closed(true); timelyAttempt.AdvanceTimeout(.25f, true);
        Require(calls == 1 && rewarded && timely.Destroys == 1, "timeout changed a timely SDK completion");
    }
}
