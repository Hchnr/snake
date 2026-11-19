using System.Text.Json;
using System.Collections;
using System.Reflection;
using SnakeClub.Core;

if ((args.Length == 3 && args[0] == "strength-replay") || (args.Length == 4 && args[0] == "strength-growth-replay"))
{
    int seed = int.Parse(args[1]); bool growth = args[0] == "strength-growth-replay"; bool first = args[growth ? 3 : 2] == "1";
    var snapshot = growth ? StrengthEvalStartConfig.Capture(new PlayerProgress { experience = int.Parse(args[2]), tutorialComplete = !first }) : null;
    var world = new PveFixedWorld(seed, first, null, strengthPreview: true, strengthSnapshot: snapshot);
    var inputs = new List<byte>();
    while (!world.Finished)
    {
        byte heading = (byte)(world.Tick / 53 * 37 + seed);
        bool boost = world.Tick % 120 >= 95;
        inputs.Add(heading); inputs.Add(boost ? (byte)1 : (byte)0); world.Step(heading, boost);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { ticks = world.Tick, winner = world.Winner, kills = world.Fighters[0].kills,
        length = world.Fighters[0].motion.Length, stateDigest = StrengthStateDigest.Compute(world), inputs = Convert.ToBase64String(inputs.ToArray()) }));
    return;
}

if (args.Length == 4 && (args[0] == "strength-replay-terminal" || args[0] == "strength-advantage-replay-terminal" || args[0] == "strength-opening-replay-terminal" || args[0] == "strength-encounter-replay-terminal" || args[0] == "strength-recovery-replay-terminal" || args[0] == "strength-safe-recovery-replay-terminal" || args[0] == "strength-aligned-start-replay-terminal"))
{
    int seed = int.Parse(args[1]); bool first = args[2] == "1";
    byte[] tape = Convert.FromBase64String(args[3]);
    bool advantage = args[0] != "strength-replay-terminal", alignedStart = args[0] == "strength-aligned-start-replay-terminal", safeRecovery = args[0] == "strength-safe-recovery-replay-terminal" || alignedStart;
    bool recoverySupply = args[0] == "strength-recovery-replay-terminal" || safeRecovery;
    bool openingFood = args[0] == "strength-opening-replay-terminal" || args[0] == "strength-encounter-replay-terminal" || recoverySupply;
    bool openingEncounter = args[0] == "strength-encounter-replay-terminal" || recoverySupply;
    if (!PveFixedRecording.TryReplayStrength(seed, first, tape, out var replay, advantage, openingFood, openingEncounter, recoverySupply, safeRecovery, alignedStart)) throw new InvalidOperationException("Invalid recording");
    Console.WriteLine(JsonSerializer.Serialize(new { ticks = replay.Tick, hash = SnakeClub.Tests.StrengthPveReplayVector.Terminal(replay) }));
    return;
}

if (args.Length == 1 && args[0] == "strength-pve-vector")
{
    Console.WriteLine(SnakeClub.Tests.StrengthPveReplayVector.Compute());
    return;
}

if (args.Length == 1 && args[0] == "strength-recovery-vector")
{
    Console.WriteLine(SnakeClub.Tests.StrengthPveReplayVector.ComputeRecovery());
    return;
}

if (args.Length == 1 && args[0] == "strength-safe-recovery-vector")
{
    Console.WriteLine(SnakeClub.Tests.StrengthPveReplayVector.ComputeSafeRecovery());
    return;
}

if (args.Length == 1 && args[0] == "strength-aligned-start-vector")
{
    Console.WriteLine(SnakeClub.Tests.StrengthPveReplayVector.ComputeAlignedStart());
    return;
}

if (args.Length == 1 && args[0] == "pve-vector")
{
    Console.WriteLine(SnakeClub.Tests.PveReplayVector.Compute() + ";matrix=" + SnakeClub.Tests.PveReplayVector.ComputeMatrix());
    return;
}

if (args.Length >= 2 && args[0] == "fixed-replay")
{
    int seed = int.Parse(args[1]);
    var profile = args.Length > 2 ? new PlayerProgress() : new PlayerProgress { selectedSkin = 2, talents = new[] { 1, 2, 1, 0, 1, 0, 0, 0, 0 } };
    var world = new PveFixedWorld(seed, true, profile);
    var inputs = new List<byte>();
    while (!world.Finished)
    {
        byte heading = (byte)(world.Tick / 53 * 37 + seed);
        bool boost = world.Tick % 120 >= 95;
        inputs.Add(heading); inputs.Add(boost ? (byte)1 : (byte)0);
        world.Step(heading, boost);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { ticks = world.Tick, winner = world.Winner, kills = world.Fighters[0].kills,
        length = world.Fighters[0].motion.Length, eventPoints = world.Fighters[0].eventPoints, inputs = Convert.ToBase64String(inputs.ToArray()) }));
    return;
}

if (args.Length >= 2 && args[0] == "replay")
{
    int seed = int.Parse(args[1]);
    int variant = args.Length > 2 ? int.Parse(args[2]) : 0;
    string rules = args.Length > 3 ? args[3] : DailyMatch.RulesVersion;
    var match = new DailyMatch(seed, rules);
    var inputs = new List<byte>();
    while (!match.Finished)
    {
        int tick = match.State.tick;
        byte heading = (byte)((tick / 100 * 29 + variant * 61) & 255);
        bool boost = tick % 173 >= 140;
        match.Step(heading, boost);
        inputs.Add(heading); inputs.Add(boost ? (byte)1 : (byte)0);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { score = match.Score, ticks = match.State.tick, inputs = Convert.ToBase64String(inputs.ToArray()) }));
    return;
}

var cases = new (string name, Action run)[]
{
    ("incoming friend challenges deduplicate, retry and reject stale replies", IncomingFriendCases),
    ("platform login handles timeout, late and duplicate callbacks", PlatformLoginCases),
    ("session generations reject old login responses", SessionGenerationCases),
    ("profile snapshots reject outdated or foreign accounts", ProfileSnapshotCases),
    ("Douyin login handles valid, malformed and unavailable upstream responses", DouyinExchangeCases),
    ("first seven active days grow linearly and later growth slows", GrowthCurve),
    ("talent points, rank limits and PvE/PvP caps are enforced", TalentBudget),
    ("recommended talents allocate available points deterministically", TalentRecommendation),
    ("cave refresh invalidates stale responses and backs off failures", CaveSyncCases),
    ("expedition rewards respect time and bonus limits", ExpeditionRewards),
    ("expedition fragments unlock stories without spending inventory", CaveAlbum),
    ("wall bounce grants a brief pulse with a cooldown", BouncePulse),
    ("daily replay reproduces states and published score formula", DailyReplay),
    ("daily fruit projection reuses objects and reflects simulation changes", DailyFruitProjection),
    ("daily challenges use their own seed and are bounded", DailySeeds),
    ("friend comparison reports the verified score gap", FriendComparisonCases),
    ("legacy daily replays preserve their original result", LegacyDailyReplay),
    ("daily-2 recordings remain compatible with daily-3", PreviousDailyReplay),
    ("first-run hints respond to player actions and stop on time", FirstRunHintsCases),
    ("late-round timer follows live match state", RoundTimerCases),
    ("arena visual clock freezes across suspension", ArenaVisualClockCases),
    ("minimap fades on overlap and returns without flicker", MinimapOcclusionCases),
    ("drag steering works at any screen position and tracks one finger", FullScreenDrag),
    ("drag steering recovers missing touch ends and transfers control", TouchRecovery),
    ("audio feedback rebases after interruption without replaying old events", AudioFeedbackCursorCases),
    ("music respects interaction, suspension and bounded loop samples", MusicBehavior),
    ("settlement retry delays do not block other runs or accounts", IndependentSettlementRetries),
    ("pending settlement retries back off and stay bound to their account", PendingRetries),
    ("danger cue follows live, unshielded collision hazards", NearbyDanger),
    ("result celebration only follows a real win and respects reduced effects", ResultMotionCases),
    ("character motion follows real turns, boost and reduced effects", CharacterMotionCases),
    ("original characters have bounded distinct idle motion", OriginalIdleMotionCases),
    ("strength appearance follows real defense and temporary effects", StrengthAppearanceCases),
    ("growth view follows current length and bounded preferences", GrowthViewCases),
    ("elimination transitions require a real displayed life and reset cleanly", EliminationTransitionCases),
    ("elimination feedback follows observed credited outcomes", EliminationFeedbackCases),
    ("kill feedback reflects observed growth and freezes with match ticks", KillFeedbackCases),
    ("match outcomes explain actual collision and capture results", MatchOutcomeCases),
    ("capture sweep follows even-odd geometry for concave and crossing loops", CaptureSweepCases),
    ("capture geometry is deterministic and includes its boundary", CaptureGeometryCases),
    ("a starting-length player can actually close a loop", CapturePlayable),
    ("offline comparison holds the initial world constant and disables only capture", GameplayComparison),
    ("closed loops eliminate unshielded targets and grow the survivor", CaptureResolution),
    ("local arena reuses collision buffers without stale outcomes", ArenaBufferReuse),
    ("seeded PvE setup and inputs reproduce locally", PveReplayFoundation),
    ("ordinary PvE bots avoid head threats and pursue visible players", ArenaBotEncounterCases),
    ("PvE input tape replays bounded actual controls locally", PveRecordingCases),
    ("experimental body strength is bounded and weakens toward the tail", BodyStrengthCases),
    ("experimental strength combat bites real body segments deterministically", StrengthBodyCombatCases),
    ("strength practice world uses new body and head combat", StrengthPracticeWorldCases),
    ("strength pickup applies visible temporary power fairly and expires", StrengthPickupCases),
    ("strength loops reward real maneuver without elimination", StrengthLoopCases),
    ("strength match levels track actual actions after combat", StrengthLevelCases),
    ("strength practice uses neutral profiles and varied pickup anchors", StrengthFairnessCases),
    ("strength bots promptly avoid lethal heads but not protected or equal heads", StrengthBotHeadCases),
    ("strength bots seek fruit near protected opponents", StrengthBotForagingCases),
    ("strength bots avoid ineligible body targets", StrengthBotTargetEligibilityCases),
    ("strength contact feedback reports the latest real outcomes", StrengthNoticeCases),
    ("strength protection cue follows actual shield and expiry", StrengthProtectionNoticeCases),
    ("minimum-length strength boost explains why movement cannot accelerate", StrengthBoostNoticeCases),
    ("strength growth snapshots are bounded, frozen and affect combat", StrengthGrowthCases),
    ("strength replay diagnostics detect hidden-state divergence", StrengthHiddenDiagnosticsCases),
    ("strength preview complete recordings reproduce the match", StrengthRecordingCases),
    ("offline opening food gives real reachable early growth", StrengthOpeningFoodCases),
    ("offline opening opponent retains real combat and earlier rules", StrengthOpeningEncounterCases),
    ("offline recovery supply follows real bites and bounded opportunities", StrengthRecoverySupplyCases),
    ("safe recovery avoids nearby AI and preserves the v4 replay", StrengthSafeRecoveryCases),
    ("aligned offline start follows the default direction and preserves v5", StrengthAlignedStartCases),
    ("strength weak-point hints follow current combat inputs", StrengthWeakPointCases),
    ("nearby strength head warning reflects actual combat and protection", StrengthHeadWarningCases),
    ("strength body warnings follow real nearby threats", StrengthBodyThreatCases),
    ("completed local PvE replay ends on the same elimination step", CompletedPveRecordingCases),
    ("PvE starting configuration freezes trial skin and talents", PveConfigCases),
    ("integer PvE movement preserves steering, bounce and bonuses", PveFixedMotionCases),
    ("integer PvE combat respects actual collision and protection", PveFixedCombatCases),
    ("integer combat workspace does not leak prior outcomes", PveFixedCombatWorkspaceCases),
    ("integer PvE world replays AI and fruit deterministically", PveFixedWorldCases),
    ("integer PvE recording requires a complete and well-formed match", PveFixedRecordingCases),
    ("integer PvE render projection follows real state", PveFixedProjectionCases),
    ("integer PvE bots avoid real dangerous trails", PveBotAvoidanceCases),
    ("integer PvE events respect real outcomes and timing", PveFixedEventsCases),
    ("local arena captures multiple opponents without defeating protected targets", LocalCaptureResolution),
    ("match highlights preserve only real, credited capture peaks", HighlightSummary),
    ("capture notices identify the actual visible owner and count", CaptureNoticeCases),
    ("off-screen objective markers follow real targets and screen direction", EventTargetIndicatorCases),
    ("nearby off-screen opponent indicators use real actors and safe placement", OpponentEdgeCases),
    ("edge markers seek safe nearby slots without covering labels", EdgeMarkerLayoutCases),
    ("nameplates avoid overlap, anchors and the minimap", NameplateCases),
    ("visible combat notices reserve nameplate and edge-marker space", ContactNoticeLayoutCases),
    ("event outcome notices reflect observed events and actual points", EventOutcomeCases),
    ("star carriers attract nearby AI without changing daily-3 replay", StarPursuitCases),
    ("daily events are repeatable and start with nearby opponents", DailyEvents),
    ("single-player events rotate, award real captures and end cleanly", LocalRotatingEvents),
    ("skin identities preserve the original inventory and restrict reference previews", SkinIdentities),
    ("missing skin visuals fall back without changing ownership", SkinVisualFallback),
    ("depth rooms require the correct living player and objective", DepthGoals),
    ("depth star and displayed bonus caps match each room", DepthRoomFlow),
    ("depth bonuses change growth and make rebound boosts free", DepthBonuses),
    ("depth boss telegraphs attacks and loses shields before elimination", DepthBoss)
};
foreach (var test in cases)
{
    try { test.run(); Console.WriteLine("PASS " + test.name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + test.name + ": " + ex.Message); Environment.ExitCode = 1; }
}

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
static void ProfileSnapshotCases()
{
    var previous = new PlayerProgress { playerId = "a", revision = 3, coins = 100 };
    var late = new PlayerProgress { playerId = "a", revision = 2, coins = 35 };
    var newer = new PlayerProgress { playerId = "a", revision = 4, coins = 120 };
    Check(ProfileSnapshot.ShouldAccept(null, previous), "initial account rejected");
    Check(!ProfileSnapshot.ShouldAccept(previous, late), "older rewards snapshot accepted");
    Check(ProfileSnapshot.ShouldAccept(previous, newer), "newer account snapshot rejected");
    Check(ProfileSnapshot.ShouldAccept(previous, previous), "idempotent response rejected");
    Check(!ProfileSnapshot.ShouldAccept(previous, new PlayerProgress { playerId = "b", revision = 99 }), "foreign account accepted");
    Check(ProfileSnapshot.ShouldAccept(null, new PlayerProgress { playerId = "legacy" }), "legacy account rejected");
}

static void SessionGenerationCases()
{
    var session = new SessionGeneration();
    long first = session.Capture();
    Check(session.IsCurrent(first), "initial request was rejected");
    session.Advance(); // Login or a 401 invalidates every earlier request.
    long second = session.Capture();
    Check(!session.IsCurrent(first) && session.IsCurrent(second), "login did not invalidate old requests");
    session.Advance();
    Check(!session.IsCurrent(first) && !session.IsCurrent(second), "reconnect accepted stale responses");
    Check(session.IsCurrent(session.Capture()), "new session request was rejected");
}

static void DouyinExchangeCases()
{
    string seenUrl = "";
    var handler = new FakeHttpHandler((request, _) =>
    {
        Check(request.Method == HttpMethod.Get && request.Content.Headers.ContentType.MediaType == "application/json", "incorrect platform request method or content type");
        seenUrl = request.RequestUri.AbsoluteUri;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"error\":0,\"openid\":\"registered\",\"anonymous_openid\":\"visitor\",\"session_key\":\"private\"}") });
    });
    var client = new HttpClient(handler);
    var exchange = new DouyinSessionExchange(client, TimeSpan.FromMilliseconds(100));
    var result = exchange.Exchange("app id", "secret&key", "code+1", "anon/2").GetAwaiter().GetResult();
    Check(result.Status == 200 && result.OpenId == "registered" && result.AnonymousId == "visitor", "registered/anonymous identities were not retained");
    Check(seenUrl.Contains("appid=app%20id") && seenUrl.Contains("secret=secret%26key") && seenUrl.Contains("code=code%2B1") && seenUrl.Contains("anonymous_code=anon%2F2"), "request parameters were not encoded");
    ExchangeResult Reply(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        var stub = new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
        return new DouyinSessionExchange(new HttpClient(stub)).Exchange("app", "secret", null, "anon").GetAwaiter().GetResult();
    }
    result = Reply("{\"error\":0,\"anonymous_openid\":\"visitor\"}");
    Check(result.Status == 200 && result.OpenId == "visitor" && result.AnonymousId == "visitor", "anonymous identity not supported");
    Check(Reply("{\"error\":40018}").Status == 401 && Reply("{\"error\":40019}").Status == 401, "invalid one-use code not rejected");
    Check(Reply("{\"error\":40017}").Status == 503 && Reply("{\"error\":-1}").Status == 503, "platform or configuration failure misclassified");
    foreach (string bad in new[] { "not json", "[]", "{}", "{\"error\":\"zero\"}", "{\"error\":0,\"openid\":5}", new string('x', 17000) })
        Check(Reply(bad).Status == 503, "malformed or oversized platform response was accepted");
    Check(Reply("{\"error\":0}").Status == 401, "missing identity was accepted");
    Check(Reply("{}", System.Net.HttpStatusCode.ServiceUnavailable).Status == 503, "upstream HTTP error was not handled");
    Check(exchange.Exchange("app", "secret", "", "").GetAwaiter().GetResult().Status == 400, "empty code was sent upstream");
    var slow = new FakeHttpHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); });
    result = new DouyinSessionExchange(new HttpClient(slow), TimeSpan.FromMilliseconds(20)).Exchange("app", "do-not-leak", "code", null).GetAwaiter().GetResult();
    Check(result.Status == 503 && !result.Error.Contains("do-not-leak"), "timeout was not safe or exposed credentials");
    var failed = new FakeHttpHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("secret do-not-leak")));
    result = new DouyinSessionExchange(new HttpClient(failed)).Exchange("app", "do-not-leak", "code", null).GetAwaiter().GetResult();
    Check(result.Status == 503 && !result.Error.Contains("do-not-leak"), "network exception exposed credentials or did not degrade gracefully");
}

static void IncomingFriendCases()
{
    var incoming = new IncomingFriendChallenge();
    incoming.Offer(""); incoming.Offer(new string('x', 257));
    Check(!incoming.Pending, "invalid link was queued");
    incoming.Offer("first");
    Check(incoming.Begin(0, out var first, out var oldVersion) && first == "first", "first link was not queued");
    incoming.Offer("first");
    Check(!incoming.Begin(0, out _, out _), "duplicate link started a second request");
    incoming.Offer("second");
    Check(incoming.Begin(0, out var second, out var newVersion) && second == "second", "new link did not replace old link");
    Check(!incoming.Complete(oldVersion, false, 0), "stale response was accepted");
    Check(incoming.Complete(newVersion, true, 0), "retryable response was ignored");
    Check(!incoming.Begin(1, out _, out _), "retry occurred too early");
    Check(incoming.Begin(2, out _, out var retryVersion), "retry backoff failed");
    Check(incoming.Complete(retryVersion, false, 2) && !incoming.Pending, "finished challenge remained pending");
    incoming.Offer("second");
    Check(incoming.Begin(2, out _, out _), "a later invocation of the same link was ignored");
}

static void PlatformLoginCases()
{
    Action<string, string> success = null;
    Action failure = null, timeout = null;
    int exchanges = 0, failures = 0;
    string message = "";
    void Fail(string value) { failures++; message = value; }
    void Start() => PlatformLoginFlow.Start((ok, bad) => { success = ok; failure = bad; },
        deadline => timeout = deadline, (code, anonymous) => exchanges++, Fail);

    Start(); timeout(); success("late", ""); failure(); timeout();
    Check(exchanges == 0 && failures == 1 && message.Contains("超时"), "timeout did not reject late callbacks exactly once");
    exchanges = failures = 0; Start(); success("code", ""); success("duplicate", ""); failure(); timeout();
    Check(exchanges == 1 && failures == 0, "success started multiple exchanges or failed later");
    exchanges = failures = 0; Start(); failure(); success("late", ""); timeout();
    Check(exchanges == 0 && failures == 1 && message.Contains("失败"), "failure did not settle exactly once");
    exchanges = failures = 0; Start(); success(" ", null); timeout();
    Check(exchanges == 0 && failures == 1 && message.Contains("凭证"), "empty platform credential was exchanged");
    exchanges = failures = 0; Start(); success(null, "anonymous"); timeout();
    Check(exchanges == 1 && failures == 0, "anonymous platform credential was rejected");
    failures = 0;
    PlatformLoginFlow.Start((ok, bad) => throw new Exception("SDK error"), deadline => { }, (code, anonymous) => exchanges++, Fail);
    Check(failures == 1 && message.Contains("不可用"), "synchronous SDK exception did not complete login");
    failures = 0;
    PlatformLoginFlow.Start((ok, bad) => { }, deadline => throw new Exception("timer error"), (code, anonymous) => exchanges++, Fail);
    Check(failures == 1 && message.Contains("不可用"), "timeout scheduling error did not complete login");
    failures = 0;
    PlatformLoginFlow.Start((ok, bad) => ok("code", ""), deadline => { }, (code, anonymous) => throw new Exception("request error"), Fail);
    Check(failures == 1 && message.Contains("请求失败"), "exchange startup exception did not complete login");
}

static void IndependentSettlementRetries()
{
    foreach (string error in new[] { "对局凭证无效或已过期", "挑战凭证无效或已过期", "挑战规则已更新，请重新开始", "未检测到操作，本局不发放账号奖励", "回放格式无效", "回放长度无效", "回放内容无效", "成绩与回放不一致" })
    {
        Check(SettlementFailure.IsTerminal(400, error), "known permanent settlement rejection was retained: " + error);
        foreach (int status in new[] { 0, 401, 429, 500, 503 })
            Check(!SettlementFailure.IsTerminal(status, error), "retryable HTTP status discarded a saved run: " + status);
    }
    foreach (string error in new[] { "回放服务暂不可用", "对局时长无效", "挑战时长无效", "凭证无效或已过期，请稍后重试", "", null })
        Check(!SettlementFailure.IsTerminal(400, error), "unknown or potentially transient failure discarded a saved run");
    var schedule = new PendingSettlementSchedule();
    Check(schedule.Due("alice", "old", 0) && schedule.Due("alice", "new", 0), "new runs were delayed");
    schedule.Fail("alice", "old", 0);
    Check(!schedule.Due("alice", "old", 1) && schedule.Due("alice", "new", 1), "failed run blocked a later settlement");
    Check(schedule.Due("bob", "old", 1), "one account delayed a different account");
    schedule.Fail("alice", "new", 1);
    Check(schedule.Due("alice", "old", 2) && !schedule.Due("alice", "new", 2), "separate retry schedules were mixed");
    schedule.Fail("alice", "old", 2);
    Check(!schedule.Due("alice", "old", 5) && schedule.Due("alice", "old", 6), "per-run exponential backoff failed");
    schedule.Remove("alice", "old");
    Check(schedule.Due("alice", "old", 2) && !schedule.Due("alice", "new", 2), "completed run was not independently cleared");
    Check(!schedule.Due("", "run", 0) && !schedule.Due("alice", "", 0), "missing owner or run id was scheduled");
}

static void PendingRetries()
{
    var retry = new PendingRetrySchedule();
    Check(retry.Due(0), "initial settlement was delayed");
    retry.Fail(100);
    Check(!retry.Due(101.99) && retry.Due(102), "first retry timing incorrect");
    retry.Fail(102);
    Check(!retry.Due(105.99) && retry.Due(106), "backoff did not grow");
    double now = 106;
    for (int i = 0; i < 12; i++) { retry.Fail(now); Check(retry.NextAttempt - now <= 60, "retry exceeded one minute"); now = retry.NextAttempt; }
    retry.Fail(now);
    Check(retry.Failures == 6 && retry.NextAttempt - now == 60 && !retry.Due(now + 59), "retry cap incorrect");
    retry.Reset(); Check(retry.Failures == 0 && retry.Due(0), "successful settlement did not reset backoff");
    Check(PendingRetrySchedule.OwnedBy("player-a", "player-a"), "owner was not recognized");
    Check(!PendingRetrySchedule.OwnedBy("player-a", "player-b") && !PendingRetrySchedule.OwnedBy("", "") && !PendingRetrySchedule.OwnedBy(null, "player-a"), "another or missing account could submit the result");
}

static void AudioFeedbackCursorCases()
{
    var cursor = new FeedbackCursor();
    Check(cursor.PendingBaseline && cursor.Observe(0), "first observed state was not baselined");
    Check(!cursor.PendingBaseline && !cursor.Observe(1), "new event after the baseline was suppressed");
    cursor.Suspend(); cursor.Suspend();
    Check(cursor.PendingBaseline && cursor.Observe(200), "resume did not discard background events");
    Check(!cursor.Observe(201), "first new simulation step after resume was suppressed");
    Check(cursor.Observe(0) && !cursor.Observe(1), "new match did not reset event counters");
    Check(StrengthSoundFeedback.Select(true, 0, 1, 1, -1) == StrengthSoundCue.Bite &&
        StrengthSoundFeedback.Select(true, 1, 2, 1, 2) == StrengthSoundCue.Damage, "real strength contacts did not produce distinct cues");
    Check(StrengthSoundFeedback.Select(true, 1, 3, 2, 3) == StrengthSoundCue.Damage &&
        StrengthSoundFeedback.Select(true, 1, 3, 3, 2) == StrengthSoundCue.Bite &&
        StrengthSoundFeedback.Select(true, 1, 3, 3, 3) == StrengthSoundCue.Damage, "latest or simultaneous contact priority changed");
    Check(StrengthSoundFeedback.Select(false, 0, 1, 1, 1) == StrengthSoundCue.None &&
        StrengthSoundFeedback.Select(true, 5, 5, 5, 5) == StrengthSoundCue.None &&
        StrengthSoundFeedback.Select(true, 5, 6, 5, 7) == StrengthSoundCue.None &&
        StrengthSoundFeedback.Select(true, -1, 0, 0, 0) == StrengthSoundCue.None, "stale, future, initial or non-strength events made a sound");
    Check(StrengthSoundFeedback.Select(true, 1, 2, -1, -1, 2) == StrengthSoundCue.Pickup &&
        StrengthSoundFeedback.Select(true, 1, 2, 2, -1, 2) == StrengthSoundCue.Bite &&
        StrengthSoundFeedback.Select(true, 1, 2, 2, 2, 2) == StrengthSoundCue.Damage &&
        StrengthSoundFeedback.Select(true, 1, 3, 2, -1, 3) == StrengthSoundCue.Pickup &&
        StrengthSoundFeedback.Select(true, 2, 3, -1, -1, 2) == StrengthSoundCue.None,
        "pickup cue or simultaneous contact priority is incorrect");
    cursor.Suspend(); Check(cursor.Observe(200), "sound did not rebaseline on resume");
    Check(StrengthSoundFeedback.Select(true, cursor.LastTick, 201, 198, -1) == StrengthSoundCue.None &&
        StrengthSoundFeedback.Select(true, cursor.LastTick, 201, 201, -1) == StrengthSoundCue.Bite,
        "resume replayed an old bite or suppressed a new bite");
    Check(StrengthSoundFeedback.CaptureHaptic(true, 10, 11, 11, "you", "you", 0), "actual strength loop lacked haptic feedback");
    Check(!StrengthSoundFeedback.CaptureHaptic(false, 10, 11, 11, "you", "you", 0) &&
        StrengthSoundFeedback.CaptureHaptic(false, 10, 11, 11, "you", "you", 1), "legacy loop haptic changed");
    Check(!StrengthSoundFeedback.CaptureHaptic(true, 10, 11, 11, "other", "you", 0) &&
        !StrengthSoundFeedback.CaptureHaptic(true, 11, 12, 11, "you", "you", 0) &&
        !StrengthSoundFeedback.CaptureHaptic(true, 10, 11, 12, "you", "you", 0) &&
        !StrengthSoundFeedback.CaptureHaptic(true, -1, 0, 0, "you", "you", 0), "foreign, stale, future or baseline loop vibrated");
}

static void MusicBehavior()
{
    Check(AudioLevels.Next(100) == 75 && AudioLevels.Next(75) == 50 && AudioLevels.Next(50) == 25 && AudioLevels.Next(25) == 0 && AudioLevels.Next(0) == 100, "volume controls did not cycle through their levels");
    Check(AudioLevels.Next(1000) == 75 && AudioLevels.Next(-4) == 100, "invalid stored volume was not clamped");
    Check(!MusicPolicy.ShouldPlay(true, false, true, false, false), "music started before user interaction");
    Check(MusicPolicy.ShouldPlay(true, true, true, false, false), "music did not start after interaction");
    Check(!MusicPolicy.ShouldPlay(false, true, true, false, false), "music ignored its independent setting");
    Check(!MusicPolicy.ShouldPlay(true, true, false, false, false), "music continued in background");
    Check(!MusicPolicy.ShouldPlay(true, true, true, true, false), "music continued while paused");
    Check(!MusicPolicy.ShouldPlay(true, true, true, false, true), "music continued during an ad");
    var fade = new MusicTransition();
    Check(fade.Advance(false, .016f) && fade.Gain == 0, "first music clip did not start at silence");
    Check(!fade.Advance(false, .1f) && Math.Abs(fade.Gain - .5f) < .001f, "first clip did not fade in");
    Check(!fade.Advance(false, .1f) && fade.Gain == 1, "music did not reach its configured level");
    Check(!fade.Advance(true, .1f) && Math.Abs(fade.Gain - .5f) < .001f, "old track did not fade out before the switch");
    Check(!fade.Advance(false, .1f) && fade.Gain == 1, "rapidly reverting a switch did not restore the current track");
    Check(!fade.Advance(true, .1f) && fade.Advance(true, .1f) && fade.Gain == 0, "new track was not switched at silence");
    Check(!fade.Advance(true, float.NaN) && fade.Gain == 0, "invalid delta changed music gain");
    Check(!fade.Advance(true, 1f) && Math.Abs(fade.Gain - .5f) < .001f, "large frame skipped the fade");
    fade.Reset(); Check(fade.Advance(false, 0) && fade.Gain == 0, "failed track recovery retained stale transition state");
    var home = MusicComposer.Create(false); var battle = MusicComposer.Create(true);
    Check(home.Length == MusicComposer.SampleRate * MusicComposer.Seconds && battle.Length == home.Length, "incorrect loop duration");
    Check(home[0] == 0 && Math.Abs(home[^1]) < .01 && battle[0] == 0 && Math.Abs(battle[^1]) < .01, "loop boundary is not faded");
    Check(home.All(x => float.IsFinite(x) && Math.Abs(x) < .4f) && home.Any(x => Math.Abs(x) > .05f), "home loop is clipped or silent");
    Check(battle.All(x => float.IsFinite(x) && Math.Abs(x) < .4f) && battle.Any(x => Math.Abs(x) > .05f), "battle loop is clipped or silent");
    Check(!home.SequenceEqual(battle), "home and battle loops are identical");
}

static void ContactNoticeLayoutCases()
{
    Check(!ContactNoticeLayout.TryGetArea(false, false, out _), "an absent notice reserved screen space");
    Check(ContactNoticeLayout.TryGetArea(true, true, out var damage) && damage.x == 190 && damage.width == 700 &&
        ContactNoticeLayout.TryGetArea(false, true, out var capture) && capture.x == 240 && capture.width == 600,
        "visible notice bounds or combat-over-capture priority changed");
    var world = new PveFixedWorld(42, false, null, true);
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
    fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
    fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-11700, 0, 32768, 16));
    for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
    ((List<PveFixedFruit>)world.Fruit).Clear(); world.Step(0, false);
    string notice = StrengthContactNotice.Text(world, world.Tick, out _);
    Check(!string.IsNullOrEmpty(notice) && ContactNoticeLayout.TryGetArea(true, false, out damage),
        "real combat did not expose the notice area");
    var anchor = new Point(540, 660); var names = new NameplateLayout();
    names.BeginFrame(world.Tick); names.AddAnchor(anchor);
    Check(names.TryPlace("you", anchor, out var unreserved) && unreserved.y < damage.y + damage.height &&
        unreserved.y + NameplateLayout.Height > damage.y, "regression fixture did not reproduce the overlap");
    names.BeginFrame(world.Tick + 1); names.AddAnchor(anchor);
    names.Reserve(damage.x, damage.y, damage.width, damage.height);
    Check(names.TryPlace("you", anchor, out var moved) && moved.y + NameplateLayout.Height <= damage.y,
        "a remembered nameplate covered the active combat notice");
    var edge = new Point(540, 510);
    Check(EdgeIndicatorLayout.TryPlace(edge, null, out var original) && original.y + 54 > damage.y,
        "edge-marker fixture did not reproduce the overlap");
    Check(EdgeIndicatorLayout.TryPlace(edge, new[] { damage }, out var safe) &&
        (safe.x + EdgeIndicatorLayout.DefaultWidth <= damage.x || safe.x >= damage.x + damage.width ||
         safe.y + 54 <= damage.y || safe.y >= damage.y + damage.height),
        "edge marker covered the active combat notice");
    Check(StrengthContactNotice.Text(world, world.Tick + 20, out _) == "", "combat notice did not expire");
    names.BeginFrame(world.Tick + 30); names.AddAnchor(anchor);
    Check(names.TryPlace("you", anchor, out var restored) && restored.y == unreserved.y,
        "nameplate failed to return after the notice disappeared");
}

static void NameplateCases()
{
    var layout = new NameplateLayout();
    // The opponent's anchor may be below the player's sprite top while its
    // otherwise valid label falls across the player's actual illustration.
    var playerTop = new Point(538, 610); var crowdedOpponent = new Point(600, 816);
    layout.BeginFrame(1); layout.AddAnchor(playerTop); layout.AddAnchor(crowdedOpponent);
    Check(layout.TryPlace("you", playerTop, out var baselineOwn) && baselineOwn.y == 542 &&
        layout.TryPlace("bot", crowdedOpponent, out var baselineBot) && baselineBot.y == 748,
        "natural-overlap fixture did not place the bot over the player illustration");
    layout.ClearHistory(); layout.BeginFrame(1); layout.AddAnchor(playerTop); layout.AddAnchor(crowdedOpponent);
    layout.Reserve(456, 610, 164, 276);
    Check(layout.TryPlace("you", playerTop, out var protectedOwn) && protectedOwn.y == 542,
        "protecting the player illustration moved its own priority nameplate");
    bool placedBot = layout.TryPlace("bot", crowdedOpponent, out var protectedBot);
    Check(!placedBot || protectedBot.y + NameplateLayout.Height + 6 <= 610 || protectedBot.y >= 886 + 6 ||
        protectedBot.x + NameplateLayout.Width + 6 <= 456 || protectedBot.x >= 620 + 6,
        "an opponent nameplate covered the player illustration");
    layout.BeginFrame(2); layout.AddAnchor(playerTop); layout.AddAnchor(crowdedOpponent);
    layout.Reserve(30, 400, 1020, 1050);
    Check(!layout.TryPlace("bot", crowdedOpponent, out _), "a crowded layout forced a label onto a protected illustration");
    layout.BeginFrame(50); layout.AddAnchor(playerTop); layout.AddAnchor(crowdedOpponent);
    Check(layout.TryPlace("you", playerTop, out _) && layout.TryPlace("bot", crowdedOpponent, out var returnedBot) && returnedBot.y == 748,
        "opponent nameplate did not return after the player illustration moved away");
    layout.ClearHistory();
    var center = new Point(540, 800);
    for (int i = 0; i < 8; i++) layout.AddAnchor(center);
    var boxes = new List<Point>();
    for (int i = 0; i < 12; i++)
    {
        if (!layout.TryPlace(center, out var box)) continue;
        if (boxes.Count == 0) Check(box.x == 435 && box.y == 732, "priority player did not retain the natural label position");
        Check(box.y + NameplateLayout.Height <= center.y - 12, "nameplate moved below the avatar anchor");
        Check(!(center.x >= box.x - 8 && center.x <= box.x + NameplateLayout.Width + 8 &&
            center.y >= box.y - 8 && center.y <= box.y + NameplateLayout.Height + 8), "nameplate covered an actor anchor");
        foreach (var prior in boxes)
            Check(box.x >= prior.x + NameplateLayout.Width + 6 || box.x + NameplateLayout.Width + 6 <= prior.x ||
                  box.y >= prior.y + NameplateLayout.Height + 6 || box.y + NameplateLayout.Height + 6 <= prior.y, "overlapping actors produced overlapping labels");
        boxes.Add(box);
    }
    Check(boxes.Count >= 4 && boxes.Count < 12, "crowded area did not bound readable labels");
    layout.Reset(); layout.AddAnchor(new Point(960, 430));
    Check(!layout.TryPlace(new Point(960, 430), out _), "upper-right label covered the minimap or the avatar");
    layout.Reset(); layout.AddAnchor(new Point(960, 750));
    Check(layout.TryPlace(new Point(960, 750), out var besideMap) && besideMap.y >= 545 && besideMap.x + NameplateLayout.Width <= 1050,
        "label below the minimap was not placed safely");
    Check(!layout.TryPlace(new Point(float.NaN, 500), out _) && !layout.TryPlace(new Point(30, 500), out _), "invalid or off-screen label was placed");
    layout.Reset(); layout.AddAnchor(center); layout.Reserve(400, 700, 280, 100);
    Check(layout.TryPlace(center, out var away) && away.y + NameplateLayout.Height + 6 <= 700,
        "label covered the tutorial panel");
    layout.Reset(); layout.AddAnchor(center);
    Check(layout.TryPlace(center, out var first) && first.y == 732, "layout reset retained old slots");

    // Moving across the overlap threshold must not make the same label jump
    // between rows every frame; a genuinely unsafe slot still moves immediately.
    layout.ClearHistory();
    Point PlacePair(int tick, float opponentX)
    {
        var own = new Point(400, 800); var opponent = new Point(opponentX, 800);
        layout.BeginFrame(tick); layout.AddAnchor(own); layout.AddAnchor(opponent);
        Check(layout.TryPlace("you", own, out var ownBox) && ownBox.y == 732, "player lost priority");
        Check(layout.TryPlace("opponent", opponent, out var otherBox), "opponent label unexpectedly hidden");
        return otherBox;
    }
    Check(PlacePair(1, 615).y == 676, "initial overlap was not resolved");
    for (int t = 2; t <= 25; t++)
        Check(PlacePair(t, t % 2 == 0 ? 617 : 615).y == 676, "label flickered across the overlap threshold");
    Check(PlacePair(26, 645).y == 732, "label did not return after sustained clearance");
    Check(PlacePair(27, 615).y == 676, "new overlap was not resolved immediately");
    layout.BeginFrame(28); layout.AddAnchor(new Point(615, 800));
    layout.Reserve(400, 620, 500, 130);
    Check(!layout.TryPlace("opponent", new Point(615, 800), out var blocked) || blocked.y + NameplateLayout.Height + 6 <= 620,
        "remembered label remained on a new reserved panel");
    layout.ClearHistory();
    Check(PlacePair(1, 645).y == 732, "new match retained an old label slot");
    layout.ClearHistory(); Check(PlacePair(100, 615).y == 676, "recovery fixture did not overlap");
    layout.BeginFrame(141); // Opponent absent for more than two seconds.
    Check(PlacePair(142, 617).y == 732, "absent actor retained a stale slot");
    layout.ClearHistory(); Check(PlacePair(100, 615).y == 676, "rollback fixture did not overlap");
    Check(PlacePair(0, 617).y == 732, "rewound simulation retained a stale slot");
}

static void NearbyDanger()
{
    var state = new ArenaState { phase = "running" };
    var player = new SnakeState { id = "you", segments = new List<Point> { new(0, 0), new(-1, 0), new(-2, 0) } };
    var other = new SnakeState { id = "other", segments = new List<Point> { new(30, 0), new(29, 0), new(3, 0), new(25, 0) } };
    state.snakes.Add(player); state.snakes.Add(other);
    Check(DangerProbe.Find(state, "you", out var cue) && cue.x == 3, "near body was not indicated");
    other.shield = 1;
    Check(!DangerProbe.Find(state, "you", out _), "protected opponent incorrectly indicated danger");
    other.shield = 0; player.shield = 1;
    Check(!DangerProbe.Find(state, "you", out _), "protected player incorrectly received warning");
    Check(DangerProbe.FindBeforeProtectionEnds(state, "you", out cue) && cue.x == 3, "near threat was not signalled before protection expiry");
    player.shield = 1.01f;
    Check(!DangerProbe.FindBeforeProtectionEnds(state, "you", out _), "warning appeared before the final second");
    player.shield = .5f; other.shield = 1.01f;
    Check(!DangerProbe.FindBeforeProtectionEnds(state, "you", out _), "long-protected opponent was treated as an imminent hazard");
    other.shield = .5f;
    Check(DangerProbe.FindBeforeProtectionEnds(state, "you", out cue) && cue.x == 3, "both expiring protections hid a nearby threat");
    other.alive = false;
    Check(!DangerProbe.FindBeforeProtectionEnds(state, "you", out _), "dead opponent triggered expiry warning");
    other.alive = true; other.shield = 0; player.shield = 0;
    Check(!DangerProbe.FindBeforeProtectionEnds(state, "you", out _) && DangerProbe.Find(state, "you", out _), "expiry warning did not transition to real danger");
    player.shield = 0; other.alive = false;
    Check(!DangerProbe.Find(state, "you", out _), "dead opponent incorrectly indicated danger");
    other.alive = true; other.segments[2] = new Point(30, 0); other.segments[0] = new Point(3, 0);
    Check(DangerProbe.Find(state, "you", out cue) && cue.x == 3, "dangerous opposing head was missed");
    other.segments.RemoveAt(3);
    other.segments.RemoveAt(2);
    Check(!DangerProbe.Find(state, "you", out _), "shorter opposing head incorrectly indicated danger");
    player.shield = .5f;
    Check(!DangerProbe.FindBeforeProtectionEnds(state, "you", out _), "shorter opposing head produced a false expiry warning");
    player.shield = 0;
    other.segments[0] = new Point(30, 0); other.segments[1] = new Point(1, 0);
    Check(!DangerProbe.Find(state, "you", out _), "non-colliding neck incorrectly indicated danger");
    state.phase = "ended"; other.segments.Add(new Point(1, 0));
    Check(!DangerProbe.Find(state, "you", out _), "ended match retained danger warning");
}

static void MinimapOcclusionCases()
{
    var map = new MinimapOcclusion();
    map.Advance(0, false, .1f, false); Check(map.Visibility == 1f, "clear minimap unexpectedly faded");
    map.Advance(1, true, .1f, false); map.Advance(2, true, .1f, false);
    Check(Math.Abs(map.Visibility - .2f) < .001f, "overlapping character did not reveal through minimap");
    map.Advance(3, false, .1f, false); Check(Math.Abs(map.Visibility - .2f) < .001f, "minimap flickered at overlap boundary");
    map.Advance(12, false, .1f, true); Check(Math.Abs(map.Visibility - .2f) < .001f, "suspended minimap changed");
    map.Advance(12, false, .1f, false); Check(map.Visibility > .2f && map.Visibility < 1f, "minimap did not fade back gradually");
    map.Advance(0, false, .1f, false); Check(map.Visibility == 1f, "old match occlusion leaked into new match");
    map.Advance(1, true, float.NaN, false); Check(map.Visibility == 1f, "invalid delta corrupted visibility");
    map.Advance(2, true, .01f, false, true); Check(Math.Abs(map.Visibility - .2f) < .001f, "reduced mode animated instead of switching");
    map.Advance(12, false, .01f, false, true); Check(map.Visibility == 1f, "reduced mode did not restore the clear map");
}

static void ArenaVisualClockCases()
{
    var clock = new ArenaVisualClock();
    clock.Advance(.5f, false);
    Check(Math.Abs(clock.Time - .5f) < .001f, "visual clock did not advance");
    clock.Advance(10, true);
    Check(Math.Abs(clock.Time - .5f) < .001f, "visual clock advanced while suspended");
    clock.Advance(float.NaN, false); clock.Advance(float.PositiveInfinity, false); clock.Advance(-1, false);
    Check(Math.Abs(clock.Time - .5f) < .001f, "invalid delta changed visual time");
    clock.Advance(.25f, false);
    Check(Math.Abs(clock.Time - .75f) < .001f, "visual clock did not resume");
}

static void EliminationFeedbackCases()
{
    var feed = new EliminationFeedback();
    var you = new SnakeState { id = "you", name = "玩家" };
    var other = new SnakeState { id = "bot", name = "AI 1", bot = true };
    var state = new ArenaState { phase = "running", tick = 1, snakes = new List<SnakeState> { you, other } };
    feed.Observe(state, "you");
    Check(feed.Current(state) == "", "invented an elimination");
    other.alive = false; other.eliminatedBy = "you"; other.eliminationReason = "capture"; state.tick = 2;
    feed.Observe(state, "you");
    Check(feed.Current(state) == "收圈捕获 · AI 1", "actual capture was not attributed");
    state.tick = 43; feed.Observe(state, "you");
    Check(feed.Current(state) == "", "old notification did not expire");
    feed.Reset(); feed.Observe(state, "you");
    Check(feed.Current(state) == "", "previously dead opponent generated a new event");
    other.alive = true; state.tick++; feed.Observe(state, "you");
    other.alive = false; you.alive = false; state.tick++; feed.Observe(state, "you");
    Check(feed.Current(state) == "", "dead player was credited with a simultaneous elimination");
    feed.Reset(); you.alive = true; other.alive = true;
    var second = new SnakeState { id = "bot-2", name = "AI 2", bot = true };
    state.snakes.Add(second); state.tick++; feed.Observe(state, "you");
    other.alive = false; other.eliminatedBy = "you"; other.eliminationReason = "capture";
    second.alive = false; second.eliminatedBy = "you"; second.eliminationReason = "capture"; state.tick++;
    feed.Observe(state, "you");
    Check(feed.Current(state) == "收圈捕获 · 2 个对手", "simultaneous captures did not report the real count");
    feed.Reset(); other.alive = second.alive = true; state.tick++; feed.Observe(state, "you");
    other.alive = second.alive = false; second.eliminationReason = "trail"; state.tick++; feed.Observe(state, "you");
    Check(feed.Current(state) == "淘汰 · 2 个对手", "mixed elimination causes were incorrectly grouped as one action");
    feed.Reset(); other.alive = true; state.tick++; feed.Observe(state, "you");
    other.alive = false; other.eliminationReason = "strength_head"; state.tick++; feed.Observe(state, "you");
    Check(feed.Current(state) == "正面胜出 · AI 1", "strength head elimination omitted from feedback");
}

static void KillFeedbackCases()
{
    var feedback = new KillFeedback();
    var snake = new SnakeState { id = "you", segments = Enumerable.Repeat(new Point(), 16).ToList() };
    var world = new ArenaState { phase = "running", tick = 1, snakes = new List<SnakeState> { snake } };
    feedback.Observe(world, "you"); Check(feedback.Current(world) == "", "initial state invented a kill");
    snake.kills++; snake.segments.Add(new Point()); world.tick++;
    feedback.Observe(world, "you"); Check(feedback.Current(world).Contains("变长"), "real growth was not indicated");
    feedback.Suspend(); feedback.Observe(world, "you");
    Check(feedback.Current(world).Contains("变长"), "suspending at the same tick lost the current cue");
    world.tick += 22; Check(feedback.Current(world) == "", "kill cue did not expire by simulation time");
    while (snake.segments.Count < Arena.MaximumLength) snake.segments.Add(new Point());
    world.tick++; feedback.Observe(world, "you");
    snake.kills++; world.tick++; feedback.Observe(world, "you");
    Check(feedback.Current(world) == "围猎成功！", "maximum length incorrectly claimed growth");
    feedback.Suspend(); snake.kills++; world.tick++; feedback.Observe(world, "you");
    Check(feedback.Current(world) == "围猎成功！", "resuming replayed unseen background events");
    feedback.Reset(); feedback.Observe(world, "you");
    Check(feedback.Current(world) == "", "previous match cue leaked into a new match");
    snake.kills++; world.tick++; feedback.Observe(world, "you", true);
    Check(feedback.Current(world) == "正面胜出！", "strength head victory was described as capture");
    feedback.Reset(); snake.segments.RemoveAt(snake.segments.Count - 1); feedback.Observe(world, "you", true);
    snake.kills++; snake.segments.Add(new Point()); world.tick++; feedback.Observe(world, "you", true);
    Check(feedback.Current(world) == "正面胜出  ·  变长！", "strength head growth cue missing");
}

static void RoundTimerCases()
{
    Check(!RoundTimerPresentation.IsUrgent(null), "missing match appeared urgent");
    var state = new ArenaState { phase = "waiting", remaining = 9 };
    Check(!RoundTimerPresentation.IsUrgent(state), "waiting match appeared urgent");
    state.phase = "running"; state.remaining = 10.1f;
    Check(!RoundTimerPresentation.IsUrgent(state), "urgency started too early");
    state.remaining = 10;
    Check(RoundTimerPresentation.IsUrgent(state), "final ten seconds not highlighted");
    state.remaining = .1f;
    Check(RoundTimerPresentation.IsUrgent(state), "last fraction was not highlighted");
    state.remaining = 0;
    Check(!RoundTimerPresentation.IsUrgent(state), "zero time remained urgent");
    state.phase = "ended"; state.remaining = 5;
    Check(!RoundTimerPresentation.IsUrgent(state), "ended match appeared urgent");
}

static void FirstRunHintsCases()
{
    Check(FirstRunHints.Select(39, false, false, false, 0) == "", "hint appeared before two seconds");
    Check(FirstRunHints.Select(40, false, false, false, 0).Contains("拖动"), "movement hint was missing");
    Check(FirstRunHints.Select(400, false, false, false, 0).Contains("拖动"), "timer advanced the tutorial without steering");
    Check(FirstRunHints.Select(80, true, false, false, 0) == "", "movement hint did not disappear after steering");
    Check(FirstRunHints.Select(100, true, false, false, 0).Contains("第二根手指"), "boost hint missing");
    Check(FirstRunHints.Select(100, true, false, false, 0, false, true) == "", "boost hint remained after actual boost");
    Check(FirstRunHints.Select(100, true, false, false, 0, true).Contains("闭合"), "nearby loop did not take priority");
    Check(FirstRunHints.Select(160, true, false, false, 0).Contains("轨迹"), "boost hint outlasted its window");
    Check(FirstRunHints.Select(180, true, false, false, 0).Contains("轨迹"), "loop hint missing");
    Check(FirstRunHints.Select(180, true, true, false, 0) == "", "loop hint ignored an actual loop");
    Check(FirstRunHints.Select(340, true, false, false, 0).Contains("撞墙"), "rebound hint missing");
    Check(FirstRunHints.Select(340, true, false, true, 0) == "", "rebound hint ignored an actual rebound");
    Check(FirstRunHints.Select(500, true, false, false, 0).Contains("淘汰"), "elimination hint missing");
    Check(FirstRunHints.Select(500, true, false, false, 1) == "", "elimination hint ignored an actual elimination");
    Check(FirstRunHints.Select(600, false, false, false, 0) == "", "hint continued after the tutorial window");
    var snake = new SnakeState { angle = 0, segments = new List<Point> {
        new Point(0, 0), new Point(-1, 0), new Point(-2, 0), new Point(-3, 0), new Point(-4, 0),
        new Point(1, -1), new Point(1, 1) } };
    Check(FirstRunHints.ApproachingOwnTrail(snake), "approaching a real body segment was not detected");
    Check(FirstRunHints.Select(450, true, false, false, 0, true).Contains("闭合"), "contextual loop hint missing");
    Check(!FirstRunHints.Select(450, true, true, false, 0, true).Contains("闭合"), "loop hint remained after capture");
    snake.angle = (float)Math.PI;
    Check(!FirstRunHints.ApproachingOwnTrail(snake), "body behind the head triggered a hint");
    snake.angle = 0; snake.shield = 1;
    Check(!FirstRunHints.ApproachingOwnTrail(snake), "protected snake was told it could capture");
    snake.shield = 0; snake.alive = false;
    Check(!FirstRunHints.ApproachingOwnTrail(snake), "dead snake triggered a hint");
}

static void FullScreenDrag()
{
    foreach (var origin in new[] { new Point(0, 0), new Point(1079, 0), new Point(0, 1919), new Point(1079, 1919), new Point(540, 960) })
    {
        var drag = new DragSteering(12);
        Check(drag.Begin(3, origin), "first finger did not start");
        Check(!drag.Begin(8, origin), "another finger stole control");
        Check(!drag.Move(3, origin + new Point(5, 0), out _), "jitter moved the player");
        Check(!drag.Move(8, origin + new Point(40, 0), out _), "other finger changed direction");
        Check(drag.Move(3, origin + new Point(30, 0), out var right) && right.x > .99f, "right drag failed");
        Check(drag.Move(3, origin + new Point(30, -30), out var up) && up.y > .99f, "continuous upward drag failed");
        Check(!drag.End(8) && drag.Active, "other finger ended the drag");
        Check(drag.End(3) && !drag.Active, "drag release was not reported");
        Check(drag.Begin(8, origin) && !drag.End(8), "a tap was mistaken for a drag");
    }
}
static void TouchRecovery()
{
    Check(!DragSteering.SuppressGuiPointer(false, false, 1) && !DragSteering.SuppressGuiPointer(true, false, 0),
        "single finger button tap was consumed");
    Check(DragSteering.SuppressGuiPointer(false, false, 2) && DragSteering.SuppressGuiPointer(true, false, 1),
        "secondary finger button events were not consumed");
    Check(DragSteering.SuppressGuiPointer(true, true, 0) && !DragSteering.SuppressGuiPointer(true, false, 0),
        "desktop drag release and click were not distinguished");
    var drag = new DragSteering(12);
    drag.Begin(3, new Point(100, 100));
    drag.ReconcileTouches(new[] { 3, 7 });
    Check(drag.IsTracking(3), "second finger stole the steering pointer");
    Check(drag.Move(3, new Point(130, 100), out var right) && right.x > .99f, "steering stopped while both fingers were present");
    drag.ReconcileTouches(new[] { 7 }); // Host omitted finger 3's end event.
    Check(!drag.Active && drag.Begin(7, new Point(200, 200)), "remaining finger could not take over");
    Check(drag.Move(7, new Point(200, 170), out var up) && up.y > .99f, "new steering finger could not move");
    drag.ReconcileTouches(Array.Empty<int>());
    Check(!drag.Active && drag.Begin(-1, new Point(0, 0)), "missing last touch blocked mouse control");
    drag.ReconcileTouches(Array.Empty<int>());
    Check(drag.IsTracking(-1), "normal mouse drag was cancelled without touches");
    drag.ReconcileTouches(new[] { 9 });
    Check(!drag.Active && drag.Begin(9, new Point(0, 0)), "real touch could not replace mouse emulation");
    Check(!drag.End(3) && drag.IsTracking(9), "late end from old finger interrupted control");
    drag.Reset(); Check(!drag.Active && drag.Begin(4, new Point(0, 0)), "suspension reset prevented a new gesture");
}

static void CaveAlbum()
{
    var profile = new PlayerProgress { caveFragments = new[] { 1, 3, 7 } };
    for (int cave = 0; cave < 3; cave++) for (int chapter = 0; chapter < 3; chapter++)
    {
        int count = profile.caveFragments[cave];
        string entry = Progression.CaveStory(cave, chapter, count);
        bool unlocked = count >= Progression.CaveStoryThreshold(chapter);
        Check(!string.IsNullOrEmpty(entry) && entry.Contains("收集") != unlocked, "story unlock does not match fragments");
    }
    Check(profile.caveFragments.SequenceEqual(new[] { 1, 3, 7 }), "viewing stories spent fragments");
    Check(Progression.CaveStory(-1, 0, 7) == "" && Progression.CaveStory(0, 3, 7) == "", "invalid story indexes were accepted");
}
static void Near(double actual, double expected, double epsilon, string message)
{
    Check(Math.Abs(actual - expected) <= epsilon, $"{message}: expected {expected}, got {actual}");
}
static void GrowthCurve()
{
    for (int day = 0; day <= 7; day++)
    {
        Check(Progression.Level(day * 100) == day + 1, $"day {day} level incorrect");
        Check(Progression.ExperienceForLevel(day + 1) == day * 100, $"day {day} threshold incorrect");
    }
    Check(Progression.Level(1400) < 15 && Progression.Level(1400) > 8, "post-week growth did not slow");
    int previous = 0;
    for (int level = 2; level <= 25; level++)
    {
        int xp = Progression.ExperienceForLevel(level);
        Check(xp > previous && Progression.Level(xp) == level && Progression.Level(xp - 1) < level, $"invalid threshold for level {level}");
        previous = xp;
    }
}
static void TalentBudget()
{
    var p = new PlayerProgress();
    Check(Progression.AvailablePoints(p) == 0 && !Progression.Allocate(p, 0), "new user received a premature point");
    p.tutorialComplete = true;
    Check(Progression.Allocate(p, 0) && !Progression.Allocate(p, 0), "initial point should be spent only once");
    p.experience = 2000;
    Check(Progression.Allocate(p, 0) && Progression.Allocate(p, 0) && !Progression.Allocate(p, 0), "node rank cap not enforced");
    p.talents = new[] { 3, 3, 3, 3, 3, 3, 3, 3, 3 };
    p.selectedSkin = 0;
    var pve = Progression.Bonuses(p); var pvp = Progression.Bonuses(p, true);
    double Sum(CombatBonuses b) => b.turn + b.boostSaving + b.pickup + b.nearWallTurn + b.shortBoostSaving + b.reboundTurn;
    Near(Sum(pve), .07, .00001, "PvE attribute cap including skin");
    Near(Sum(pvp), .04, .00001, "PvP attribute cap including skin");
    p.selectedSkin = 3;
    Check(Progression.CaveBonusPercent(p) == 7 && Progression.TaskBonusPercent(p) == 3, "exploration bonuses incorrect");
}
static void TalentRecommendation()
{
    var profile = new PlayerProgress { tutorialComplete = true, experience = 700 };
    Check(Progression.AllocateRecommended(profile) == 8, "recommended setup did not use eight available points");
    Check(profile.talents.SequenceEqual(new[] { 1, 1, 1, 1, 1, 0, 1, 1, 1 }), "recommendation order or ranks changed");
    Check(Progression.AvailablePoints(profile) == 0 && Progression.AllocateRecommended(profile) == 0, "recommendation spent unavailable points");
}

static void CaveSyncCases()
{
    var sync = new CaveSyncState();
    Check(!sync.Ready, "new cave state unexpectedly ready");
    Check(sync.TryBegin(0, out var first) && sync.InFlight, "initial cave refresh did not start");
    sync.Invalidate();
    Check(!sync.TryBegin(0, out _), "overlapping refresh started during an in-flight request");
    Check(!sync.Complete(first, true, 0) && !sync.Ready && !sync.InFlight, "stale foreground response was accepted");
    Check(sync.TryBegin(0, out var second), "new refresh did not start");
    Check(!sync.Complete(first, true, 0) && sync.InFlight, "duplicate stale response interrupted a newer request");
    Check(sync.Complete(second, false, 0) && !sync.Ready, "failed refresh became ready");
    Check(!sync.TryBegin(1, out _), "failed refresh retried too early");
    Check(sync.TryBegin(2, out var third), "failed refresh did not retry");
    Check(sync.Complete(third, true, 2) && sync.Ready && !sync.TryBegin(3, out _), "successful refresh did not become ready");
    sync.Invalidate();
    Check(!sync.Ready, "foreground return did not invalidate refresh");
    Check(sync.TryBegin(3, out var fourth) && sync.Complete(fourth, true, 3), "returning to foreground did not require a new refresh");
    Check(!sync.Complete(fourth, false, 3) && sync.Ready, "duplicate callback changed the refresh state");
}

static void ExpeditionRewards()
{
    Check(Progression.ExpeditionAvailableExperience(480, 18) == 2, "remaining daily experience cap was not applied");
    Check(Progression.ExpeditionAvailableExperience(480, 99) == 0, "over-cap old data produced negative experience");
    Check(Progression.ExpeditionRemainingExperience(int.MinValue) == 20 && Progression.ExpeditionRemainingExperience(int.MaxValue) == 0, "invalid experience totals overflowed the cap");
    Check(Progression.ExpeditionCoins(120, 0) == 60 && Progression.ExpeditionExperience(120) == 5, "2-hour rewards incorrect");
    Check(Progression.ExpeditionCoins(480, 0) == 240 && Progression.ExpeditionExperience(480) == 20, "8-hour rewards incorrect");
    Check(Progression.ExpeditionCoins(720, 7) == 256 && Progression.ExpeditionExperience(720) == 20, "8-hour ceiling or bonus incorrect");
    Check(Progression.ExpeditionCoins(-10, 0) == 0 && Progression.ExpeditionExperience(-10) == 0, "negative time must pay nothing");
}
static void BouncePulse()
{
    var arena = new Arena(23, fruitCount: 0);
    var player = arena.AddSnake("you", "You", false);
    var other = arena.AddSnake("other", "Other", false);
    void Place(SnakeState s, Point head)
    {
        s.angle = 0; s.shield = 0; s.segments.Clear();
        for (int i = 0; i < Arena.StartingLength; i++) s.segments.Add(head - new Point(i * Arena.SegmentSpacing, 0));
        arena.SetInput(s.id, new Point(1, 0), false);
    }
    Place(player, new Point(109.2f, 0)); Place(other, new Point(-80, -80)); arena.Start();
    arena.Tick(.05f);
    Check(player.alive && player.rebound > .79f && player.segments[0].x <= 110 - Arena.CollisionRadius, "wall did not reflect with pulse");
    var before = player.segments[0]; arena.Tick(.05f);
    Near(Point.Distance(before, player.segments[0]), .345, .001, "bounce pulse speed");
    for (int i = 0; i < 20; i++) arena.Tick(.05f);
    Check(player.rebound == 0, "pulse did not expire");
    Place(player, new Point(109.2f, 20)); arena.Tick(.05f);
    Check(player.alive && player.rebound == 0, "bounce cooldown failed");
}
static void DailyReplay()
{
    foreach (int seed in new[] { 0, 1, -1, int.MinValue, int.MaxValue })
    {
        var a = new DailyMatch(seed); var b = new DailyMatch(seed);
        int peak = DailyMatch.CaptureStartingLength;
        while (!a.Finished && !b.Finished)
        {
            byte direction = (byte)((a.State.tick / 75 * 23 + seed) & 255);
            bool boost = a.State.tick % 140 >= 110;
            a.Step(direction, boost); b.Step(direction, boost);
            var x = a.State.snakes[0]; var y = b.State.snakes[0];
            peak = Math.Max(peak, x.segments.Count);
            Check(x.alive == y.alive && x.kills == y.kills && x.starPoints == y.starPoints && x.segments.Count == y.segments.Count, "replay state differs");
            Check(BitConverter.SingleToInt32Bits(x.segments[0].x) == BitConverter.SingleToInt32Bits(y.segments[0].x) && BitConverter.SingleToInt32Bits(x.segments[0].y) == BitConverter.SingleToInt32Bits(y.segments[0].y), "replay position differs");
            Check(a.Score == b.Score, "replay score differs");
        }
        var me = a.State.snakes[0];
        int expected = 10 * (peak - DailyMatch.CaptureStartingLength) + 100 * me.kills + a.State.tick / 20 + 30 * me.starPoints;
        Check(a.Finished && b.Finished && a.Score == expected, "daily score formula or completion incorrect");
        Check(a.State.tick <= DailyMatch.MaxTicks && a.Score >= 0 && a.Score <= DailyMatch.CaptureMaxScore, "daily bounds exceeded");
    }
}
static void DailyFruitProjection()
{
    var match = new DailyMatch(17);
    var first = match.State.fruits[0];
    var all = match.State.fruits.ToArray();
    match.Step(42, false);
    Check(match.State.fruits.Count == Arena.FruitCount && ReferenceEquals(first, match.State.fruits[0]), "fruit projection recreated its objects");
    Check(all.Zip(match.State.fruits).All(pair => ReferenceEquals(pair.First, pair.Second)), "fruit object ordering changed");
    var type = typeof(DailyMatch); var pointType = type.GetNestedType("SimPoint", BindingFlags.NonPublic);
    var fruit = (IList)type.GetField("fruit", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
    var colors = (IList)type.GetField("fruitColor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
    fruit[0] = Activator.CreateInstance(pointType, new object[] { 1234, -5678 }); colors[0] = 3;
    type.GetMethod("Sync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(match, null);
    Check(ReferenceEquals(first, match.State.fruits[0]) && Math.Abs(first.position.x - 1.234f) < .0001f &&
        Math.Abs(first.position.y + 5.678f) < .0001f && first.color == 3, "reused projection did not update its visible values");
}

static void FriendComparisonCases()
{
    Check(FriendChallengeComparison.Format(120, 100).Contains("领先 20 分"), "leading score comparison incorrect");
    Check(FriendChallengeComparison.Format(70, 100).Contains("落后 30 分"), "trailing score comparison incorrect");
    Check(FriendChallengeComparison.Format(0, 0).Contains("成绩持平"), "tie comparison incorrect");
}

static void DailySeeds()
{
    var a = new DailyMatch(12345); var b = new DailyMatch(12346);
    Check(a.State.snakes.Count == 8 && a.State.fruits.Count == 200, "daily setup incorrect");
    Check(Point.Distance(a.State.snakes[0].segments[0], b.State.snakes[0].segments[0]) > .01f, "different seeds produced identical spawn");
    Check(a.State.snakes[0].segments.Count == 16 && !a.State.snakes[0].bot && a.State.snakes.Skip(1).All(s => s.bot), "daily player/AI setup incorrect");
    for (int seed = 1; seed <= 80; seed++)
    {
        var match = new DailyMatch(seed);
        var me = match.State.snakes[0].segments[0];
        float nearest = match.State.snakes.Skip(1).Min(s => Point.Distance(me, s.segments[0]));
        Check(nearest >= 8f && nearest <= 11.1f, "new daily spawn must show a nearby, non-overlapping opponent");
    }
}

static void LegacyDailyReplay()
{
    var match = new DailyMatch(1, DailyMatch.LegacyRulesVersion);
    while (!match.Finished)
    {
        int tick = match.State.tick;
        match.Step((byte)((tick / 100 * 29) & 255), tick % 173 >= 140);
    }
    Check(match.State.tick == 1200 && match.Score == 90, "daily-1 replay changed from its recorded result");
    Check(match.State.eventKind == "", "legacy replay unexpectedly has a new event");
}

static void PreviousDailyReplay()
{
    var match = new DailyMatch(1, DailyMatch.PreviousRulesVersion);
    while (!match.Finished)
    {
        int tick = match.State.tick;
        match.Step((byte)((tick / 100 * 29) & 255), tick % 173 >= 140);
    }
    Check(match.State.tick == 1200 && match.Score == 90, "daily-2 replay changed from its recorded result");
    Check(match.State.captureTick == -1, "daily-2 unexpectedly enabled capture");
    Check(DailyMatch.MaxScoreFor(DailyMatch.PreviousRulesVersion) == 1540 && DailyMatch.MaxScoreFor(DailyMatch.RulesVersion) == 1460, "score bounds not versioned");
}

static void ResultMotionCases()
{
    var world = new ArenaState { phase = "running", winner = "you" };
    var player = new SnakeState { id = "you", alive = true }; world.snakes.Add(player);
    Check(!MatchOutcome.IsVictory(world, "you"), "ongoing match was called a victory");
    world.phase = "ended"; Check(MatchOutcome.IsVictory(world, "you"), "actual winner not recognized");
    world.remaining = 0;
    for (int i = 0; i < 10; i++) player.segments.Add(new Point());
    Check(StrengthMatchOutcome.Explain(world, "you").Contains("位居第一"), "timed strength victory was not explained");
    var winner = new SnakeState { id = "other", alive = true }; world.snakes.Add(winner);
    for (int i = 0; i < 12; i++) winner.segments.Add(new Point());
    world.winner = "other";
    Check(StrengthMatchOutcome.Explain(world, "you").Contains("你的长度 10，第一名 12"), "survival without victory lacked the actual lengths");
    winner.segments.RemoveRange(10, 2); winner.kills = 2;
    Check(StrengthMatchOutcome.Explain(world, "you").Contains("对手淘汰更多"), "equal-length tiebreak was not explained");
    world.winner = "";
    var leader = new SnakeState { id = "leader", alive = true, kills = 2 }; world.snakes.Add(leader);
    for (int i = 0; i < 10; i++) leader.segments.Add(new Point());
    Check(!StrengthMatchOutcome.IsVictory(world, "you") && StrengthMatchOutcome.Explain(world, "you").Contains("第 3 名，第一名并列"), "non-leader was incorrectly treated as tied first");
    player.kills = 2;
    Check(StrengthMatchOutcome.IsSharedFirst(world, "you") && StrengthMatchOutcome.IsVictory(world, "you") &&
        StrengthMatchOutcome.Explain(world, "you").Contains("你并列第一"), "shared first was not recognized");
    leader.alive = false; winner.alive = false;
    Check(!StrengthMatchOutcome.IsSharedFirst(world, "you"), "single survivor was called a shared leader");
    winner.alive = true; leader.alive = true;
    world.remaining = 1; Check(!StrengthMatchOutcome.IsSharedFirst(world, "you"), "early finish was called a timed tie");
    world.remaining = 10; world.winner = "you";
    Check(StrengthMatchOutcome.Explain(world, "you") == "你赢得了本局", "early victory was described as a timeout");
    world.remaining = 0; player.alive = false; player.eliminationReason = "strength_head";
    Check(!StrengthMatchOutcome.IsVictory(world, "you"), "eliminated player was called shared first");
    Check(StrengthMatchOutcome.Explain(world, "you").Contains("强度较弱"), "elimination reason was lost");
    player.eliminationReason = ""; world.winner = "you";
    player.alive = false; Check(!MatchOutcome.IsVictory(world, "you"), "eliminated player was called a winner");
    player.alive = true; world.winner = "other";
    Check(!MatchOutcome.IsVictory(world, "you") && !MatchOutcome.IsVictory(world, ""), "wrong player was called a winner");
    var neutral = ResultMotion.Sample(false, false, .25f);
    Check(neutral.Lift == 0 && neutral.Tilt == 0, "ordinary result played a victory pose");
    var win = ResultMotion.Sample(true, false, .25f);
    Check(win.Lift > 0 && win.Lift <= .16f && Math.Abs(win.Tilt) <= 4, "victory pose is missing or unbounded");
    var reduced = ResultMotion.Sample(true, true, .25f);
    Check(reduced.Lift == 0 && reduced.Tilt == 0, "reduced effects played a victory pose");
    var end = ResultMotion.Sample(true, false, 1f);
    Check(end.Lift == 0 && end.Tilt == 0, "celebration did not finish");
    var invalid = ResultMotion.Sample(true, false, float.NaN);
    Check(invalid.Lift == 0 && invalid.Tilt == 0, "invalid time produced a pose");
}

static void GrowthViewCases()
{
    Check(GrowthView.CharacterScale(16) == 1f && GrowthView.CharacterScale(80) == 1.5f &&
        GrowthView.CharacterScale(-1) == 1f && GrowthView.CharacterScale(int.MaxValue) == 1.5f,
        "growth scale escaped its bounds");
    Check(GrowthView.CharacterScale(48) > GrowthView.CharacterScale(16) && GrowthView.CharacterScale(48) < GrowthView.CharacterScale(80),
        "visible size did not follow current length");
    Check(GrowthView.CameraSize(16, false, 0) == 20f && GrowthView.CameraSize(80, false, 0) == 28f &&
        GrowthView.CameraSize(48, true, 0) > GrowthView.CameraSize(48, false, 0), "camera did not follow growth and boost");
    Check(GrowthView.CameraSize(48, false, -1) < GrowthView.CameraSize(48, false, 0) &&
        GrowthView.CameraSize(48, false, 1) > GrowthView.CameraSize(48, false, 0) &&
        GrowthView.CameraSize(48, false, 99) == GrowthView.CameraSize(48, false, 0), "camera preference is not bounded or defaulted");
    Check(GrowthView.NextPreference(0) == 1 && GrowthView.NextPreference(1) == -1 && GrowthView.NextPreference(-1) == 0 &&
        GrowthView.PreferenceLabel(42) == "标准", "view preference cycle or invalid fallback changed");
}

static void StrengthAppearanceCases()
{
    var neutral = StrengthAppearance.FromDefense(100, 55, 1000);
    var stronger = StrengthAppearance.FromDefense(180, 99, 1000);
    var buffed = StrengthAppearance.FromDefense(180, 99, 1120);
    Check(stronger.BodyWidth > neutral.BodyWidth && stronger.HeadScale > neutral.HeadScale, "stronger defense did not increase visual size");
    Check(Math.Abs(neutral.TailRatio - .55f) < .001f && buffed.Glow > neutral.Glow, "tail taper or temporary glow was not represented");
    var max = StrengthAppearance.FromDefense(int.MaxValue, int.MaxValue, int.MaxValue);
    var missing = StrengthAppearance.FromDefense(0, 0, 0);
    Check(max.BodyWidth <= 1.35f && max.HeadScale <= 1.18f && max.TailRatio <= 1 && max.Glow <= 1 &&
        missing.BodyWidth == .8f && missing.HeadScale == 1f && missing.Glow == 0, "appearance escaped bounds or invented a buff");
}

static void OriginalIdleMotionCases()
{
    var poses = new List<OriginalIdlePose>();
    foreach (int skin in new[] { 0, 1, 2, 3, 24 })
    {
        Check(OriginalIdleMotion.TrySample(skin, .7f, false, out var pose), "original identity lacks motion");
        Check(Math.Abs(pose.Lift) <= .0651f && Math.Abs(pose.Tilt) <= 2.801f && pose.ScaleX >= .978f && pose.ScaleY <= 1.0221f,
            "idle pose escaped its visual bounds");
        Check(OriginalIdleMotion.TrySample(skin, .7f, true, out var reduced) && reduced.Lift == 0 && reduced.Tilt == 0 && reduced.ScaleX == 1 && reduced.ScaleY == 1,
            "reduced effects did not suppress idle motion");
        poses.Add(pose);
    }
    Check(poses.Select(p => p.Lift).Distinct().Count() == poses.Count, "original characters share one idle motion");
    Check(!OriginalIdleMotion.TrySample(4, .7f, false, out _) && !OriginalIdleMotion.TrySample(-1, .7f, false, out _),
        "internal reference or invalid identity was treated as an original");
    Check(OriginalIdleMotion.TrySample(24, float.NaN, false, out var invalid) && invalid.ScaleX == 1 && invalid.Lift == 0,
        "invalid time corrupted the idle pose");
}

static void CharacterMotionCases()
{
    var motion = new CharacterMotion();
    motion.Update(0, 0, false, .05f, false, true);
    Check(motion.LeanDegrees == 0 && motion.Scale == 1, "initial motion is not neutral");
    motion.Update(1, .1f, true, .05f, false, true);
    Check(motion.LeanDegrees < 0 && motion.LeanDegrees > -8 && motion.Scale > 1 && motion.Scale < 1.08f, "turn or gradual boost did not respond");
    for (int i = 2; i < 30; i++) motion.Update(i, .1f, true, .05f, false, true);
    Check(Math.Abs(motion.LeanDegrees) < .01f && motion.Scale > 1.079f && motion.Scale <= 1.08f, "straight travel did not settle or boost exceeded limit");
    motion.Update(30, -.5f, true, .05f, false, true);
    Check(motion.LeanDegrees > 0 && motion.LeanDegrees <= 8, "opposite turn did not lean in the other direction");
    motion.Update(31, -.5f, true, .05f, true, true);
    Check(motion.LeanDegrees == 0 && motion.Scale == 1, "reduced effects did not restore a neutral pose");
    var idleReaction = StrengthReaction.Sample(-1, -1, false);
    var biteReaction = StrengthReaction.Sample(.175f, -1, false);
    var hurtReaction = StrengthReaction.Sample(-1, .225f, false);
    var mutualReaction = StrengthReaction.Sample(.175f, .225f, false);
    Check(idleReaction.ScaleX == 1 && idleReaction.ScaleY == 1 && idleReaction.DamageTint == 0, "idle character reacted without contact");
    Check(biteReaction.ScaleY > 1 && biteReaction.ScaleX < 1 && biteReaction.DamageTint == 0, "actual bite lacked a distinct pose");
    Check(hurtReaction.ScaleY < 1 && hurtReaction.ScaleX > 1 && hurtReaction.DamageTint > 0, "damage lacked a distinct pose");
    Check(mutualReaction.ScaleY == hurtReaction.ScaleY && mutualReaction.DamageTint == hurtReaction.DamageTint, "simultaneous damage did not take priority");
    var cursor = new StrengthReactionCursor();
    cursor.Observe(1, 1, -1, 0, false, false);
    Check(cursor.Observe(2, 1, -1, .175f, false, false).ScaleY > 1, "actual bite did not animate");
    var pausedCursor = new StrengthReactionCursor();
    pausedCursor.Observe(2, 2, -1, 1, true, false);
    Check(pausedCursor.Observe(2, 2, -1, 1.1f, false, false).ScaleY == 1, "paused event replayed on resume");
    var staleCursor = new StrengthReactionCursor();
    Check(staleCursor.Observe(10, 1, -1, 1, false, false).ScaleY == 1, "stale event animated");
    var reducedReaction = StrengthReaction.Sample(.175f, .225f, true);
    Check(reducedReaction.ScaleX == 1 && reducedReaction.ScaleY == 1 && reducedReaction.DamageTint == 0 &&
        StrengthReaction.Sample(float.NaN, float.PositiveInfinity, false).ScaleY == 1 &&
        StrengthReaction.Sample(2, 2, false).ScaleY == 1, "reaction leaked into reduced mode or beyond its duration");
    var wrapped = new CharacterMotion();
    wrapped.Update(0, 3.13f, false, .05f, false, true);
    wrapped.Update(1, -3.13f, false, .05f, false, true);
    Check(wrapped.LeanDegrees < 0 && wrapped.LeanDegrees > -2, "angle wrapping generated a false large turn");
    float pausedLean = wrapped.LeanDegrees, pausedScale = wrapped.Scale;
    wrapped.Update(2, 0, true, .05f, false, false);
    for (int i = 0; i < 30; i++) wrapped.Update(2, 0, true, .05f, false, false);
    Check(wrapped.LeanDegrees == pausedLean && wrapped.Scale == pausedScale, "paused motion did not freeze");
    var boosted = new CharacterMotion(); boosted.Update(0, 0, false, .05f, false, true);
    boosted.Update(1, .2f, true, .05f, false, true);
    float frozenLean = boosted.LeanDegrees, frozenScale = boosted.Scale;
    for (int i = 0; i < 30; i++) boosted.Update(1, .2f, true, .05f, false, false);
    Check(frozenScale > 1 && boosted.Scale == frozenScale && boosted.LeanDegrees == frozenLean,
        "paused boost or turn pose changed");
    wrapped.Update(0, 1, false, .05f, false, true);
    Check(wrapped.LeanDegrees == 0 && wrapped.Scale == 1, "previous match motion leaked after tick reset");
    wrapped.Update(1, float.NaN, true, .05f, false, true);
    Check(wrapped.LeanDegrees == 0 && wrapped.Scale == 1, "invalid angle changed the pose");
}

static void EliminationTransitionCases()
{
    var exit = new EliminationTransition();
    Check(!exit.ObserveDead("unseen", 1, out _, out _), "unseen or already dead character produced an exit");
    exit.ObserveAlive("bot");
    Check(exit.ObserveDead("bot", 2, out var start, out var started) && started && start == 0, "real elimination did not start");
    Check(exit.ObserveDead("bot", 2.175f, out var middle, out started) && !started && Math.Abs(middle - .5f) < .001f, "exit did not progress once");
    Check(!exit.ObserveDead("bot", 2.4f, out _, out _) && !exit.ObserveDead("bot", 3, out _, out _), "exit repeated after completion");
    exit.ObserveAlive("bot");
    Check(exit.ObserveDead("bot", 4, out _, out started) && started, "new life did not reset its exit");
    exit.Reset();
    Check(!exit.ObserveDead("bot", 4, out _, out _), "exit leaked between matches");
    exit.ObserveAlive("bot"); exit.ObserveDead("bot", 5, out _, out _); exit.ObserveAlive("bot");
    Check(exit.ObserveDead("bot", 5.1f, out var next, out started) && started && next == 0, "revived character retained an old exit");
}

static void MatchOutcomeCases()
{
    Arena Setup(out SnakeState player, out SnakeState bot)
    {
        var arena = new Arena(71, fruitCount: 0, enableCaptures: true);
        player = arena.AddSnake("you", "玩家", false); bot = arena.AddSnake("bot", "AI", true);
        player.shield = bot.shield = 0;
        return arena;
    }
    void Collide(Arena arena) => typeof(Arena).GetMethod("ResolveCollisions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, null);
    var arena = Setup(out var player, out var bot);
    player.segments = new() { new(0, 0), new(-1, 0), new(-2, 0) };
    bot.segments = new() { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };
    Collide(arena); arena.State.phase = "ended";
    Check(player.eliminationReason == "head" && player.eliminatedBy == "bot" && MatchOutcome.Explain(arena.State, "you").Contains("长度较短"), "unequal head collision not explained");
    arena = Setup(out player, out bot);
    player.segments = new() { new(0, 0), new(-1, 0), new(-2, 0) };
    bot.segments = new() { new(0, 0), new(1, 0), new(2, 0) };
    Collide(arena); arena.State.phase = "ended";
    Check(player.eliminationReason == "head_tie" && !bot.alive && MatchOutcome.Explain(arena.State, "you").Contains("双方出局"), "equal head collision not explained");
    arena = Setup(out player, out bot);
    player.segments = new() { new(0, 0), new(-1, 0), new(-2, 0) };
    bot.segments = new() { new(5, 0), new(3, 0), new(0, 0) };
    Collide(arena); arena.State.phase = "ended";
    Check(player.eliminationReason == "trail" && MatchOutcome.Explain(arena.State, "you").Contains("轨迹"), "trail collision not explained");
    player.eliminationReason = "capture";
    Check(MatchOutcome.Explain(arena.State, "you").Contains("收圈"), "capture not explained");
    player.eliminationReason = "unknown";
    Check(MatchOutcome.Explain(arena.State, "you") == "本局已出局", "unknown cause invented a reason");
    player.alive = true; arena.State.remaining = 0;
    Check(MatchOutcome.Explain(arena.State, "you").Contains("时间到"), "time limit not explained");
}

static void CaptureSweepCases()
{
    var shapes = new[]
    {
        new List<Point> { new(0, 0), new(6, 0), new(6, 6), new(4, 6), new(4, 2), new(2, 2), new(2, 6), new(0, 6) },
        new List<Point> { new(0, 0), new(6, 6), new(0, 6), new(6, 0) }
    };
    var intersections = new List<float>();
    foreach (var shape in shapes)
    {
        var integer = shape.Select(p => new CapturePoint((int)(p.x * 1000), (int)(p.y * 1000))).ToList();
        for (int row = 0; row < 24; row++)
        {
            float y = (row + .37f) / 4;
            CaptureGeometry.Scanline(shape, y, intersections);
            Check(intersections.Count % 2 == 0, "scanline has unmatched edge intersections");
            for (int column = -2; column <= 26; column++)
            {
                float x = (column + .31f) / 4;
                bool visual = false;
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                    if (x >= intersections[i] && x <= intersections[i + 1]) visual = true;
                bool captured = CaptureGeometry.Contains(integer, new CapturePoint((int)(x * 1000), (int)(y * 1000)));
                Check(visual == captured, "sweep disagrees with capture area");
            }
        }
        shape.Reverse();
        CaptureGeometry.Scanline(shape, 1.5f, intersections);
        Check(intersections.Count == 2, "winding reversed or horizontal edge mishandled");
    }
    CaptureGeometry.Scanline(null, 0, intersections);
    Check(intersections.Count == 0, "invalid polygon retained stale scanlines");
}

static void CaptureGeometryCases()
{
    var polygon = new List<CapturePoint> { new(0, 0), new(4000, 0), new(4000, 4000), new(0, 4000) };
    Check(CaptureGeometry.Contains(polygon, new CapturePoint(2000, 2000)), "inside target was not captured");
    Check(CaptureGeometry.Contains(polygon, new CapturePoint(0, 2000)), "boundary must count as inside");
    Check(!CaptureGeometry.Contains(polygon, new CapturePoint(5000, 2000)), "outside target was captured");
    polygon.Reverse();
    Check(CaptureGeometry.Contains(polygon, new CapturePoint(2000, 2000)), "reverse winding changed capture");
    var path = new List<CapturePoint> { new(3000, 1000), new(3000, 2000), new(3000, 3000), new(2000, 4000),
        new(1000, 4000), new(0, 4000), new(0, 0), new(4000, 0) };
    Check(CaptureGeometry.TryLoop(new CapturePoint(3000, -1000), path, out var loop), "crossing the visible body did not form a loop");
    Check(CaptureGeometry.Contains(loop.polygon, new CapturePoint(1000, 1000)), "target inside closed loop was missed");
    Check(!CaptureGeometry.TryLoop(new CapturePoint(3000, 1000), path, out _), "stationary head incorrectly closed a loop");
    Check(!CaptureGeometry.TryLoop(new CapturePoint(2000, -1000), path.GetRange(0, 5), out _), "short path incorrectly formed a loop");
}

static void CapturePlayable()
{
    for (int turn = 1; turn <= 8; turn++) for (int seed = 1; seed <= 12; seed++)
    {
        var match = new DailyMatch(seed);
        int initial = ((int)Math.Round(match.State.snakes[0].angle * 256 / (Math.PI * 2)) + 256) & 255;
        while (!match.Finished && match.State.tick < 200 && match.State.captureTick < 0)
            match.Step((byte)((initial + (match.State.tick + 1) * turn) & 255), false);
        if (match.State.captureOwner == "you") return;
    }
    throw new Exception("no starting-length loop could be closed with a continuous turn");
}

static void GameplayComparison()
{
    Check(!DailyMatch.SupportsRules("comparison-c"), "offline comparison became a server rules version");
    Check(!DailyMatch.SupportsRules("opening-experiment"), "opening experiment became a server rules version");
    foreach (int seed in new[] { 1, 17, 12345 })
    {
        var baseline = new DailyMatch(seed, DailyMatch.RulesVersion);
        var trial = DailyMatch.CreateOpeningExperiment(seed);
        for (int i = 0; i < baseline.State.snakes.Count; i++)
        {
            var a = baseline.State.snakes[i]; var d = trial.State.snakes[i];
            Check(a.shield == 3f && d.shield == 4f && a.segments.Count == d.segments.Count &&
                a.segments[0].x == d.segments[0].x && a.segments[0].y == d.segments[0].y,
                "opening experiment changed spawn layout or protection was not equal");
        }
        for (int tick = 0; tick < 60 && !baseline.Finished && !trial.Finished; tick++)
        {
            baseline.Step(0, false); trial.Step(0, false);
            for (int i = 0; i < baseline.State.snakes.Count; i++)
            {
                var a = baseline.State.snakes[i]; var d = trial.State.snakes[i];
                Check(a.segments[0].x == d.segments[0].x && a.segments[0].y == d.segments[0].y,
                    "opening experiment diverged while both sides were protected");
            }
        }
    }
    bool sawLoop = false;
    for (int turn = 1; turn <= 8 && !sawLoop; turn++) for (int seed = 1; seed <= 12 && !sawLoop; seed++)
    {
        var b = new DailyMatch(seed, DailyMatch.RulesVersion);
        var c = DailyMatch.CreateNoCaptureComparison(seed);
        Check(b.State.snakes.Count == c.State.snakes.Count && b.State.fruits.Count == c.State.fruits.Count, "comparison setup differs");
        for (int i = 0; i < b.State.snakes.Count; i++)
        {
            var left = b.State.snakes[i]; var right = c.State.snakes[i];
            Check(left.segments.Count == right.segments.Count && left.angle == right.angle, "comparison starting length or angle differs");
            for (int j = 0; j < left.segments.Count; j++)
                Check(left.segments[j].x == right.segments[j].x && left.segments[j].y == right.segments[j].y, "comparison spawn differs");
        }
        for (int i = 0; i < b.State.fruits.Count; i++)
            Check(b.State.fruits[i].position.x == c.State.fruits[i].position.x && b.State.fruits[i].position.y == c.State.fruits[i].position.y, "comparison fruit layout differs");
        int initial = ((int)Math.Round(b.State.snakes[0].angle * 256 / (Math.PI * 2)) + 256) & 255;
        while (!b.Finished && !c.Finished && b.State.tick < 200 && b.State.captureTick < 0)
        {
            byte direction = (byte)((initial + (b.State.tick + 1) * turn) & 255);
            b.Step(direction, false); c.Step(direction, false);
            if (b.State.captureTick >= 0)
            {
                Check(c.State.captureTick < 0, "no-capture comparison reported a loop");
                sawLoop = true;
                break;
            }
            for (int i = 0; i < b.State.snakes.Count; i++)
            {
                var left = b.State.snakes[i]; var right = c.State.snakes[i];
                Check(left.alive == right.alive && left.segments.Count == right.segments.Count &&
                    left.segments[0].x == right.segments[0].x && left.segments[0].y == right.segments[0].y,
                    "comparison diverged before the first capture");
            }
        }
    }
    Check(sawLoop, "no scripted comparison produced a loop");
}

static void CaptureResolution()
{
    var match = new DailyMatch(17);
    var matchType = typeof(DailyMatch);
    var pointType = matchType.GetNestedType("SimPoint", BindingFlags.NonPublic);
    var snakes = (IList)matchType.GetField("snakes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
    var snakeType = snakes[0].GetType();
    var alive = snakeType.GetField("alive"); var shield = snakeType.GetField("shield");
    var segments = snakeType.GetField("segments");
    object PointAt(int x, int y) => Activator.CreateInstance(pointType, new object[] { x, y });
    void SetPath(int index, params (int x, int y)[] points)
    {
        var path = (IList)segments.GetValue(snakes[index]); path.Clear();
        foreach (var point in points) path.Add(PointAt(point.x, point.y));
    }
    for (int i = 0; i < snakes.Count; i++) { alive.SetValue(snakes[i], false); shield.SetValue(snakes[i], 0); }
    alive.SetValue(snakes[0], true); alive.SetValue(snakes[1], true); alive.SetValue(snakes[2], true); alive.SetValue(snakes[3], true);
    shield.SetValue(snakes[2], 1);
    SetPath(0, (3000, 1000), (3000, 2000), (3000, 3000), (2000, 4000), (1000, 4000), (0, 4000), (0, 0), (4000, 0));
    SetPath(1, (1000, 1000), (1100, 1000));
    SetPath(2, (1500, 1500), (1600, 1500));
    SetPath(3, (2000, 2000), (2100, 2000));
    var previous = Array.CreateInstance(pointType, snakes.Count);
    previous.SetValue(PointAt(3000, -1000), 0);
    matchType.GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(match, new object[] { previous });
    Check(!(bool)alive.GetValue(snakes[1]) && (bool)alive.GetValue(snakes[2]) && !(bool)alive.GetValue(snakes[3]), "multiple capture failed or ignored spawn protection");
    Check((int)snakeType.GetField("kills").GetValue(snakes[0]) == 2 && ((IList)segments.GetValue(snakes[0])).Count == 10,
        "capture did not credit and immediately grow the survivor");
    Check(match.State.captureOwner == "you" && match.State.captureCount == 2 && match.State.capturePolygon.Count > 2,
        "capture presentation is not derived from the resolved event");

    // Both loops are evaluated from the same snapshot; mutual captures do not
    // award growth to either eliminated owner.
    match = new DailyMatch(18);
    snakes = (IList)matchType.GetField("snakes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
    for (int i = 0; i < snakes.Count; i++) { alive.SetValue(snakes[i], false); shield.SetValue(snakes[i], 0); }
    alive.SetValue(snakes[0], true); alive.SetValue(snakes[1], true);
    SetPath(0, (3000, 1000), (3000, 2000), (3000, 3000), (2000, 4000), (1000, 4000), (0, 4000), (0, 0), (4000, 0));
    SetPath(1, (1000, 1000), (1000, 2000), (1000, 3000), (2000, 4000), (3000, 4000), (4000, 4000), (4000, 0), (0, 0));
    previous = Array.CreateInstance(pointType, snakes.Count);
    previous.SetValue(PointAt(3000, -1000), 0); previous.SetValue(PointAt(1000, -1000), 1);
    matchType.GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(match, new object[] { previous });
    Check(!(bool)alive.GetValue(snakes[0]) && !(bool)alive.GetValue(snakes[1]), "mutual captures must resolve simultaneously");
    matchType.GetMethod("Sync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(match, null);
    Check(match.State.snakes[0].eliminationReason == "capture" && match.State.snakes[0].eliminatedBy == "bot-1", "daily capture cause was not projected");
    Check((int)snakeType.GetField("kills").GetValue(snakes[0]) == 0 && (int)snakeType.GetField("kills").GetValue(snakes[1]) == 0,
        "an eliminated captor incorrectly received growth");
    Check(match.State.captureOwner == "you" && match.State.captureCount == 0 && match.State.capturePolygon.Count > 2,
        "mutual daily capture displayed an uncredited elimination");
}

static void OpponentEdgeCases()
{
    Check(OpponentEdgeIndicator.KeepPrevious(22 * 22, 20 * 20), "near-equal opponents caused rapid switching");
    Check(!OpponentEdgeIndicator.KeepPrevious(24 * 24, 20 * 20) && !OpponentEdgeIndicator.KeepPrevious(float.NaN, 1), "clearly nearer or invalid opponent did not replace old marker");
    var world = new ArenaState { phase = "running" };
    var player = new SnakeState { id = "you", segments = new List<Point> { new(0, 0) } };
    var bot = new SnakeState { id = "bot", bot = true, segments = new List<Point> { new(20, 0) } };
    world.snakes.Add(player); world.snakes.Add(bot);
    Check(OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out float distance, out var edge) &&
        distance == 400 && edge.x == 970, "near off-screen actor was not mapped to the right edge");
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1000, 960), out _, out _), "visible actor was labelled off-screen");
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", player, new Point(1200, 960), out _, out _), "player was labelled as opponent");
    bot.segments[0] = new Point(31, 0);
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out _, out _), "distant actor was shown as nearby");
    bot.segments[0] = new Point(20, 0); bot.alive = false;
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out _, out _), "dead actor was shown");
    bot.alive = true; world.eventStatus = "active"; world.eventKind = "bounty"; world.eventTarget = "bot";
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out _, out _), "bounty target received a duplicate indicator");
    world.eventStatus = ""; world.starVisible = true; world.starHolder = "bot";
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out _, out _), "star carrier received a duplicate indicator");
    world.starVisible = false;
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(float.NaN, 0), out _, out _), "invalid projection was accepted");
    player.alive = false;
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out _, out _), "dead player retained opponent indicator");
    player.alive = true; world.phase = "ended";
    Check(!OpponentEdgeIndicator.TryCandidate(world, "you", bot, new Point(1200, 960), out _, out _), "ended match retained opponent indicator");
    var safe = OpponentEdgeIndicator.BoxOrigin(new Point(970, 410));
    Check(safe.x + 140 <= 1050 && safe.y >= 656 && safe.y + 54 <= 1504, "upper-right indicator obscured HUD or tutorial");
    safe = OpponentEdgeIndicator.BoxOrigin(new Point(110, 580));
    Check(safe.y >= 656, "side indicator covered tutorial panel");
}

static void EdgeMarkerLayoutCases()
{
    var edge = new Point(970, 900);
    Check(EdgeIndicatorLayout.TryPlace(edge, null, out var first), "open edge did not receive a marker");
    var blockers = new List<EdgeObstacle> { new(first.x, first.y, 140, 54) };
    Check(EdgeIndicatorLayout.TryPlace(edge, blockers, out var shifted) && (shifted.x != first.x || shifted.y != first.y), "blocked marker did not move");
    Check(!(shifted.x < first.x + 140 && shifted.x + 140 > first.x && shifted.y < first.y + 54 && shifted.y + 54 > first.y), "shifted marker overlapped the blocker");
    blockers.Add(new EdgeObstacle(0, 0, 1080, 1920));
    Check(!EdgeIndicatorLayout.TryPlace(edge, blockers, out _), "marker obscured a fully occupied screen");
    Check(!EdgeIndicatorLayout.TryPlace(new Point(float.NaN, 0), null, out _), "invalid edge was accepted");
    Check(EdgeIndicatorLayout.TryPlace(new Point(970, 410), null, out var corner) && corner.y >= 656, "minimap or tutorial was covered");
    float width = EdgeIndicatorLayout.ObjectiveWidth;
    Check(EdgeIndicatorLayout.TryPlace(new Point(970, 900), null, out var wide, width) && wide.x >= 30 && wide.x + width <= 1050,
        "wide objective indicator exceeded the safe viewport");
    var extension = new List<EdgeObstacle> { new(wide.x + 150, wide.y, 40, 54) };
    Check(EdgeIndicatorLayout.TryPlace(new Point(970, 900), extension, out var moved, width) &&
        !(moved.x < extension[0].x + extension[0].width && moved.x + width > extension[0].x &&
          moved.y < extension[0].y + extension[0].height && moved.y + 54 > extension[0].y), "extended part ignored an obstacle");
    Check(EdgeIndicatorLayout.TryPlace(new Point(970, 410), null, out var wideCorner, width) && wideCorner.y >= 656,
        "wide indicator covered minimap or tutorial");
    Check(!EdgeIndicatorLayout.TryPlace(edge, null, out _, float.NaN) && !EdgeIndicatorLayout.TryPlace(edge, null, out _, 1021),
        "invalid objective width was accepted");
    foreach (float x in new[] { 110f, 540f, 970f }) foreach (float y in new[] { 410f, 580f, 900f, 1450f })
    {
        if (!EdgeIndicatorLayout.TryPlace(new Point(x, y), null, out var candidate, width)) continue;
        Check(candidate.x >= 30 && candidate.x + width <= 1050 && candidate.y >= 383 && candidate.y <= 1450,
            "wide marker left the usable screen");
        Check(!(candidate.x + width > 760 && candidate.y < 545) &&
            !(candidate.y < 656 && candidate.y + 54 > 544 && candidate.x + width > 94 && candidate.x < 986),
            "wide marker covered minimap or tutorial in another edge position");
    }
}

static void CaptureNoticeCases()
{
    var world = new ArenaState { phase = "running", tick = 100, captureTick = 100, captureOwner = "you", captureCount = 0 };
    world.snakes.Add(new SnakeState { id = "you" });
    var bot = new SnakeState { id = "bot", bot = true }; world.snakes.Add(bot);
    Check(CaptureNotice.Text(world, "you", false) == "轨迹闭合！", "empty own loop gave a capture notice");
    Check(CaptureNotice.Text(world, "you", false, true) == "围住对手 · 攻防提升！", "strength loop did not explain its effect");
    world.captureCount = 2;
    Check(CaptureNotice.Text(world, "you", false).Contains("2"), "own capture count missing");
    world.captureOwner = "bot";
    Check(CaptureNotice.Text(world, "you", true).StartsWith("AI") && CaptureNotice.Text(world, "you", false) == "", "bot ownership or visibility incorrect");
    bot.bot = false; Check(CaptureNotice.Text(world, "you", true).StartsWith("玩家"), "human capture attributed to AI");
    world.captureCount = 0; Check(CaptureNotice.Text(world, "you", true) == "", "empty opponent loop announced a capture");
    world.captureCount = 1; world.captureOwner = "missing"; Check(CaptureNotice.Text(world, "you", true) == "", "unknown owner announced");
    world.captureOwner = "you"; world.tick = 99; Check(CaptureNotice.Text(world, "you", true) == "", "future capture announced");
    world.tick = 112; Check(CaptureNotice.Text(world, "you", true) == "" && CaptureNotice.Text(world, "you", true, true) == "", "expired capture announced");
    world.tick = 100; world.phase = "ended"; Check(CaptureNotice.Text(world, "you", true) == "", "ended match announced capture");
}

static void EventTargetIndicatorCases()
{
    var world = new ArenaState { phase = "running", eventStatus = "active", eventKind = "bounty", eventTarget = "bot" };
    var bot = new SnakeState { id = "bot", segments = new List<Point> { new(12, -4) } }; world.snakes.Add(bot);
    Check(EventTargetIndicator.TryTarget(world, "you", out var target, out var name) && target.x == 12 && target.y == -4 && name == "悬赏", "bounty target not shown");
    Check(!EventTargetIndicator.TryTarget(world, "bot", out _, out _), "player was directed toward their own bounty");
    bot.boss = true;
    Check(EventTargetIndicator.TryTarget(world, "you", out _, out name) && name == "首领", "boss not distinguished");
    bot.alive = false;
    Check(!EventTargetIndicator.TryTarget(world, "you", out _, out _), "dead bounty target remained visible");
    world.eventKind = "star"; world.starVisible = true; world.starHolder = "you"; world.starPosition = new Point(4, 5);
    Check(!EventTargetIndicator.TryTarget(world, "you", out _, out _), "carried star incorrectly pointed away");
    world.starHolder = "bot";
    Check(EventTargetIndicator.TryTarget(world, "you", out target, out name) && name == "星核" && target.x == 4, "star position missing");
    world.starVisible = false; world.eventKind = "fruit"; world.eventRadius = 14; world.eventPosition = new Point(-7, 9);
    Check(EventTargetIndicator.TryTarget(world, "you", out target, out name) && name == "果潮" && target.y == 9, "fruit target missing");
    world.eventStatus = "ended"; Check(!EventTargetIndicator.TryTarget(world, "you", out _, out _), "ended event remained visible");
    world.strengthPickupVisible = true; world.strengthPickupPosition = new Point(6, -8);
    Check(EventTargetIndicator.TryTarget(world, "you", out target, out name) && name == "能量" && target.x == 6 && target.y == -8,
        "strength pickup location was not used for its marker");
    world.strengthPickupVisible = false; world.strengthPickupPending = true;
    Check(EventTargetIndicator.TryTarget(world, "you", out target, out name) && name == "能量预告" && target.x == 6 && target.y == -8,
        "announced pickup location was not used for its marker");
    world.strengthPickupPending = false;
    Check(!EventTargetIndicator.TryEdge(new Point(50, 50), 0, 0, 100, 100, out _), "on-screen target gained edge marker");
    Check(EventTargetIndicator.TryEdge(new Point(150, 100), 0, 0, 100, 100, out var edge) && Math.Abs(edge.x - 100) < .001 && Math.Abs(edge.y - 75) < .001, "diagonal marker lost direction");
    Check(EventTargetIndicator.TryEdge(new Point(50, -50), 0, 0, 100, 100, out edge) && edge.x == 50 && edge.y == 0, "vertical marker misplaced");
    Check(!EventTargetIndicator.TryEdge(new Point(float.NaN, 0), 0, 0, 100, 100, out _), "invalid projection accepted");
}

static void EventOutcomeCases()
{
    var notice = new EventOutcomeNotice();
    var world = new ArenaState { phase = "running", tick = 300, eventKind = "bounty", eventStatus = "waiting" };
    var player = new SnakeState { id = "you" }; world.snakes.Add(player);
    notice.Observe(world, "you");
    world.eventStatus = "ended"; notice.Observe(world, "you");
    Check(notice.Current(world) == "", "event that never started produced a notice");
    world.eventStatus = "active"; notice.Observe(world, "you");
    player.starPoints = 2; world.tick = 350; world.eventStatus = "ended"; notice.Observe(world, "you");
    Check(notice.Current(world).Contains("巨蛇悬赏") && notice.Current(world).Contains("2 事件点"), "earned points were not shown");
    world.tick = 409; notice.Observe(world, "you");
    Check(notice.Current(world) != "", "notice expired too early");
    world.tick = 410; Check(notice.Current(world) == "", "notice did not expire");
    notice.Reset(); player.starPoints = 0; world.tick = 500; world.eventKind = "star"; world.eventStatus = "active"; notice.Observe(world, "you");
    world.eventStatus = "ended"; notice.Observe(world, "you");
    Check(notice.Current(world).Contains("星核争夺") && notice.Current(world).Contains("未得事件点"), "no-reward outcome misrepresented");
    world.phase = "ended"; Check(notice.Current(world) == "", "event notice obscured result");
    notice.Reset(); world.phase = "running"; world.eventKind = "fruit"; world.eventStatus = "active"; notice.Observe(world, "missing");
    world.eventStatus = "ended"; notice.Observe(world, "missing");
    Check(notice.Current(world) == "", "unknown player was reported as earning no points");
}

static void StarPursuitCases()
{
    int DailyTarget(string rules, int holder)
    {
        var match = new DailyMatch(1, rules); // Seed 1 selects the star event.
        var type = typeof(DailyMatch);
        var snakes = (IList)type.GetField("snakes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(match);
        var snakeType = snakes[0].GetType(); var pointType = type.GetNestedType("SimPoint", BindingFlags.NonPublic);
        object PointAt(int x, int y) => Activator.CreateInstance(pointType, new object[] { x, y });
        for (int i = 0; i < snakes.Count; i++)
        {
            snakeType.GetField("alive").SetValue(snakes[i], i < 2);
            var path = (IList)snakeType.GetField("segments").GetValue(snakes[i]); path.Clear();
            int count = i == 0 ? (holder == 1 ? 12 : 20) : 16;
            for (int j = 0; j < count; j++) path.Add(PointAt(i == 0 ? 10000 : 0, 0));
        }
        type.GetField("eventActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(match, holder >= 0);
        type.GetField("starActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(match, holder >= 0);
        type.GetField("starHolder", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(match, holder);
        type.GetMethod("UpdateBot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(match, new object[] { 1 });
        return (int)snakeType.GetField("target").GetValue(snakes[1]);
    }
    bool Right(int angle) => angle < 64 || angle > 192;
    Check(!Right(DailyTarget(DailyMatch.CaptureRulesVersion, 0)), "old daily-3 AI changed");
    Check(!Right(DailyTarget(DailyMatch.RulesVersion, -1)), "inactive event changed AI behavior");
    Check(Right(DailyTarget(DailyMatch.RulesVersion, 0)), "daily-4 AI did not pursue a nearby carrier");
    Check(Right(DailyTarget(DailyMatch.CaptureRulesVersion, 1)) && !Right(DailyTarget(DailyMatch.RulesVersion, 1)), "daily-4 AI carrier did not evade a nearby threat");
    Check(DailyMatch.StartingLengthFor(DailyMatch.CaptureRulesVersion) == 16 && DailyMatch.MaxScoreFor(DailyMatch.CaptureRulesVersion) == 1460, "daily-3 capture rules changed");
    var old = new DailyMatch(1, DailyMatch.CaptureRulesVersion);
    while (!old.Finished)
    {
        int tick = old.State.tick; old.Step((byte)((tick / 100 * 29) & 255), tick % 173 >= 140);
    }
    Check(old.Score == 100 && old.State.tick == 1200, "published daily-3 replay changed");

    Point LocalTarget(bool carrier)
    {
        var arena = new Arena(27, fruitCount: 0, enableCaptures: true, rotatingEvents: true);
        var player = arena.AddSnake("you", "you", false); var bot = arena.AddSnake("bot", "bot", true);
        player.segments = Enumerable.Repeat(new Point(10, 0), 20).ToList();
        bot.segments = Enumerable.Repeat(new Point(0, 0), 16).ToList();
        arena.State.starVisible = carrier; arena.State.starHolder = carrier ? "you" : "";
        var controls = (IDictionary)typeof(Arena).GetField("controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(arena);
        object control = controls["bot"];
        typeof(Arena).GetMethod("UpdateBot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, new[] { (object)bot, control, .1f });
        return (Point)control.GetType().GetField("target").GetValue(control);
    }
    Check(LocalTarget(false).x < 0 && LocalTarget(true).x > 0, "practice AI did not pursue a nearby carrier");
}

static void DailyEvents()
{
    var observed = new HashSet<string>();
    bool sawGiant = false;
    for (int seed = 1; seed <= 36 && observed.Count < 3; seed++)
    {
        var a = new DailyMatch(seed); var b = new DailyMatch(seed);
        Point player = a.State.snakes[0].segments[0];
        Check(a.State.snakes.Skip(1).Count(s => Point.Distance(player, s.segments[0]) <= 22) >= 1, "no opponent was spawned nearby");
        while (!a.Finished && a.State.tick < 315)
        {
            byte direction = (byte)((a.State.tick / 100 * 29) & 255);
            a.Step(direction, false); b.Step(direction, false);
            Check(a.State.eventKind == b.State.eventKind && a.State.eventStatus == b.State.eventStatus && a.Score == b.Score,
                "event replay differs with identical seed and inputs");
            if (a.State.tick == 300 && a.State.eventKind == "bounty" && a.State.eventStatus == "active")
            {
                var target = a.State.snakes.Find(s => s.id == a.State.eventTarget);
                Check(target != null && target.alive && target.segments.Count >= 20, "bounty target is not a giant snake");
                sawGiant = true;
            }
        }
        if (a.State.tick >= 300 && a.State.eventStatus != "waiting") observed.Add(a.State.eventKind);
    }
    Check(observed.SetEquals(new[] { "bounty", "star", "fruit" }), "not all three daily events were observed");
    Check(sawGiant, "no active giant bounty was observed");
}

static void LocalRotatingEvents()
{
    var observed = new HashSet<string>();
    for (int seed = 0; seed < 80 && observed.Count < 3; seed++)
    {
        var arena = new Arena(seed, fruitCount: 100, enableCaptures: true, rotatingEvents: true);
        var player = arena.AddSnake("you", "you", false);
        var target = arena.AddSnake("bot", "bot", true);
        player.shield = target.shield = 1000;
        arena.Start();
        for (int i = 0; i < 149; i++) arena.Tick(.1f);
        Check(arena.State.eventStatus == "", "event began before 15 seconds");
        for (int i = 0; i < 3 && arena.State.eventStatus != "active"; i++) arena.Tick(.1f);
        Check(arena.State.eventStatus == "active", "event did not begin after 15 seconds");
        string kind = arena.State.eventKind;
        if (!observed.Add(kind)) continue;
        Check(arena.State.eventRemaining > 24 && arena.State.eventRemaining <= 25, "incorrect event duration");
        if (kind == "star")
        {
            Check(arena.State.starVisible, "star did not spawn");
            arena.State.starHolder = player.id;
            var update = typeof(Arena).GetMethod("UpdateStar", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 81; i++) update.Invoke(arena, new object[] { .1f });
            Check(player.starPoints == 1 && arena.State.starHolder == player.id, "eight-second carry failed");
            target.alive = false;
            update.Invoke(arena, new object[] { .1f });
            for (int i = 0; i < 81 && arena.State.eventStatus == "active"; i++) update.Invoke(arena, new object[] { .1f });
            Check(player.starPoints == 2 && arena.State.eventStatus == "ended" && !arena.State.starVisible, "star did not finish at two points");
        }
        else
        {
            if (kind == "bounty") Check(arena.State.eventTarget == target.id && target.segments.Count >= 20, "bounty target missing or too short");
            if (kind == "fruit") Check(arena.State.eventRadius == 14 && arena.State.fruits.Count(f => Point.Distance(f.position, arena.State.eventPosition) <= 14.01f) >= 40, "fruit tide did not concentrate fruit");
            var center = kind == "fruit" ? arena.State.eventPosition : new Point(0, 0);
            player.shield = target.shield = 0;
            player.segments = new List<Point> { center + new Point(3, 1), center + new Point(3, 2), center + new Point(3, 3), center + new Point(2, 4), center + new Point(1, 4), center + new Point(0, 4), center, center + new Point(4, 0) };
            target.segments = new List<Point> { center + new Point(1, 1), center + new Point(1.1f, 1) };
            typeof(Arena).GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena,
                new object[] { new Dictionary<string, Point> { ["you"] = center + new Point(3, -1) } });
            Check(!target.alive && player.kills == 1 && player.starPoints == (kind == "bounty" ? 2 : 1), "actual capture did not award event points");
            typeof(Arena).GetMethod("UpdateRotatingEvent", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, new object[] { .1f });
            if (kind == "bounty") Check(arena.State.eventStatus == "ended" && arena.State.eventTarget == "", "dead bounty target did not end event");
            else
            {
                var updateEvent = typeof(Arena).GetMethod("UpdateRotatingEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int i = 0; i < 251 && arena.State.eventStatus == "active"; i++) updateEvent.Invoke(arena, new object[] { .1f });
                Check(arena.State.eventStatus == "ended" && arena.State.eventRadius == 0 && arena.State.eventRemaining == 0, "fruit tide did not expire cleanly");
            }
        }
    }
    Check(observed.SetEquals(new[] { "bounty", "star", "fruit" }), "rotation did not cover all three events");
    var early = new Arena(1, fruitCount: 0, rotatingEvents: true);
    early.AddSnake("you", "you", false); early.AddSnake("bot", "bot", true); early.Start(); early.EndRound();
    for (int i = 0; i < 200; i++) early.Tick(.1f);
    Check(early.State.eventStatus == "" && !early.State.starVisible, "event began after an early round end");
}

static void PveFixedCombatWorkspaceCases()
{
    var workspace = new PveFixedCombatWorkspace(2);
    var first = new[] { new PveFixedFighter("a", new PveFixedMotion(0, 0, 0, 10)), new PveFixedFighter("b", new PveFixedMotion(0, 0, 0, 10)) };
    PveFixedCombat.Resolve(first, workspace);
    Check(!first[0].alive && !first[1].alive, "setup did not produce deaths");
    var second = new[] { new PveFixedFighter("a", new PveFixedMotion(-30000, 0, 0, 10)), new PveFixedFighter("b", new PveFixedMotion(30000, 0, 0, 10)) };
    PveFixedCombat.Resolve(second, workspace);
    Check(second[0].alive && second[1].alive && second[0].kills == 0 && second[1].kills == 0, "workspace retained deaths");
    bool rejected = false;
    try { PveFixedCombat.Resolve(second, new PveFixedCombatWorkspace(1)); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "wrong workspace size accepted");
    rejected = false;
    try { PveFixedCombat.Resolve(new[] { second[0], second[0] }, workspace); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "duplicate fighter IDs accepted");
}

static void PveBotAvoidanceCases()
{
    var bot = new PveFixedFighter("bot", new PveFixedMotion(0, 0, 0, 16));
    var threat = new PveFixedFighter("other", new PveFixedMotion(6000, 0, 0, 16));
    var fighters = new[] { bot, threat }; long x = 0, y = 0;
    PveFixedWorld.AccumulateTrailAvoidance(bot, fighters, ref x, ref y);
    Check(x < 0 && y == 0, "nearby collision trail did not push bot away");
    threat.shieldTicks = 100; x = 0; y = 0;
    PveFixedWorld.AccumulateTrailAvoidance(bot, fighters, ref x, ref y);
    Check(x == 0 && y == 0, "protected trail incorrectly affected bot");
    threat.shieldTicks = 0; bot.shieldTicks = 100;
    PveFixedWorld.AccumulateTrailAvoidance(bot, fighters, ref x, ref y);
    Check(x == 0 && y == 0, "protected bot incorrectly avoided trail");
    bot.shieldTicks = 10; threat.shieldTicks = 10;
    PveFixedWorld.AccumulateTrailAvoidance(bot, fighters, ref x, ref y);
    Check(x < 0, "bot failed to anticipate an expiring shield");
    bot.shieldTicks = 0; threat.shieldTicks = 0; threat.alive = false; x = 0; y = 0;
    PveFixedWorld.AccumulateTrailAvoidance(bot, fighters, ref x, ref y);
    Check(x == 0 && y == 0, "dead trail incorrectly affected bot");

    var head = new PveFixedFighter("head", new PveFixedMotion(4000, 0, 0, 16));
    var heads = new[] { bot, head }; x = 0; y = 0;
    PveFixedWorld.AccumulateHeadAvoidance(bot, heads, ref x, ref y);
    Check(x < 0 && y == 0, "equally long opposing head was not avoided");
    bot.shieldTicks = 21; x = 0; y = 0;
    PveFixedWorld.AccumulateHeadAvoidance(bot, heads, ref x, ref y);
    Check(x == 0 && y == 0, "long protected bot avoided a head too early");
    bot.shieldTicks = 20; head.shieldTicks = 20;
    PveFixedWorld.AccumulateHeadAvoidance(bot, heads, ref x, ref y);
    Check(x < 0, "bot did not anticipate expiring head protection");
    bot.shieldTicks = 0; head.shieldTicks = 21; x = 0; y = 0;
    PveFixedWorld.AccumulateHeadAvoidance(bot, heads, ref x, ref y);
    Check(x == 0 && y == 0, "long protected opponent was treated as an immediate threat");
    head.shieldTicks = 0; head.alive = false;
    PveFixedWorld.AccumulateHeadAvoidance(bot, heads, ref x, ref y);
    Check(x == 0 && y == 0, "dead head was avoided");
    head.alive = true; bot.motion.Grow(1);
    PveFixedWorld.AccumulateHeadAvoidance(bot, heads, ref x, ref y);
    Check(x == 0 && y == 0, "shorter head was treated as a head-on threat");
}

static void PveFixedProjectionCases()
{
    var world = new PveFixedWorld(1, false, new PlayerProgress());
    var comparison = new PveFixedWorld(1, false, new PlayerProgress());
    var view = world.RenderState(); var snake = view.snakes[0]; var fruit = view.fruits[0];
    Check(view.phase == "running" && view.snakes.Count == 8 && view.fruits.Count == Arena.FruitCount && snake.id == "you", "initial projection missing entities");
    for (int i = 0; i < 360 && !world.Finished; i++)
    {
        world.Step((byte)(i * 3), i % 40 > 30); comparison.Step((byte)(i * 3), i % 40 > 30);
        var current = world.RenderState();
        Check(ReferenceEquals(view, current) && ReferenceEquals(snake, current.snakes[0]) && ReferenceEquals(fruit, current.fruits[0]), "projection objects were not reused");
        Check(current.tick == world.Tick && current.eventKind == world.EventKind && current.eventStatus == world.EventStatus, "projection timeline or event differed");
        for (int j = 0; j < world.Fighters.Count; j++)
        {
            var fighter = world.Fighters[j]; var rendered = current.snakes[j];
            Check(rendered.alive == fighter.alive && rendered.kills == fighter.kills && rendered.starPoints == fighter.eventPoints && rendered.segments.Count == fighter.motion.Length, "projected fighter differs");
            Check(Math.Abs(rendered.segments[0].x - fighter.motion.X / 1000f) < .0001f && Math.Abs(rendered.segments[0].y - fighter.motion.Y / 1000f) < .0001f, "projected head differs");
        }
        Check(world.Fighters[0].motion.X == comparison.Fighters[0].motion.X && world.Fighters[0].motion.Y == comparison.Fighters[0].motion.Y, "projection changed simulation");
    }
}

static void PveFixedEventsCases()
{
    foreach (int seed in new[] { 0, 1, 2 })
    {
        var world = new PveFixedWorld(seed, false, new PlayerProgress());
        foreach (var fighter in world.Fighters) fighter.shieldTicks = 2000;
        for (int i = 0; i < 299; i++) world.Step(0, false);
        Check(world.EventStatus == "", "event started too early");
        world.Step(0, false);
        Check(world.EventStatus == "active" && world.EventKind == new[] { "bounty", "star", "fruit" }[seed], "wrong event started");
        var view = world.RenderState();
        Check(view.eventStatus == "active" && view.eventKind == world.EventKind && view.eventTarget == world.EventTarget, "active event projection differed");
        Check(view.starVisible == (seed == 1) && view.eventRadius == (seed == 2 ? 14f : 0f), "event visual geometry differed");
        if (seed == 0) Check(world.EventTarget.StartsWith("bot-") && world.Fighters.Any(f => f.id == world.EventTarget && f.motion.Length >= 20), "bounty target missing");
        if (seed == 2) Check(world.Fruit.Take(12).Count(f => Math.Abs(f.position.x - world.EventCenter.x) <= 14000 && Math.Abs(f.position.y - world.EventCenter.y) <= 14000) >= 6, "fruit event spawn outside region");
        if (seed == 1)
        {
            typeof(PveFixedWorld).GetField("starHolder", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(world, 0);
            for (int i = 0; i < 159; i++) world.Step(0, false);
            Check(world.Fighters[0].eventPoints == 0, "star awarded too early");
            world.Step(0, false); Check(world.Fighters[0].eventPoints == 1, "star did not award after eight seconds");
            for (int i = 0; i < 160; i++) world.Step(0, false);
            Check(world.Fighters[0].eventPoints == 2 && world.EventStatus == "ended", "star did not end at two points");
        }
        while (!world.Finished && world.Tick < 800 && world.EventStatus == "active") world.Step(0, false);
        Check(world.EventStatus == "ended" && world.EventTicksRemaining == 0, "event failed to end");
        foreach (var fighter in world.Fighters) Check(fighter.eventPoints >= 0 && fighter.eventPoints <= 2, "event point cap exceeded");
    }
    var early = new PveFixedWorld(1, false, new PlayerProgress());
    early.Fighters[0].alive = false; early.Step(0, false);
    Check(early.Finished && early.EventStatus == "", "early ending invented an event");
    var owner = new PveFixedFighter("a", new PveFixedMotion(0, 0, 0, 16));
    var target = new PveFixedFighter("b", new PveFixedMotion(0, 0, 0, 16));
    target.eliminatedBy = "a";
    PveFixedWorld.AwardEventElimination(owner, target, "bounty", "b", new CapturePoint());
    Check(owner.eventPoints == 0, "uncredited elimination awarded points");
    target.credited = true;
    PveFixedWorld.AwardEventElimination(owner, target, "bounty", "other", new CapturePoint());
    Check(owner.eventPoints == 0, "non-target bounty awarded points");
    PveFixedWorld.AwardEventElimination(owner, target, "bounty", "b", new CapturePoint());
    Check(owner.eventPoints == 2, "real bounty not awarded");
    owner.eventPoints = 0;
    PveFixedWorld.AwardEventElimination(owner, target, "fruit", "", new CapturePoint(14000, 0));
    Check(owner.eventPoints == 1, "fruit-zone boundary kill not awarded");
    PveFixedWorld.AwardEventElimination(owner, target, "fruit", "", new CapturePoint(14001, 0));
    Check(owner.eventPoints == 1, "outside fruit zone awarded points");
}

static void PveFixedRecordingCases()
{
    foreach (bool firstRun in new[] { true, false })
    foreach (int seed in new[] { 0, 1, 793, int.MaxValue })
    {
        var profile = new PlayerProgress { selectedSkin = 2, talents = new[] { 1, 2, 0, 1, 0, 0, 0, 0, 0 } };
        var live = new PveFixedWorld(seed, firstRun, profile);
        var tape = new List<byte>();
        while (!live.Finished)
        {
            byte heading = (byte)(live.Tick / 30 * 6); byte boost = (byte)(live.Tick % 100 >= 80 ? 1 : 0);
            live.Step(heading, boost != 0); tape.Add(heading); tape.Add(boost);
        }
        Check(PveFixedRecording.TryReplay(seed, firstRun, profile, tape.ToArray(), out var replay), "completed recording rejected");
        Check(replay.Tick == live.Tick && replay.Winner == live.Winner && replay.EventKind == live.EventKind && replay.StarTicks == live.StarTicks, "replayed match differs");
        for (int i = 0; i < live.Fighters.Count; i++)
        {
            var a = live.Fighters[i]; var b = replay.Fighters[i];
            Check(a.alive == b.alive && a.kills == b.kills && a.motion.Length == b.motion.Length && a.motion.X == b.motion.X && a.motion.Y == b.motion.Y, "replayed fighter differs");
        }
        var incomplete = tape.Take(tape.Count - 2).ToArray();
        Check(!PveFixedRecording.TryReplay(seed, firstRun, profile, incomplete, out _), "unfinished recording accepted");
        tape.Add(0); tape.Add(0);
        Check(!PveFixedRecording.TryReplay(seed, firstRun, profile, tape.ToArray(), out _), "extra frame after finish accepted");
    }
    Check(!PveFixedRecording.TryReplay(1, false, null, null, out _), "null recording accepted");
    Check(!PveFixedRecording.TryReplay(1, false, null, new byte[] { 1 }, out _), "partial frame accepted");
    Check(!PveFixedRecording.TryReplay(1, false, null, new byte[] { 1, 2 }, out _), "invalid boost accepted");
    Check(!PveFixedRecording.TryReplay(1, true, null, new byte[(60 * PveFixedWorld.TickRate + 1) * 2], out _), "first-run recording over limit accepted");
    Check(!PveFixedRecording.TryReplay(1, false, null, new byte[(PveFixedRecording.MaxTicks + 1) * 2], out _), "oversized recording accepted");
}

static void PveFixedWorldCases()
{
    foreach (int seed in new[] { 0, 1, 12345, int.MaxValue })
    {
        var profile = new PlayerProgress { selectedSkin = 2, talents = new[] { 1, 2, 0, 1, 0, 0, 0, 0, 0 } };
        var a = new PveFixedWorld(seed, true, profile); var b = new PveFixedWorld(seed, true, profile);
        Check(a.Fighters.Count == 8 && a.Fruit.Count == Arena.FruitCount, "missing opponents or fruit");
        Check(a.Fighters[0].shieldTicks == 4 * PveFixedWorld.TickRate, "first-run player shield should last four seconds");
        Check(a.Fighters.Skip(1).All(f => f.shieldTicks == 3 * PveFixedWorld.TickRate), "bot shields should last three seconds");
        var regular = new PveFixedWorld(seed, false, profile);
        Check(regular.Fighters[0].shieldTicks == 3 * PveFixedWorld.TickRate, "regular player shield should last three seconds");
        for (int i = 0; i < 1200 && !a.Finished; i++)
        {
            a.Step((byte)(i * 13), i % 30 > 20); b.Step((byte)(i * 13), i % 30 > 20);
            Check(a.Tick == b.Tick && a.Finished == b.Finished && a.Winner == b.Winner, "world outcome differed");
            for (int j = 0; j < a.Fighters.Count; j++)
            {
                var x = a.Fighters[j]; var y = b.Fighters[j];
                Check(x.alive == y.alive && x.kills == y.kills && x.motion.Length == y.motion.Length && x.motion.X == y.motion.X && x.motion.Y == y.motion.Y, "world fighter differed");
            }
            for (int j = 0; j < a.Fruit.Count; j++)
                Check(a.Fruit[j].position.x == b.Fruit[j].position.x && a.Fruit[j].position.y == b.Fruit[j].position.y && a.Fruit[j].color == b.Fruit[j].color, "world fruit differed");
        }
        Check(a.Finished && a.Tick <= 1200, "first-run match did not finish");
        int ended = a.Tick; a.Step(0, false); Check(a.Tick == ended, "ended match continued");
    }
}

static void PveFixedCombatCases()
{
    PveFixedFighter Fighter(string id, int x, int y, int length) => new PveFixedFighter(id, new PveFixedMotion(x, y, 0, length));
    var a = Fighter("a", 0, 0, 10); var b = Fighter("b", 0, 0, 10);
    PveFixedCombat.Resolve(new[] { a, b });
    Check(!a.alive && !b.alive && a.eliminationReason == "head_tie" && a.kills == 0 && b.kills == 0, "equal heads did not eliminate both without reward");
    a = Fighter("a", 0, 0, 10); b = Fighter("b", 0, 0, 16);
    PveFixedCombat.Resolve(new[] { a, b });
    Check(!a.alive && b.alive && a.eliminatedBy == "b" && b.kills == 1 && b.motion.Length == 21, "unequal head collision or growth incorrect");
    a = Fighter("a", 0, 0, 10); b = Fighter("b", 1800, 0, 16);
    PveFixedCombat.Resolve(new[] { a, b });
    Check(!a.alive && a.eliminationReason == "trail" && b.kills == 1, "trail collision not resolved");
    a = Fighter("a", 0, 0, 10); b = Fighter("b", 1800, 0, 16); b.shieldTicks = 1;
    PveFixedCombat.Resolve(new[] { a, b });
    Check(a.alive && b.alive, "protected defender caused a collision");
    bool captured = false;
    for (int turn = 1; turn <= 8 && !captured; turn++)
    {
        var owner = Fighter("owner", 0, 0, 16);
        for (int i = 1; i <= 200 && !captured; i++)
        {
            owner.motion.Step((byte)(i * turn), false, new PveFixedBonuses());
            if (!owner.motion.TryCapture(out var loop)) continue;
            long sx = 0, sy = 0; foreach (var point in loop.polygon) { sx += point.x; sy += point.y; }
            int x = (int)(sx / loop.polygon.Count), y = (int)(sy / loop.polygon.Count);
            if (!CaptureGeometry.Contains(loop.polygon, new CapturePoint(x, y))) continue;
            var victim = Fighter("victim", x, y, 5); victim.shieldTicks = 1;
            PveFixedCombat.Resolve(new[] { owner, victim });
            Check(victim.alive, "protected snake was captured");
            owner.captureCooldown = 0; victim.shieldTicks = 0;
            var workspace = new PveFixedCombatWorkspace(2);
            PveFixedCombat.Resolve(new[] { owner, victim }, workspace);
            if (!victim.alive && victim.eliminationReason == "capture")
            {
                Check(victim.eliminatedBy == owner.id && owner.kills == 1, "capture ownership incorrect");
                Check(workspace.captureOwner == owner.id && workspace.captureCount == 1 && workspace.capturePolygon.Count >= 3, "capture projection did not retain real geometry");
                captured = true;
            }
        }
    }
    Check(captured, "closed trail failed to capture an unprotected opponent");
    a = Fighter("you", 3000, 1000, 8); b = Fighter("bot-1", 1000, 1000, 8);
    var segmentField = typeof(PveFixedMotion).GetField("segments", BindingFlags.Instance | BindingFlags.NonPublic);
    void SetMotion(PveFixedFighter fighter, CapturePoint previous, params CapturePoint[] points)
    {
        var parts = (List<CapturePoint>)segmentField.GetValue(fighter.motion); parts.Clear(); parts.AddRange(points);
        typeof(PveFixedMotion).GetProperty("PreviousHead").SetValue(fighter.motion, previous);
    }
    SetMotion(a, new(3000, -1000), new(3000, 1000), new(3000, 2000), new(3000, 3000), new(2000, 4000), new(1000, 4000), new(0, 4000), new(0, 0), new(4000, 0));
    SetMotion(b, new(1000, -1000), new(1000, 1000), new(1000, 2000), new(1000, 3000), new(2000, 4000), new(3000, 4000), new(4000, 4000), new(4000, 0), new(0, 0));
    var mutual = new PveFixedCombatWorkspace(2);
    PveFixedCombat.Resolve(new[] { a, b }, mutual);
    Check(!a.alive && !b.alive && a.eliminationReason == "capture" && b.eliminationReason == "capture" && a.kills == 0 && b.kills == 0,
        "integer mutual captures did not resolve without credit");
    Check(mutual.captureOwner == "you" && mutual.captureCount == 0 && mutual.capturePolygon.Count > 2,
        "integer mutual capture displayed an uncredited elimination");
}

static void PveFixedMotionCases()
{
    var noBonus = new PveFixedBonuses();
    var straight = new PveFixedMotion(0, 0, 0, 16); straight.Step(0, false, noBonus);
    Check(straight.X == 300 && straight.Y == 0 && straight.Angle == 0, "straight movement differs");
    Check(straight.PreviousHead.x == 0 && straight.Segments[0].x == 300 && straight.Segments[1].x == -600, "body did not follow the real head");
    int oldLength = straight.Length; var oldTail = straight.Segments[oldLength - 1];
    straight.Grow(2);
    Check(straight.Length == oldLength + 2 && straight.Segments[oldLength].x < oldTail.x, "growth did not extend the tail");
    straight.Grow(int.MaxValue); Check(straight.Length == Arena.MaximumLength, "growth cap was not enforced");
    Check(!straight.TryCapture(out _), "straight trail produced a loop");
    bool closed = false;
    for (int turn = 1; turn <= 8 && !closed; turn++)
    {
        var looping = new PveFixedMotion(0, 0, 0, 16);
        for (int i = 1; i <= 200 && !closed; i++)
        {
            looping.Step((byte)(i * turn), false, noBonus);
            closed = looping.TryCapture(out _);
        }
    }
    Check(closed, "starting-length player could not form a valid loop");
    var slower = new PveFixedMotion(0, 0, 0, 16); var faster = new PveFixedMotion(0, 0, 0, 16);
    slower.Step(64, false, noBonus); faster.Step(64, false, new PveFixedBonuses { turn = 100 });
    Check(faster.Angle > slower.Angle && slower.Angle > 0, "small turn bonus was lost to rounding");
    var wall = new PveFixedMotion(PveFixedMotion.Edge - 1, 0, 0, 16); wall.Step(0, false, noBonus);
    Check(wall.X < PveFixedMotion.Edge && wall.Angle == 32768 && wall.ReboundTicks == 16, "wall did not reflect");
    var boost = new PveFixedMotion(0, 0, 0, 7);
    for (int i = 0; i < 150; i++) boost.Step(0, true, noBonus);
    Check(boost.Length == PveFixedMotion.MinimumLength, "boost did not stop consuming at minimum length");
    var basic = new PveFixedMotion(0, 0, 0, 16); var efficient = new PveFixedMotion(0, 0, 0, 16);
    for (int i = 0; i < 50; i++) { basic.Step(0, true, noBonus); efficient.Step(0, true, new PveFixedBonuses { boostSaving = 700 }); }
    Check(basic.Length == 15 && efficient.Length == 16, "boost-saving bonus did not delay length loss");
    var profile = new PlayerProgress { selectedSkin = 1, talents = new[] { 3, 3, 3, 3, 3, 3, 0, 0, 0 } };
    var bonuses = PveFixedBonuses.From(profile);
    Check(bonuses.turn == 100 && bonuses.boostSaving == 200 && bonuses.pickup == 100, "capped talent or skin bonus incorrect");
    var a = new PveFixedMotion(123, -456, 13, 16); var b = new PveFixedMotion(123, -456, 13, 16);
    for (int i = 0; i < 1000; i++) { a.Step((byte)(i * 7), i % 3 == 0, bonuses); b.Step((byte)(i * 7), i % 3 == 0, bonuses); }
    Check(a.X == b.X && a.Y == b.Y && a.Angle == b.Angle && a.Length == b.Length, "integer motion is not repeatable");
    for (int i = 0; i < a.Length; i++) Check(a.Segments[i].x == b.Segments[i].x && a.Segments[i].y == b.Segments[i].y, "integer trails differ");
}

static void PveConfigCases()
{
    var profile = new PlayerProgress { tutorialComplete = false, selectedSkin = 2, talents = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 } };
    var saved = PveStartConfig.Capture(profile, 3);
    profile.tutorialComplete = true; profile.selectedSkin = 1; profile.talents[0] = 3;
    Check(saved.Supported && saved.firstRun && saved.skin == 3 && saved.talents[0] == 1, "start config followed later account changes");
    var match = saved.MatchProfile(); match.talents[0] = 2;
    Check(saved.talents[0] == 1, "match changed stored snapshot");
    Check(PveStartConfig.Capture(profile, -1).skin == 1, "equipped skin not preserved");
    Check(!new PveStartConfig().Supported, "unknown snapshot version accepted");
}

static void ArenaBotEncounterCases()
{
    var hunter = new SnakeState { id = "bot-0", bot = true, segments = Enumerable.Repeat(new Point(0, 0), 16).ToList() };
    var player = new SnakeState { id = "you", segments = Enumerable.Repeat(new Point(4, 0), 16).ToList() };
    Check(Arena.HeadAvoidance(hunter, player).x < 0, "equal-length head was not avoided");
    player.shield = 1.1f;
    Check(Arena.HeadAvoidance(hunter, player).Length == 0, "long-protected head treated as dangerous");
    player.shield = 1f;
    Check(Arena.HeadAvoidance(hunter, player).x < 0, "bot did not anticipate expiring protection");
    player.shield = 0; player.segments.RemoveAt(15);
    Check(Arena.HeadAvoidance(hunter, player).Length == 0, "shorter head was avoided");
    player.segments.Add(new Point(4, 0));
    Check(Arena.PlayerPursuit(hunter, player, false).x > 0, "hunter did not pursue current player position");
    Check(Arena.PlayerPursuit(hunter, player, true).Length == 0, "player pursuit overrode star event");
    hunter.id = "bot-2";
    Check(Arena.PlayerPursuit(hunter, player, false).Length == 0, "ordinary bot became a designated hunter");
    hunter.id = "bot-0"; player.alive = false;
    Check(Arena.PlayerPursuit(hunter, player, false).Length == 0 && Arena.HeadAvoidance(hunter, player).Length == 0, "dead player affected bot behavior");
}


static void StrengthGrowthCases()
{
    var legacy = new StrengthEvalStartConfig { version = StrengthEvalStartConfig.LegacyVersion, firstRun = false };
    Check(legacy.Supported && legacy.EffectivePower == 100 && legacy.EffectiveHardness == 0, "legacy snapshot changed");
    int previousPower = 99, previousHardness = -1;
    for (int level = 1; level <= 41; level++)
    {
        var snap = StrengthEvalStartConfig.Capture(new PlayerProgress { experience = Progression.ExperienceForLevel(level) });
        Check(snap.Supported && snap.accountLevel == level && snap.basePower >= previousPower && snap.hardness >= previousHardness, "growth must be valid and monotonic");
        previousPower = snap.basePower; previousHardness = snap.hardness;
    }
    var max = StrengthEvalStartConfig.Capture(new PlayerProgress { experience = int.MaxValue });
    Check(max.accountLevel == 41 && max.basePower == 120 && max.hardness == 80, "growth cap changed");
    var snapshot = StrengthEvalStartConfig.Capture(new PlayerProgress { experience = 700, tutorialComplete = true });
    var world = new PveFixedWorld(42, false, null, true, snapshot);
    snapshot.basePower = 120;
    Check(!snapshot.Supported && world.StrengthBasePower == 103 && world.StrengthHardness == 14, "snapshot was not frozen or validated");
    Check(!PveFixedRecording.TryReplayStrength(42, snapshot, new byte[0], out _), "invalid snapshot accepted");
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    PveFixedWorld Bite(bool growth, bool advantage = false)
    {
        var cfg = growth ? StrengthEvalStartConfig.Capture(new PlayerProgress { experience = 700, tutorialComplete = true }) : null;
        var match = new PveFixedWorld(42, false, null, true, cfg, advantage);
        var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(match);
        actors[0] = new PveFixedFighter("you", new PveFixedMotion(-3600, 0, 0, 16));
        actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 21));
        for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
        match.Step(0, false); return match;
    }
    Check(Bite(false).LastPlayerBlockedTick == 1 && Bite(true).LastPlayerBiteTick == 1, "account growth did not change a real body collision");
    Check(Bite(false, true).LastPlayerBiteTick == 1, "offline advantage did not enable the real body bite");
    bool invalidLegacy = false, invalidSnapshot = false;
    try { _ = new PveFixedWorld(42, false, null, localAdvantage: true); } catch (ArgumentException) { invalidLegacy = true; }
    try { _ = new PveFixedWorld(42, false, null, true, StrengthEvalStartConfig.Capture(new PlayerProgress { tutorialComplete = true }), true); } catch (ArgumentException) { invalidSnapshot = true; }
    Check(invalidLegacy && invalidSnapshot, "offline advantage leaked into legacy or server snapshot modes");
}

static void StrengthBotHeadCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    PveFixedWorld Match(bool strong)
    {
        var snapshot = strong ? StrengthEvalStartConfig.Capture(new PlayerProgress {
            experience = Progression.ExperienceForLevel(41), tutorialComplete = true }) : null;
        var world = new PveFixedWorld(42, false, null, true, snapshot);
        var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
        actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
        actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(3000, 0, 32768, 16));
        for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
        ((int[])typeof(PveFixedWorld).GetField("botTimer", flags).GetValue(world))[1] = 10;
        ((byte[])typeof(PveFixedWorld).GetField("botDirection", flags).GetValue(world))[1] = 128;
        return world;
    }
    byte Heading(PveFixedWorld world) => (byte)typeof(PveFixedWorld).GetMethod("BotHeading", flags).Invoke(world, new object[] { 1 });
    var dangerous = Match(true); byte away = Heading(dangerous);
    Check(PveFixedMotion.DirectionX(away) > 0, "bot did not avoid a lethal head between planning updates");
    var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(dangerous);
    actors[0].shieldTicks = 2;
    Check(Heading(dangerous) == 128, "bot avoided a protected head");
    actors[0].shieldTicks = 0; actors[1].shieldTicks = 2;
    Check(Heading(dangerous) == 128, "protected bot avoided a head");
    actors[1].shieldTicks = 0;
    actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 32768, 16));
    Check(Heading(dangerous) == 128, "bot avoided a distant head moving away");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(2000, 0, 32768, 16));
    Check(PveFixedMotion.DirectionX(Heading(dangerous)) > 0, "bot ignored a lethal head in immediate contact range");
    var equal = Match(false);
    Check(Heading(equal) == 128, "bot avoided an equally strong head as if it were lethal");
    var repeated = Match(true);
    Check(Heading(repeated) == away, "head avoidance was not deterministic");
}

static void StrengthBotForagingCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    byte Heading(int fruitX)
    {
        var world = new PveFixedWorld(42, false, null, strengthPreview: true);
        var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
        actors[0] = new PveFixedFighter("you", new PveFixedMotion(-5000, 0, 0, 16)) { shieldTicks = 100 };
        actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 16));
        for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
        var fruit = (List<PveFixedFruit>)typeof(PveFixedWorld).GetField("fruit", flags).GetValue(world);
        fruit.Clear(); fruit.Add(new PveFixedFruit(new CapturePoint(fruitX, 0), 0));
        return (byte)typeof(PveFixedWorld).GetMethod("BotHeading", flags).Invoke(world, new object[] { 1 });
    }
    byte right = Heading(15000), left = Heading(-15000);
    Check(PveFixedMotion.DirectionX(right) > 0 && PveFixedMotion.DirectionX(left) < 0,
        "a nearby protected opponent prevented fruit seeking");
    Check(right == Heading(15000) && left == Heading(-15000), "fruit seeking was not deterministic");
}

static void StrengthBotTargetEligibilityCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    byte Heading(bool cooldown, bool shield)
    {
        var world = new PveFixedWorld(42, false, null, strengthPreview: true);
        var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
        actors[0] = new PveFixedFighter("you", new PveFixedMotion(-5000, 0, 0, 16));
        actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 16)) { shieldTicks = shield ? 20 : 0 };
        for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
        var fruit = (List<PveFixedFruit>)typeof(PveFixedWorld).GetField("fruit", flags).GetValue(world);
        fruit.Clear(); fruit.Add(new PveFixedFruit(new CapturePoint(15000, 0), 0));
        if (cooldown)
        {
            var combat = (StrengthBodyCombat)typeof(PveFixedWorld).GetField("strengthCombat", flags).GetValue(world);
            var times = (Dictionary<(string, string), long>)typeof(StrengthBodyCombat).GetField("nextHit", flags).GetValue(combat);
            times[("bot-1", "you")] = 8;
        }
        return (byte)typeof(PveFixedWorld).GetMethod("BotHeading", flags).Invoke(world, new object[] { 1 });
    }
    Check(PveFixedMotion.DirectionX(Heading(false, false)) < 0, "eligible body was not targeted");
    Check(PveFixedMotion.DirectionX(Heading(true, false)) > 0, "bot targeted a body during cooldown");
    Check(PveFixedMotion.DirectionX(Heading(false, true)) > 0, "protected bot targeted a body it cannot eat");
}

static void StrengthProtectionNoticeCases()
{
    foreach (bool first in new[] { false, true })
    {
        var world = new PveFixedWorld(42, first, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true);
        foreach (var opponent in world.Fighters.Skip(1)) opponent.shieldTicks = 2000;
        var notice = new StrengthProtectionNotice();
        var state = world.RenderState(); notice.Observe(world, state, "you");
        string text = notice.Text(world, state, "you", false);
        int protectionTicks = (first ? 4 : 3) * PveFixedWorld.TickRate;
        Check(text.Contains("保护 " + (first ? 4 : 3) + " 秒") && text.Contains("不能与对手互吃") && text.Contains("果实"),
            "opening protection did not explain actual immunity and food");
        Check(notice.Text(world, state, "you", true) == "" && notice.Text(world, state, "bot-1", false) == "",
            "protection cue appeared while suspended or for another player");
        for (int tick = 1; tick <= protectionTicks; tick++)
        {
            world.Step(0, false); state = world.RenderState(); notice.Observe(world, state, "you");
            text = notice.Text(world, state, "you", false);
            if (tick < protectionTicks) Check(text.Contains("不能与对手互吃"), "protection cue ended before actual immunity");
        }
        Check(!world.Finished && world.Fighters[0].shieldTicks == 0 && text.Contains("保护结束") && text.Contains("金点"),
            "actual protection expiry was not reported");
        for (int tick = 1; tick <= PveFixedWorld.TickRate; tick++)
        {
            world.Step(0, false); state = world.RenderState(); notice.Observe(world, state, "you");
        }
        Check(notice.Text(world, state, "you", false) == "", "protection expiry notice did not end on simulation time");
        world.Fighters[0].alive = false; state = world.RenderState(); notice.Observe(world, state, "you");
        Check(notice.Text(world, state, "you", false) == "", "protection notice remained after player elimination");
        var restart = new PveFixedWorld(43, first, null, true);
        state = restart.RenderState(); notice.Reset(); notice.Observe(restart, state, "you");
        Check(notice.Text(restart, state, "you", false).Contains("不能与对手互吃"), "protection cue did not reset for a new match");
        var legacy = new PveFixedWorld(42, false, null);
        state = legacy.RenderState(); notice.Observe(legacy, state, "you");
        Check(notice.Text(legacy, state, "you", false) == "", "protection cue leaked into legacy rules");
    }
}

static void StrengthBoostNoticeCases()
{
    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
    actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, PveFixedMotion.MinimumLength));
    foreach (var fighter in actors) fighter.shieldTicks = 100;
    ((List<PveFixedFruit>)typeof(PveFixedWorld).GetField("fruit", flags).GetValue(world)).Clear();
    world.Step(0, true);
    var state = world.RenderState();
    string Notice(bool request = true, bool suspend = false) => StrengthBoostNotice.Text(world, state, "you", request, suspend);
    Check(!actors[0].motion.Boosting && Notice().Contains("吃果实恢复"), "actual minimum-length boost attempt was not explained");
    Check(Notice(false) == "" && Notice(true, true) == "", "notice remained after releasing boost or while suspended");
    state.snakes[0].boosting = true;
    Check(Notice() == "", "notice contradicted a boost that actually happened");
    state.snakes[0].boosting = false; state.snakes[0].alive = false;
    Check(Notice() == "", "eliminated player received a boost notice");
    state.snakes[0].alive = true; state.phase = "ended";
    Check(Notice() == "", "ended match retained the boost notice");
    state.phase = "running"; actors[0].motion.Grow(1); state = world.RenderState();
    Check(Notice() == "", "notice remained after actual growth restored boost eligibility");
    Check(StrengthBoostNotice.Text(new PveFixedWorld(42, false, null), state, "you", true, false) == "", "strength notice leaked into legacy mode");
    var draining = new PveFixedWorld(42, false, null, strengthPreview: true);
    var drainActors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(draining);
    drainActors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, PveFixedMotion.MinimumLength + 1));
    foreach (var fighter in drainActors) fighter.shieldTicks = 1000;
    ((List<PveFixedFruit>)typeof(PveFixedWorld).GetField("fruit", flags).GetValue(draining)).Clear();
    for (int i = 0; i < 60 && drainActors[0].motion.Length > PveFixedMotion.MinimumLength; i++) draining.Step(0, true);
    Check(drainActors[0].motion.Length == PveFixedMotion.MinimumLength && drainActors[0].motion.Boosting &&
        StrengthBoostNotice.Text(draining, draining.RenderState(), "you", true, false) == "", "last real boost step falsely displayed the restriction");
    draining.Step(0, true);
    Check(!drainActors[0].motion.Boosting && StrengthBoostNotice.Text(draining, draining.RenderState(), "you", true, false) != "", "restriction was not explained after boost stopped");
}

static void StrengthHiddenDiagnosticsCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    string Fingerprint() => SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(world);
    string baseline = Fingerprint();
    Check(baseline == SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(new PveFixedWorld(42, false, null, true)), "identical initial state differs");
    var random = typeof(PveFixedWorld).GetField("random", flags);
    uint original = (uint)random.GetValue(world); random.SetValue(world, original ^ 1u);
    Check(Fingerprint() != baseline, "random state was omitted"); random.SetValue(world, original);
    var timers = (int[])typeof(PveFixedWorld).GetField("botTimer", flags).GetValue(world);
    timers[1]++; Check(Fingerprint() != baseline, "AI timer was omitted"); timers[1]--;
    var motion = world.Fighters[0].motion;
    var debt = typeof(PveFixedMotion).GetField("boostDebt", flags);
    debt.SetValue(motion, 1L); Check(Fingerprint() != baseline, "boost debt was omitted"); debt.SetValue(motion, 0L);
    var combat = typeof(PveFixedWorld).GetField("strengthCombat", flags).GetValue(world);
    var cooldowns = (Dictionary<(string attacker, string defender), long>)typeof(StrengthBodyCombat).GetField("nextHit", flags).GetValue(combat);
    cooldowns[("bot-1", "you")] = 9;
    Check(Fingerprint() != baseline, "body cooldown was omitted"); cooldowns.Clear();
    var loops = (int[])typeof(PveFixedWorld).GetField("strengthLoopCooldown", flags).GetValue(world);
    loops[0]++; Check(Fingerprint() != baseline, "loop cooldown was omitted"); loops[0]--;
    Check(Fingerprint() == baseline, "restored hidden state produced a different fingerprint");
}

static void StrengthBodyThreatCases()
{
    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
    actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-13500, 3000, 0, 16));
    for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
    var marks = world.RenderState().snakes[0].strengthThreatSegments;
    Check(marks.Count == 16 && marks[15] && marks.Count(x => x) == 1, "nearby edible tail not marked");
    var combat = typeof(PveFixedWorld).GetField("strengthCombat", flags).GetValue(world);
    var cooldowns = (Dictionary<(string attacker, string defender), long>)typeof(StrengthBodyCombat).GetField("nextHit", flags).GetValue(combat);
    cooldowns[("bot-1", "you")] = 9;
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "cooling attacker generated a warning");
    cooldowns.Clear();
    actors[1].shieldTicks = 1;
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "protected attacker generated a warning");
    actors[1].shieldTicks = 0; actors[0].shieldTicks = 1;
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "protected player generated a warning");
    actors[0].shieldTicks = 0;
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-13500, 4501, 0, 16));
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "distant attacker generated a warning");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 2000, 0, 16));
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "strong nearest segment incorrectly marked");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-1000, 0, 0, 40));
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "head contact should take precedence over body warnings");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-13500, 3000, 0, 16)) { alive = false };
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "dead attacker generated a warning");
    actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 5));
    actors[1].alive = true;
    Check(!world.RenderState().snakes[0].strengthThreatSegments.Any(x => x), "minimum-length body generated a warning");
    Check(new PveFixedWorld(42, false, null).RenderState().snakes.All(s => s.strengthThreatSegments.Count == 0), "warning leaked into legacy rules");
}

static void StrengthHeadWarningCases()
{
    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    var field = typeof(PveFixedWorld).GetField("fighters", BindingFlags.Instance | BindingFlags.NonPublic);
    var actors = (PveFixedFighter[])field.GetValue(world);
    actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(5000, 0, 0, 40));
    for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
    Check(world.TryGetStrength("bot-1", out var power, out var tail) &&
        world.RenderState().snakes[1].strengthHeadDefense == power.defense && world.RenderState().snakes[1].strengthTailDefense == tail,
        "defense appearance did not use the actual body-strength values");
    Check(world.TryFindNearbyHeadThreat("you", out var id) && id == "bot-1", "nearby stronger opponent not reported");
    Check(world.RenderState().strengthHeadThreat == "bot-1", "actual threat not projected");
    actors[0].shieldTicks = 1;
    Check(!world.TryFindNearbyHeadThreat("you", out _) && world.RenderState().strengthHeadThreat == "", "warned while player protected");
    actors[0].shieldTicks = 0; actors[1].shieldTicks = 1;
    Check(!world.TryFindNearbyHeadThreat("you", out _), "warned about protected opponent");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(6001, 0, 0, 40));
    Check(!world.TryFindNearbyHeadThreat("you", out _), "warned about distant opponent");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(5000, 0, 0, 16));
    Check(!world.TryFindNearbyHeadThreat("you", out _), "warned about tied opponent");
    actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(5000, 0, 0, 40));
    actors[1].alive = false;
    Check(!world.TryFindNearbyHeadThreat("you", out _), "warned about eliminated opponent");
    Check(!new PveFixedWorld(42, false, null).TryFindNearbyHeadThreat("you", out _), "warning leaked into legacy rules");
}

static void StrengthWeakPointCases()
{
    Check(StrengthTutorialHints.Select(0, false, false).Contains("全屏拖动") && StrengthTutorialHints.Select(0, false, false).Contains("弹开") &&
        !StrengthTutorialHints.Select(0, true, false).Contains("全屏拖动"), "strength tutorial did not follow steering");
    Check(StrengthTutorialHints.Select(0, false, false, true).Contains("果实") && StrengthTutorialHints.Select(0, false, false, true).Contains("弹开") &&
        !StrengthTutorialHints.Select(0, false, false).Contains("果实"), "opening food tutorial was missing or leaked into other rules");
    Check(StrengthTutorialHints.Select(80, false, false).Contains("全屏拖动") && StrengthTutorialHints.Select(80, true, false).Contains("第二根手指") &&
        !StrengthTutorialHints.Select(80, true, true).Contains("第二根手指") && StrengthTutorialHints.Select(80, true, true).Contains("金点") &&
        StrengthTutorialHints.Select(80, true, true).Contains("红叉"),
        "strength tutorial did not follow actual steering and boost");
    Check(StrengthTutorialHints.Select(140, true, true).Contains("围住") && StrengthTutorialHints.Select(200, true, true).Contains("当前长度") &&
        StrengthTutorialHints.Select(260, false, false) == "", "strength tutorial did not complete on time");
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
    fighters[0] = new PveFixedFighter("you", new PveFixedMotion(-11700, 0, 0, 16));
    fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 16));
    for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
    ((List<PveFixedFruit>)world.Fruit).Clear();
    var projection = world.RenderState();
    Check(projection.snakes[0].strengthWeakSegments.All(value => !value) && projection.snakes[1].strengthWeakSegments.Count == 16 &&
        !projection.snakes[1].strengthWeakSegments[0] && !projection.snakes[1].strengthWeakSegments[1] && projection.snakes[1].strengthWeakSegments[15],
        "weak-point projection did not reflect the actual head-to-tail strength rule");
    var energy = (int[])typeof(PveFixedWorld).GetField("strengthBuffTicks", flags).GetValue(world);
    energy[0] = 120;
    Check(world.RenderState().snakes[1].strengthWeakSegments[1], "attack buff did not update a real weak-point hint");
    energy[0] = 0; fighters[1].shieldTicks = 1;
    Check(world.RenderState().snakes[1].strengthWeakSegments.All(value => !value), "protected target was highlighted");
    fighters[1].shieldTicks = 0; fighters[0].shieldTicks = 1;
    Check(world.RenderState().snakes[1].strengthWeakSegments.All(value => !value), "protected attacker was offered a bite");
    fighters[0].shieldTicks = 0; fighters[1].alive = false;
    Check(world.RenderState().snakes[1].strengthWeakSegments.All(value => !value), "dead target was highlighted");
    fighters[1].alive = true; fighters[0].alive = false;
    Check(world.RenderState().snakes[1].strengthWeakSegments.All(value => !value), "dead viewer was offered a bite");
    fighters[0].alive = true;
    fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, PveFixedMotion.MinimumLength));
    Check(world.RenderState().snakes[1].strengthWeakSegments.All(value => !value), "minimum-length target was highlighted");
    fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 16));
    world.Step(0, false);
    Check(world.LastPlayerAttackTick == 1 && world.RenderState().snakes[1].strengthWeakSegments.All(value => !value),
        "a real bite did not temporarily hide the pair's cooling-down hints");
    var combat = (StrengthBodyCombat)typeof(PveFixedWorld).GetField("strengthCombat", flags).GetValue(world);
    var cooldown = (Dictionary<(string, string), long>)typeof(StrengthBodyCombat).GetField("nextHit", flags).GetValue(combat);
    cooldown[("you", "bot-1")] = world.Tick + 1;
    Check(world.RenderState().snakes[1].strengthWeakSegments.Any(value => value), "hints did not return when the next tick becomes eligible");
    var legacy = new PveFixedWorld(42, false, null);
    Check(legacy.RenderState().snakes.All(snake => snake.strengthWeakSegments.Count == 0), "weak-point hints leaked into legacy rules");
}

static void StrengthRecordingCases()
{
    foreach (int seed in new[] { 1, 73, 12345 })
    foreach (bool first in new[] { false, true })
    foreach (int variant in new[] { 0, 1, 2, 3, 4 })
    {
        bool advantage = variant > 0, openingFood = variant >= 2, openingEncounter = variant >= 3, recoverySupply = variant == 4;
        var live = new PveFixedWorld(seed, first, null, strengthPreview: true, localAdvantage: advantage, localOpeningFood: openingFood, localOpeningEncounter: openingEncounter, localRecoverySupply: recoverySupply);
        Check(live.LocalStrengthAdvantage == advantage && live.StrengthBasePower == (advantage ? 120 : 100) && live.StrengthHardness == (advantage ? 80 : 0),
            "offline advantage did not apply only when requested");
        Check(live.LocalOpeningFood == openingFood && live.LocalOpeningEncounter == openingEncounter && live.LocalRecoverySupply == recoverySupply && live.RulesVersion == (recoverySupply ? PveFixedRecording.LocalStrengthRecoveryRules : openingEncounter ? PveFixedRecording.LocalStrengthEncounterRules : openingFood ? PveFixedRecording.LocalStrengthOpeningRules : advantage ? PveFixedRecording.LocalStrengthAdvantageRules : PveFixedRecording.ExperimentalStrengthRules),
            "local practice rules were not identified separately");
        Check(live.TryGetStrength("you", out var own, out _) && live.TryGetStrength("bot-1", out var bot, out _) &&
            (advantage ? own.attack > bot.attack && own.defense > bot.defense : own.attack == bot.attack && own.defense == bot.defense),
            "starting player advantage did not change actual attack and defense");
        var tape = new List<byte>();
        while (!live.Finished)
        {
            int tick = live.Tick; byte heading = (byte)(tick / 53 * 37 + seed);
            bool boost = tick % 120 >= 95;
            tape.Add(heading); tape.Add(boost ? (byte)1 : (byte)0); live.Step(heading, boost);
        }
        Check(PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out var replay, advantage, openingFood, openingEncounter, recoverySupply) && replay.StrengthPreview &&
            live.Tick == replay.Tick && live.Winner == replay.Winner && live.StrengthPickupVisible == replay.StrengthPickupVisible &&
            live.StrengthPickupPosition.x == replay.StrengthPickupPosition.x && live.StrengthPickupPosition.y == replay.StrengthPickupPosition.y,
            "completed strength recording did not reproduce match outcome");
        for (int i = 0; i < live.Fighters.Count; i++)
        {
            var a = live.Fighters[i]; var b = replay.Fighters[i];
            Check(a.alive == b.alive && a.kills == b.kills && a.motion.Angle == b.motion.Angle && a.motion.Length == b.motion.Length &&
                a.eliminatedBy == b.eliminatedBy && a.eliminationReason == b.eliminationReason &&
                live.StrengthLoopTicks(a.id) == replay.StrengthLoopTicks(b.id) && live.StrengthBuffTicks(a.id) == replay.StrengthBuffTicks(b.id),
                "strength recording changed fighter outcome or buff");
            Check(live.TryGetStrengthLevelProgress(a.id, out int la, out int xa, out int ra) == replay.TryGetStrengthLevelProgress(b.id, out int lb, out int xb, out int rb) &&
                la == lb && xa == xb && ra == rb, "strength recording changed level progress");
            for (int j = 0; j < a.motion.Length; j++)
                Check(a.motion.Segments[j].x == b.motion.Segments[j].x && a.motion.Segments[j].y == b.motion.Segments[j].y,
                    "strength recording changed a body segment");
        }
        Check(live.PlayerSegmentsEaten == replay.PlayerSegmentsEaten && live.PlayerSegmentsLostToBites == replay.PlayerSegmentsLostToBites,
            "strength replay changed actual bite totals");
        Check(SnakeClub.Tests.StrengthPveReplayVector.Terminal(live) == SnakeClub.Tests.StrengthPveReplayVector.Terminal(replay),
            "terminal fingerprint changed after strength replay");
        string digest = StrengthStateDigest.Compute(live);
        bool otherVersionFinished = PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out var otherVersion, !advantage);
        Check(!otherVersionFinished || !StrengthStateDigest.Matches(otherVersion, digest), "different local rules accepted as the same match");
        if (openingFood)
        {
            bool oldFinished = PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out var oldReplay, true);
            Check(!oldFinished || !StrengthStateDigest.Matches(oldReplay, digest), "old advantage recording was confused with the new opening");
        }
        if (openingEncounter)
        {
            bool oldFinished = PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out var oldReplay, true, true);
            Check(!oldFinished || !StrengthStateDigest.Matches(oldReplay, digest), "previous opening recording was confused with the encounter variant");
        }
        if (recoverySupply)
        {
            bool oldFinished = PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out var oldReplay, true, true, true);
            Check(!oldFinished || !StrengthStateDigest.Matches(oldReplay, digest), "encounter recording was confused with recovery variant");
            Check(live.RecoverySuppliesUsed == replay.RecoverySuppliesUsed && live.LastRecoverySupplyTick == replay.LastRecoverySupplyTick &&
                live.NextRecoverySupplyTick == replay.NextRecoverySupplyTick && live.RecoverySupplyCollectedByPlayer == replay.RecoverySupplyCollectedByPlayer &&
                live.RecoverySupplyCollectedByOpponents == replay.RecoverySupplyCollectedByOpponents, "recovery state changed after replay");
        }
        Check(digest.StartsWith("s1-") && StrengthStateDigest.Matches(replay, digest) && !StrengthStateDigest.Matches(replay, "") &&
            !StrengthStateDigest.Matches(replay, "s1-0000000000000000"), "strength digest was not verified correctly");
        var replayFruit = (List<PveFixedFruit>)replay.Fruit;
        var fruit = replayFruit[0];
        replayFruit[0] = new PveFixedFruit(new CapturePoint(fruit.position.x + 1, fruit.position.y), fruit.color);
        Check(live.Tick == replay.Tick && live.Winner == replay.Winner && live.Fighters[0].kills == replay.Fighters[0].kills &&
            live.Fighters[0].motion.Length == replay.Fighters[0].motion.Length && !StrengthStateDigest.Matches(replay, digest),
            "a different terminal field passed the stronger comparison");
        replayFruit[0] = fruit;
        Check(live.Fruit.Count == replay.Fruit.Count, "strength recording changed fruit count");
        for (int i = 0; i < live.Fruit.Count; i++)
            Check(live.Fruit[i].position.x == replay.Fruit[i].position.x && live.Fruit[i].position.y == replay.Fruit[i].position.y &&
                live.Fruit[i].color == replay.Fruit[i].color && live.IsRecoveryFruit(i) == replay.IsRecoveryFruit(i), "strength recording changed fruit state");
        Check(!PveFixedRecording.TryReplayStrength(seed, first, tape.Take(tape.Count - 2).ToArray(), out _, advantage, openingFood, openingEncounter, recoverySupply), "unfinished strength recording accepted");
        tape.Add(0); tape.Add(0);
        Check(!PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out _, advantage, openingFood, openingEncounter, recoverySupply), "trailing strength input accepted");
    }
    Check(!PveFixedRecording.TryReplayStrength(1, true, null, out _) &&
        !PveFixedRecording.TryReplayStrength(1, true, new byte[] { 0 }, out _) &&
        !PveFixedRecording.TryReplayStrength(1, true, new byte[] { 0, 2 }, out _) &&
        !PveFixedRecording.TryReplayStrength(1, true, new byte[(60 * PveFixedWorld.TickRate + 1) * 2], out _), "malformed strength recording accepted");
}

static void StrengthAlignedStartCases()
{
    bool invalid = false;
    try { _ = new PveFixedWorld(1, false, null, true, localAlignedStart: true); }
    catch (ArgumentException) { invalid = true; }
    Check(invalid && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, true, true, true, true, false, true),
        "aligned start escaped the offline safe-recovery variant");
    var random = typeof(PveFixedWorld).GetField("random", BindingFlags.Instance | BindingFlags.NonPublic);
    Check(random != null, "missing RNG fixture");
    foreach (bool first in new[] { false, true })
    for (int seed = 1; seed <= 100; seed++)
    {
        var old = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true);
        var aligned = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true);
        Check(aligned.RulesVersion == PveFixedRecording.LocalStrengthAlignedStartRules && old.RulesVersion == PveFixedRecording.LocalStrengthSafeRecoveryRules &&
            (uint)random.GetValue(old) == (uint)random.GetValue(aligned), "v6 changed the initial random stream or variant identity");
        var a = aligned.Fighters[0].motion; var b = old.Fighters[0].motion;
        Check(a.X == b.X && a.Y == b.Y && a.Angle == 0 && a.Length == b.Length, "aligned player start changed head or length");
        for (int i = 0; i < a.Length; i++)
            Check(a.Segments[i].x == a.X - i * 900 && a.Segments[i].y == a.Y, "aligned body was not built behind the head");
        for (int i = 0; i < old.Fruit.Count; i++)
            Check(old.Fruit[i].position.x == aligned.Fruit[i].position.x && old.Fruit[i].position.y == aligned.Fruit[i].position.y &&
                old.Fruit[i].color == aligned.Fruit[i].color, "aligned start changed initial fruit or colors");
        for (int i = 1; i < old.Fighters.Count; i++)
        {
            var x = old.Fighters[i]; var y = aligned.Fighters[i];
            Check(x.id == y.id && x.motion.X == y.motion.X && x.motion.Y == y.motion.Y && x.motion.Angle == y.motion.Angle &&
                x.motion.Length == y.motion.Length && x.shieldTicks == y.shieldTicks, "aligned start changed an opponent's initial state");
            for (int j = 0; j < x.motion.Length; j++)
                Check(x.motion.Segments[j].x == y.motion.Segments[j].x && x.motion.Segments[j].y == y.motion.Segments[j].y,
                    "aligned start changed an opponent's body");
        }
        int startX = a.X, startY = a.Y;
        for (int tick = 1; tick <= 20; tick++)
        {
            aligned.Step(0, false);
            Check(a.X == startX + 300 * tick && a.Y == startY && a.Angle == 0,
                "default direction did not produce immediate forward movement during protection");
        }
    }
    var historical = new PveFixedWorld(40, false, null, true, localAdvantage: true, localOpeningFood: true,
        localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true);
    int originalX = historical.Fighters[0].motion.X;
    Check(historical.Fighters[0].motion.Angle == 32768, "seed 40 v5 historical heading changed");
    for (int i = 0; i < 10; i++) historical.Step(0, false);
    Check(historical.Fighters[0].motion.X - originalX == -1288, "v5 historical initial movement changed");
    PveFixedWorld Opening(bool first) => new PveFixedWorld(40, first, null, true, localAdvantage: true, localOpeningFood: true,
        localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true);
    foreach (bool first in new[] { false, true })
    {
        foreach (int turnTick in new[] { 1, 10, 20, 40 })
        foreach (byte heading in new byte[] { 32, 64, 128, 192 })
        {
            var world = Opening(first); var motion = world.Fighters[0].motion;
            for (int i = 1; i < turnTick; i++) world.Step(0, false);
            int x = motion.X, y = motion.Y;
            Check(!world.OpeningSteeringSeen && !world.OpeningTurnAssistUsed, "no-input movement consumed the opening assist");
            world.Step(heading, false, true);
            Check(world.OpeningSteeringSeen && world.OpeningTurnAssistUsed && motion.Angle == heading * 256 &&
                motion.X - x == PveFixedMotion.DirectionX(heading) * 300 / 10000 &&
                motion.Y - y == PveFixedMotion.DirectionY(heading) * 300 / 10000,
                "first actual turn did not immediately follow its direction within the window");
            Check(world.StrengthLoopTicks("you") == 0 && world.LastPlayerAttackTick < 0 && world.LastPlayerDamageTick < 0 && world.Fighters[0].shieldTicks > 0,
                "opening assist bypassed protected combat or awarded a loop buff");
            int firstAngle = motion.Angle;
            world.Step((byte)(heading + 128), false, true);
            Check(motion.Angle != ((heading + 128) & 255) * 256 && motion.Angle != firstAngle,
                "a later direction change reused the opening assist");
        }
        var small = Opening(first); small.Step(1, false, true);
        Check(small.OpeningSteeringSeen && !small.OpeningTurnAssistUsed && small.Fighters[0].motion.Angle == 256,
            "small actual steering did not consume the opportunity with normal motion");
        small.Step(128, false, true);
        Check(small.Fighters[0].motion.Angle != 32768 && !small.OpeningTurnAssistUsed, "small steering allowed a later snap");
        var right = Opening(first); right.Step(0, false, true); right.Step(128, false, true);
        Check(right.OpeningSteeringSeen && !right.OpeningTurnAssistUsed && right.Fighters[0].motion.Angle == 65536 - 1982,
            "first real rightward steering failed to consume the opportunity");
        var late = Opening(first);
        for (int i = 0; i < PveFixedWorld.OpeningTurnAssistWindowTicks; i++) late.Step(0, false);
        Check(!late.OpeningSteeringSeen && !late.OpeningTurnAssistUsed, "no-input window falsely registered steering");
        late.Step(128, false, true);
        Check(late.OpeningSteeringSeen && !late.OpeningTurnAssistUsed && late.Fighters[0].motion.Angle == 65536 - 1982,
            "late initial steering unexpectedly snapped");
    }
    foreach (bool first in new[] { false, true })
    foreach (int seed in new[] { 40, 192 })
    {
        var world = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true);
        var tape = new List<byte>();
        while (!world.Finished)
        {
            byte heading = (byte)(world.Tick / 53 * 37 + seed);
            bool boost = world.Tick % 120 >= 95;
            bool steered = world.Tick >= 5;
            tape.Add(heading); tape.Add((byte)((boost ? 1 : 0) | (steered ? 2 : 0))); world.Step(heading, boost, steered);
        }
        Check(PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out var replay, true, true, true, true, true, true) &&
            StrengthStateDigest.Compute(world) == StrengthStateDigest.Compute(replay) &&
            SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(world) == SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(replay),
            "v6 aligned-start replay differs");
        Check(!PveFixedRecording.TryReplayStrength(seed, first, tape.ToArray(), out _, true, true, true, true, true), "v5 accepted v6 steering flags");
        var invalidFlags = tape.ToArray(); invalidFlags[1] = 4;
        Check(!PveFixedRecording.TryReplayStrength(seed, first, invalidFlags, out _, true, true, true, true, true, true), "v6 accepted undefined input bits");
        invalidFlags = tape.ToArray(); invalidFlags[13] &= 1; // Input had steered by this point: flag cannot go back to zero.
        Check(!PveFixedRecording.TryReplayStrength(seed, first, invalidFlags, out _, true, true, true, true, true, true), "v6 accepted a nonmonotonic steering flag");
    }
}

static void StrengthSafeRecoveryCases()
{
    bool invalid = false;
    try { _ = new PveFixedWorld(1, false, null, true, localSafeRecovery: true); }
    catch (ArgumentException) { invalid = true; }
    Check(invalid && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, false, false, false, false, true), "safe variant escaped local recovery");
    var v4Initial = new PveFixedWorld(192, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true);
    var v5Initial = new PveFixedWorld(192, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true);
    Check(v4Initial.Fighters[0].motion.X == v5Initial.Fighters[0].motion.X && v4Initial.Fighters[0].motion.Y == v5Initial.Fighters[0].motion.Y &&
        v4Initial.Fruit[0].position.x == v5Initial.Fruit[0].position.x && v4Initial.Fruit[0].position.y == v5Initial.Fruit[0].position.y &&
        SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(v4Initial) != SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(v5Initial),
        "different future recovery rules shared the same hidden diagnostic fingerprint");
    foreach (bool safe in new[] { false, true })
    {
        var world = new PveFixedWorld(192, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: safe);
        byte heading = 0; var tape = new List<byte>();
        while (!world.Finished)
        {
            if (world.Tick % 8 == 0)
            {
                var own = world.Fighters[0].motion; long best = long.MaxValue; int dx = 0, dy = 0;
                foreach (var fighter in world.Fighters)
                {
                    if (fighter.id == "you" || !fighter.alive || fighter.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    var p = fighter.motion.Segments[fighter.motion.Length - 1]; int x = p.x - own.X, y = p.y - own.Y;
                    long distance = (long)x * x + (long)y * y;
                    if (distance < best) { best = distance; dx = x; dy = y; }
                }
                if (best < long.MaxValue) heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
            }
            tape.Add(heading); tape.Add(0); world.Step(heading, false);
            if (world.Tick == 119)
            {
                Check(world.RecoverySuppliesUsed == 1, "seed 192 did not reach the real recovery event");
                if (!safe)
                {
                    var expected = new[] { (12, -18660, 21706, 2), (128, -14254, 21338, 0), (168, -15334, 18539, 1), (199, -14794, 19939, 1) };
                    foreach (var (index, x, y, color) in expected)
                        Check(world.IsRecoveryFruit(index) && world.Fruit[index].position.x == x && world.Fruit[index].position.y == y && world.Fruit[index].color == color, "v4 golden spawn changed");
                }
                else
                {
                    int count = 0;
                    for (int i = 0; i < world.Fruit.Count; i++) if (world.IsRecoveryFruit(i))
                    {
                        count++;
                        foreach (var fighter in world.Fighters.Skip(1).Where(f => f.alive))
                        {
                            long dx = (long)world.Fruit[i].position.x - fighter.motion.X, dy = (long)world.Fruit[i].position.y - fighter.motion.Y;
                            Check(dx * dx + dy * dy > (long)PveFixedWorld.RecoverySafetyDistance * PveFixedWorld.RecoverySafetyDistance, "safe fruit spawned near an AI head");
                        }
                    }
                    Check(count > 0 && count <= 4, "safe placement had no usable fruit");
                }
            }
            if (world.Tick == 120)
                Check(safe ? world.RecoverySupplyCollectedByOpponents == 0 : world.RecoverySupplyCollectedByPlayer == 0 && world.RecoverySupplyCollectedByOpponents == 1,
                    "seed 192 next-step result changed unexpectedly");
        }
        Check(PveFixedRecording.TryReplayStrength(192, false, tape.ToArray(), out var replay, true, true, true, true, safe) &&
            StrengthStateDigest.Compute(world) == StrengthStateDigest.Compute(replay), "safe or historical match replay diverged");
        if (safe)
        {
            bool oldFinished = PveFixedRecording.TryReplayStrength(192, false, tape.ToArray(), out var oldReplay, true, true, true, true);
            Check(!oldFinished || StrengthStateDigest.Compute(oldReplay) != StrengthStateDigest.Compute(world), "v5 accepted a v4 replay as the same result");
            Check(world.RulesVersion == PveFixedRecording.LocalStrengthSafeRecoveryRules, "safe variant has wrong rule identifier");
        }
        if (!safe) Check(world.Tick == 1800 && world.Winner == "you" && StrengthStateDigest.Compute(world) == "s1-98f325c0d2869913" &&
            SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(world) == "d6654a2d1021c643", "v4 golden replay changed");
    }
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var field = typeof(PveFixedWorld).GetField("fighters", flags);
    var place = typeof(PveFixedWorld).GetMethod("PlaceRecoverySupply", flags);
    PveFixedWorld WorldAt(int x, int y, int direction)
    {
        var w = new PveFixedWorld(42, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true);
        var actors = (PveFixedFighter[])field.GetValue(w);
        actors[0] = new PveFixedFighter("you", new PveFixedMotion(x, y, direction * 256, 16));
        for (int i = 1; i < actors.Length; i++) actors[i].alive = false;
        typeof(PveFixedWorld).GetProperty("Tick").SetValue(w, 1);
        typeof(PveFixedWorld).GetProperty("LastPlayerDamageTick").SetValue(w, 1);
        return w;
    }
    foreach (int x in new[] { -PveFixedMotion.Edge, 0, PveFixedMotion.Edge })
    foreach (int y in new[] { -PveFixedMotion.Edge, 0, PveFixedMotion.Edge })
    for (int direction = 0; direction < 256; direction += 32)
    {
        var w = WorldAt(x, y, direction); place.Invoke(w, null);
        Check(w.RecoverySuppliesUsed == 1 && Enumerable.Range(0, w.Fruit.Count).Count(w.IsRecoveryFruit) == 4, "safe empty arena lost supplies");
        for (int i = 0; i < w.Fruit.Count; i++) if (w.IsRecoveryFruit(i))
        {
            var p = w.Fruit[i].position; long dx = (long)p.x - x, dy = (long)p.y - y;
            Check(Math.Abs(p.x) <= PveFixedMotion.Edge && Math.Abs(p.y) <= PveFixedMotion.Edge && dx * dx + dy * dy >= 2990L * 2990L && dx * dx + dy * dy <= 9010L * 9010L, "unsafe edge placement");
        }
    }
    var crowded = WorldAt(0, 0, 0); var crowd = (PveFixedFighter[])field.GetValue(crowded);
    for (int i = 1; i < crowd.Length; i++)
    {
        byte a = (byte)((i - 1) * 256 / 7);
        crowd[i] = new PveFixedFighter("bot-" + i, new PveFixedMotion(PveFixedMotion.DirectionX(a) * 7000 / 10000, PveFixedMotion.DirectionY(a) * 7000 / 10000, 0, 16));
    }
    var oldFruit = crowded.Fruit.ToArray(); place.Invoke(crowded, null);
    Check(crowded.RecoverySuppliesUsed == 0 && crowded.LastRecoverySupplyTick == -1 && crowded.NextRecoverySupplyTick == 0 &&
        Enumerable.Range(0, crowded.Fruit.Count).All(i => !crowded.IsRecoveryFruit(i) && crowded.Fruit[i].position.x == oldFruit[i].position.x && crowded.Fruit[i].position.y == oldFruit[i].position.y),
        "fully crowded fallback consumed supply or relocated fruit");
    var priorMarkers = (bool[])typeof(PveFixedWorld).GetField("recoveryFruit", flags).GetValue(crowded);
    priorMarkers[5] = true;
    typeof(PveFixedWorld).GetProperty("RecoverySuppliesUsed").SetValue(crowded, 1);
    typeof(PveFixedWorld).GetProperty("LastRecoverySupplyTick").SetValue(crowded, 0);
    typeof(PveFixedWorld).GetProperty("NextRecoverySupplyTick").SetValue(crowded, 1);
    place.Invoke(crowded, null);
    Check(crowded.RecoverySuppliesUsed == 1 && crowded.LastRecoverySupplyTick == 0 && crowded.NextRecoverySupplyTick == 1 && crowded.IsRecoveryFruit(5) &&
        Enumerable.Range(0, crowded.Fruit.Count).All(i => crowded.Fruit[i].position.x == oldFruit[i].position.x && crowded.Fruit[i].position.y == oldFruit[i].position.y),
        "crowded fallback erased an uncollected earlier supply or consumed another use");
    bool sawPartial = false;
    foreach (int radius in new[] { 5500, 6000, 6500, 7000, 7500, 8000, 8500 })
    foreach (int offset in new[] { 0, 8, 16, 24 })
    foreach (int count in new[] { 5, 6, 7 })
    {
        var w = WorldAt(0, 0, 0); var actors = (PveFixedFighter[])field.GetValue(w);
        for (int i = 1; i <= count; i++)
        {
            byte a = (byte)(offset + (i - 1) * 256 / count);
            actors[i] = new PveFixedFighter("bot-" + i, new PveFixedMotion(PveFixedMotion.DirectionX(a) * radius / 10000, PveFixedMotion.DirectionY(a) * radius / 10000, 0, 16));
        }
        place.Invoke(w, null); int marked = Enumerable.Range(0, w.Fruit.Count).Count(w.IsRecoveryFruit);
        if (marked > 0 && marked < PveFixedWorld.RecoverySupplyCount)
        {
            sawPartial = true; Check(w.RecoverySuppliesUsed == 1 && w.Fruit.Count == Arena.FruitCount, "partial safe placement misreported its result");
            foreach (int index in Enumerable.Range(0, w.Fruit.Count).Where(w.IsRecoveryFruit)) foreach (var ai in actors.Skip(1).Where(f => f.alive))
            {
                long dx = (long)w.Fruit[index].position.x - ai.motion.X, dy = (long)w.Fruit[index].position.y - ai.motion.Y;
                Check(dx * dx + dy * dy > (long)PveFixedWorld.RecoverySafetyDistance * PveFixedWorld.RecoverySafetyDistance, "partial placement bypassed safety");
            }
        }
    }
    Check(sawPartial, "partial fallback fixture did not exercise a constrained placement");
}

static void StrengthRecoverySupplyCases()
{
    bool invalid = false;
    try { _ = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningFood: true, localRecoverySupply: true); }
    catch (ArgumentException) { invalid = true; }
    Check(invalid && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, true, true, false, true),
        "recovery supplies escaped the isolated encounter rules");
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var fightersField = typeof(PveFixedWorld).GetField("fighters", flags);
    var place = typeof(PveFixedWorld).GetMethod("PlaceRecoverySupply", flags);
    PveFixedWorld Fixture(bool recovery, int length = 16)
    {
        var world = new PveFixedWorld(42, false, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: recovery);
        var fighters = (PveFixedFighter[])fightersField.GetValue(world);
        fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, length));
        fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-(length - 3) * 900, 0, 0, 16));
        for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
        var fruit = (List<PveFixedFruit>)world.Fruit;
        for (int i = 0; i < fruit.Count; i++) fruit[i] = new PveFixedFruit(new CapturePoint(90000, 90000), i % 4);
        return world;
    }
    int[] Marked(PveFixedWorld world) => Enumerable.Range(0, world.Fruit.Count).Where(world.IsRecoveryFruit).ToArray();
    var previous = Fixture(false); var current = Fixture(true);
    previous.Step(0, false); current.Step(0, false);
    Check(current.LastPlayerDamageTick == 1 && current.PlayerSegmentsLostToBites > 0 && current.Fighters[0].alive &&
        current.Fighters[0].motion.Length <= PveFixedWorld.RecoverySupplyLengthLimit && current.RecoverySuppliesUsed == 1 &&
        previous.PlayerSegmentsLostToBites == current.PlayerSegmentsLostToBites && previous.Fighters[0].motion.Length == current.Fighters[0].motion.Length &&
        previous.RecoverySuppliesUsed == 0 && Marked(previous).Length == 0,
        "a real bite failed to produce isolated supplies or changed combat outcome");
    Check(current.Fruit.Count == Arena.FruitCount && Marked(current).Length == PveFixedWorld.RecoverySupplyCount &&
        current.RecoverySupplyCollectedByPlayer == 0 && current.RecoverySupplyCollectedByOpponents == 0 &&
        RecoverySupplyNotice.Text(current, current.Tick, false).Contains("1/2") && RecoverySupplyNotice.Text(current, current.Tick, true) == "" &&
        RecoverySupplyNotice.Text(current, current.Tick + 4 * PveFixedWorld.TickRate, false) == "",
        "supply count, cooldown notice, or nonautomatic collection was wrong");
    var projected = current.RenderState();
    for (int i = 0; i < current.Fruit.Count; i++)
        Check(projected.fruits[i].recoverySupply == current.IsRecoveryFruit(i) && current.Fruit[i].color == previous.Fruit[i].color,
            "supply projection or original fruit color was wrong");
    int sideIndex = Marked(current).Single(i => Math.Abs(current.Fruit[i].position.y - current.Fighters[0].motion.Y) > 1000);
    int beforeGrowth = current.Fighters[0].motion.Length;
    var currentActors = (PveFixedFighter[])fightersField.GetValue(current);
    currentActors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-80000, -80000, 0, 16)) { shieldTicks = 2000 };
    for (int i = 0; i < 25; i++) current.Step(0, false);
    Check(current.RecoverySupplyCollectedByPlayer == 3 && current.RecoverySupplyCollectedByOpponents == 0 && Marked(current).SequenceEqual(new[] { sideIndex }) &&
        current.Fighters[0].motion.Length >= beforeGrowth + 3 && RecoverySupplyNotice.Text(current, current.Tick, false) != "",
        "straight movement did not collect the forward supply or preserve the visible side fruit");
    var sidePoint = current.Fruit[sideIndex].position;
    currentActors[0] = new PveFixedFighter("you", new PveFixedMotion(sidePoint.x - 1500, sidePoint.y, 0, current.Fighters[0].motion.Length)) { shieldTicks = 2000 };
    current.Step(0, false);
    Check(current.RecoverySupplyCollectedByPlayer == 4 && Marked(current).Length == 0 && RecoverySupplyNotice.Text(current, current.Tick, false) == "",
        "actual player contact did not collect the side fruit or clear its notice");
    var stolen = Fixture(true); stolen.Step(0, false);
    int supplyIndex = Marked(stolen)[0]; var supplyPoint = stolen.Fruit[supplyIndex].position;
    var stolenActors = (PveFixedFighter[])fightersField.GetValue(stolen);
    stolenActors[0] = new PveFixedFighter("you", new PveFixedMotion(-50000, -50000, 0, 13)) { shieldTicks = 2000 };
    stolenActors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(supplyPoint.x, supplyPoint.y, 0, 16)) { shieldTicks = 2000 };
    stolen.Step(0, false);
    Check(stolen.RecoverySupplyCollectedByOpponents >= 1 && !stolen.IsRecoveryFruit(supplyIndex), "opponents could not collect marked normal fruit");
    var longPlayer = Fixture(true, 25); longPlayer.Step(0, false);
    Check(longPlayer.PlayerSegmentsLostToBites > 0 && longPlayer.Fighters[0].motion.Length > PveFixedWorld.RecoverySupplyLengthLimit &&
        longPlayer.RecoverySuppliesUsed == 0, "a still-large player incorrectly received comeback supplies");
    var cooldown = Fixture(true); cooldown.Step(0, false);
    void TryAgain(int tick)
    {
        typeof(PveFixedWorld).GetProperty("Tick").SetValue(cooldown, tick);
        typeof(PveFixedWorld).GetProperty("LastPlayerDamageTick").SetValue(cooldown, tick);
        place.Invoke(cooldown, null);
    }
    TryAgain(1 + PveFixedWorld.RecoverySupplyCooldownTicks - 1);
    Check(cooldown.RecoverySuppliesUsed == 1, "recovery cooldown was bypassed");
    TryAgain(1 + PveFixedWorld.RecoverySupplyCooldownTicks);
    Check(cooldown.RecoverySuppliesUsed == 2 && Marked(cooldown).Length == 4, "second supply did not replace remaining marked fruit");
    TryAgain(1 + 2 * PveFixedWorld.RecoverySupplyCooldownTicks);
    Check(cooldown.RecoverySuppliesUsed == 2 && Marked(cooldown).Length == 4, "supply limit was bypassed");
    foreach (int x in new[] { -PveFixedMotion.Edge, -PveFixedMotion.Edge + 1500, 0, PveFixedMotion.Edge - 1500, PveFixedMotion.Edge })
    foreach (int y in new[] { -PveFixedMotion.Edge, 0, PveFixedMotion.Edge })
    for (int direction = 0; direction < 256; direction += 32)
    {
        var edge = Fixture(true); var actors = (PveFixedFighter[])fightersField.GetValue(edge);
        actors[0] = new PveFixedFighter("you", new PveFixedMotion(x, y, direction * 256, 16));
        typeof(PveFixedWorld).GetProperty("Tick").SetValue(edge, 1);
        typeof(PveFixedWorld).GetProperty("LastPlayerDamageTick").SetValue(edge, 1);
        place.Invoke(edge, null);
        Check(Marked(edge).Length == 4, "edge placement omitted recovery fruit");
        foreach (int index in Marked(edge))
        {
            var p = edge.Fruit[index].position; long dx = (long)p.x - x, dy = (long)p.y - y;
            long distance = dx * dx + dy * dy;
            Check(Math.Abs(p.x) <= PveFixedMotion.Edge && Math.Abs(p.y) <= PveFixedMotion.Edge &&
                distance >= 2990L * 2990L && distance <= 6010L * 6010L,
                "edge placement moved supply outside the arena or onto the player's head");
        }
    }
    var boostOnly = Fixture(true);
    foreach (var actor in boostOnly.Fighters) actor.shieldTicks = 2000;
    for (int i = 0; i < 80; i++) boostOnly.Step(0, true);
    Check(boostOnly.PlayerSegmentsLostToBites == 0 && boostOnly.RecoverySuppliesUsed == 0, "boost loss alone spawned recovery supplies");
}

static void StrengthOpeningEncounterCases()
{
    bool invalid = false;
    try { _ = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningEncounter: true); }
    catch (ArgumentException) { invalid = true; }
    Check(invalid && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, true, false, true),
        "opening encounter escaped the offline opening mode");
    foreach (bool first in new[] { false, true })
    {
        var positions = new HashSet<(int, int)>();
        for (int seed = 1; seed <= 100; seed++)
        {
            var world = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true);
            var old = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true);
            var player = world.Fighters[0]; var opponent = world.Fighters[1];
            Check(player.motion.X == old.Fighters[0].motion.X && player.motion.Y == old.Fighters[0].motion.Y &&
                player.motion.Angle == old.Fighters[0].motion.Angle, "encounter changed the player's seeded spawn");
            for (int i = 0; i < 8; i++)
                Check(world.Fruit[i].position.x == old.Fruit[i].position.x && world.Fruit[i].position.y == old.Fruit[i].position.y &&
                    world.Fruit[i].color == old.Fruit[i].color, "encounter changed opening food");
            long dx = opponent.motion.X - player.motion.X, dy = opponent.motion.Y - player.motion.Y;
            long distance = dx * dx + dy * dy;
            Check(distance >= 13990L * 13990L && distance <= 16010L * 16010L && dx > 0 && Math.Abs(dy) <= dx + 10,
                "opening opponent did not spawn in the expected seeded arc");
            positions.Add(((int)dx, (int)dy));
            byte heading = (byte)(opponent.motion.Angle >> 8);
            long dot = dx * PveFixedMotion.DirectionX(heading) + dy * PveFixedMotion.DirectionY(heading);
            Check(dot > 0 && dot * dot >= distance * 9990L * 9990L, "opening opponent did not face away from the player");
            var tail = opponent.motion.Segments[opponent.motion.Length - 1];
            long tx = tail.x - player.motion.X, ty = tail.y - player.motion.Y;
            Check(tx * tx + ty * ty < distance && opponent.shieldTicks == 3 * PveFixedWorld.TickRate &&
                player.shieldTicks == (first ? 4 : 3) * PveFixedWorld.TickRate,
                "opening encounter changed protection or did not expose the trailing body");
            Check(world.TryGetStrength("you", out var own, out _) && world.TryGetStrength("bot-1", out var bot, out _) &&
                own.attack > bot.attack && own.defense > bot.defense, "opening opponent changed combat advantage");
        }
        Check(positions.Count >= 90, "opening opponent positions were not seeded");
    }
}

static void StrengthOpeningFoodCases()
{
    bool invalid = false;
    try { _ = new PveFixedWorld(1, false, null, true, localOpeningFood: true); } catch (ArgumentException) { invalid = true; }
    Check(invalid && !PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, false, true),
        "opening food escaped the offline advantage mode");
    foreach (bool first in new[] { false, true })
    for (int seed = 1; seed <= 64; seed++)
    foreach (byte direction in new byte[] { 0, 64, 128, 192 })
    foreach (bool boost in new[] { false, true })
    {
        var world = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true);
        var old = new PveFixedWorld(seed, first, null, true, localAdvantage: true);
        var player = world.Fighters[0].motion;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Check(Equals(typeof(PveFixedWorld).GetField("random", flags).GetValue(world), typeof(PveFixedWorld).GetField("random", flags).GetValue(old)) &&
            Equals(typeof(PveFixedWorld).GetField("nextStrengthPickupTick", flags).GetValue(world), typeof(PveFixedWorld).GetField("nextStrengthPickupTick", flags).GetValue(old)),
            "opening placement changed the seeded random stream or pickup schedule");
        for (int i = 0; i < world.Fighters.Count; i++)
            Check(world.Fighters[i].motion.X == old.Fighters[i].motion.X && world.Fighters[i].motion.Y == old.Fighters[i].motion.Y &&
                world.Fighters[i].motion.Angle == old.Fighters[i].motion.Angle, "opening placement changed a fighter spawn");
        Check(world.Fruit.Count == Arena.FruitCount && player.X == old.Fighters[0].motion.X && player.Y == old.Fighters[0].motion.Y,
            "opening changed the fruit count or player's spawn");
        for (int i = 0; i < world.Fruit.Count; i++)
        {
            var actual = world.Fruit[i]; var previous = old.Fruit[i];
            Check(actual.color == previous.color, "opening changed the seeded fruit colors");
            if (i < 8)
            {
                long dx = actual.position.x - player.X, dy = actual.position.y - player.Y;
                long distance = dx * dx + dy * dy;
                Check(distance > 1250L * 1250L && distance < 3100L * 3100L &&
                    Math.Abs(actual.position.x) < PveFixedMotion.Edge && Math.Abs(actual.position.y) < PveFixedMotion.Edge,
                    "opening fruit was unreachable, immediately collected, or outside the arena");
                for (int j = 1; j < world.Fighters.Count; j++)
                {
                    long ox = actual.position.x - world.Fighters[j].motion.X, oy = actual.position.y - world.Fighters[j].motion.Y;
                    Check(ox * ox + oy * oy > 1250L * 1250L, "opponent spawned directly on opening fruit");
                }
            }
            else Check(actual.position.x == previous.position.x && actual.position.y == previous.position.y, "opening relocated unrelated fruit");
        }
        var initial = world.Fruit.Take(8).ToArray();
        bool collectedOpening = false;
        for (int tick = 0; tick < 40 && !world.Finished && !collectedOpening; tick++)
        {
            var before = world.Fruit.Take(8).ToArray();
            world.Step(direction, boost);
            for (int i = 0; i < 8; i++)
            {
                var point = before[i].position; var after = world.Fruit[i].position;
                long dx = point.x - player.X, dy = point.y - player.Y;
                if (point.x == initial[i].position.x && point.y == initial[i].position.y &&
                    (after.x != point.x || after.y != point.y) && dx * dx + dy * dy <= 1250L * 1250L)
                    collectedOpening = true;
            }
        }
        Check(collectedOpening && world.TryGetStrengthLevelProgress("you", out int level, out int progress, out _) && (level > 0 || progress > 0),
            "player could not collect actual opening food in the first two seconds of a straight path");
    }
}

static void StrengthNoticeCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
    fighters[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
    fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-11700, 0, 32768, 16));
    for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
    ((List<PveFixedFruit>)world.Fruit).Clear();
    world.Step(0, false);
    Check(world.LastPlayerAttackTick == 1 && world.LastPlayerDamageTick == 1 && world.LastPlayerAttackCount > 0 && world.LastPlayerDamageCount > 0,
        "real mutual body collision did not record both directions");
    Check(world.PlayerSegmentsEaten == world.LastPlayerAttackCount && world.PlayerSegmentsLostToBites == world.LastPlayerDamageCount &&
        StrengthMatchSummary.Text(world) == "", "live mutual bite totals were incorrect or prematurely presented");
    var projection = world.RenderState();
    Check(projection.strengthAttackTick == world.LastPlayerAttackTick && projection.strengthDamageTick == world.LastPlayerDamageTick &&
        Math.Abs(projection.strengthAttackPoint.x * 1000 - world.LastPlayerAttackPoint.x) < .01 &&
        Math.Abs(projection.strengthAttackPoint.y * 1000 - world.LastPlayerAttackPoint.y) < .01 &&
        Math.Abs(projection.strengthDamagePoint.x * 1000 - world.LastPlayerDamagePoint.x) < .01 &&
        Math.Abs(projection.strengthDamagePoint.y * 1000 - world.LastPlayerDamagePoint.y) < .01,
        "real bite positions were not projected to presentation state");
    Check(projection.snakes[0].strengthAttackTick == 1 && projection.snakes[0].strengthDamageTick == 1 &&
        projection.snakes[1].strengthAttackTick == 1 && projection.snakes[1].strengthDamageTick == 1,
        "actual mutual bites did not reach both character presentations");
    string notice = StrengthContactNotice.Text(world, 1, out bool warning);
    Check(warning && notice.Contains("互相吞食") && notice.Contains("吃 " + world.LastPlayerAttackCount) &&
        notice.Contains("失 " + world.LastPlayerDamageCount), "mutual bite omitted an actual outcome");
    typeof(PveFixedWorld).GetProperty("LastPlayerBlockedTick").SetValue(world, 2);
    notice = StrengthContactNotice.Text(world, 2, out warning);
    Check(warning && notice.Contains("强度不足"), "older bite hid a newer blocked contact");
    typeof(PveFixedWorld).GetProperty("LastPlayerHeadTieTick").SetValue(world, 3);
    notice = StrengthContactNotice.Text(world, 3, out warning);
    Check(warning && notice.Contains("势均力敌"), "older blocked contact hid a newer head tie");
    Check(StrengthContactNotice.Text(world, 23, out _) == "" && StrengthContactNotice.Text(world, 0, out _) == "",
        "expired or future event was shown");
    Check(StrengthContactNotice.Text(new PveFixedWorld(42, false, null), 0, out _) == "", "strength feedback leaked into legacy");
    var record = typeof(PveFixedWorld).GetMethod("RecordStrengthBite", flags);
    var reversed = new PveFixedWorld(42, false, null, strengthPreview: true);
    record.Invoke(reversed, new object[] { new BodyBite("bot-1", "you", 1, 2, 100, 50) });
    record.Invoke(reversed, new object[] { new BodyBite("you", "bot-1", 1, 3, 100, 50) });
    notice = StrengthContactNotice.Text(reversed, 0, out warning);
    Check(warning && notice.Contains("吃 3 节") && notice.Contains("失 2 节"), "mutual feedback depended on bite order");
    typeof(PveFixedWorld).GetProperty("Tick").SetValue(reversed, 1);
    record.Invoke(reversed, new object[] { new BodyBite("you", "bot-1", 1, 1, 100, 50) });
    notice = StrengthContactNotice.Text(reversed, 1, out warning);
    Check(!warning && notice.Contains("吃掉身体 1 节"), "older damage hid a newer successful bite");
    Check(reversed.PlayerSegmentsEaten == 4 && reversed.PlayerSegmentsLostToBites == 2, "body-bite totals did not accumulate actual segments");
    typeof(PveFixedWorld).GetProperty("Finished").SetValue(reversed, true);
    Check(StrengthMatchSummary.Text(reversed) == "吃到 4 节 · 被吃 2 节", "finished summary did not report actual totals");
    Check(StrengthMatchSummary.Text(null) == "" && StrengthMatchSummary.Text(new PveFixedWorld(1, false, null)) == "", "summary leaked into other modes");
    var boostOnly = new PveFixedWorld(99, false, null, strengthPreview: true);
    foreach (var fighter in boostOnly.Fighters) fighter.shieldTicks = 1000;
    ((List<PveFixedFruit>)boostOnly.Fruit).Clear();
    for (int i = 0; i < 60; i++) boostOnly.Step(0, true);
    Check(boostOnly.Fighters[0].motion.Length < 16 && boostOnly.PlayerSegmentsLostToBites == 0 && boostOnly.PlayerSegmentsEaten == 0,
        "boost consumption was counted as body damage");
}

static void StrengthFairnessCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var profile = new PlayerProgress { selectedSkin = 2, talents = new[] { 3, 3, 3, 3, 3, 3, 0, 0, 0 } };
    foreach (bool firstRun in new[] { false, true })
    {
        var neutral = new PveFixedWorld(1927, firstRun, null, strengthPreview: true);
        var decorated = new PveFixedWorld(1927, firstRun, profile, strengthPreview: true);
        for (int tick = 0; tick < 600 && !neutral.Finished; tick++)
        {
            byte heading = (byte)(tick * 7); bool boost = tick % 40 > 25;
            neutral.Step(heading, boost); decorated.Step(heading, boost);
            Check(neutral.Finished == decorated.Finished && neutral.Winner == decorated.Winner &&
                neutral.StrengthPickupVisible == decorated.StrengthPickupVisible &&
                neutral.StrengthPickupPosition.x == decorated.StrengthPickupPosition.x && neutral.StrengthPickupPosition.y == decorated.StrengthPickupPosition.y,
                "skin or talents changed strength-preview simulation");
            for (int j = 0; j < neutral.Fighters.Count; j++)
            {
                var a = neutral.Fighters[j]; var b = decorated.Fighters[j];
                Check(a.alive == b.alive && a.motion.X == b.motion.X && a.motion.Y == b.motion.Y &&
                    a.motion.Length == b.motion.Length && neutral.StrengthLevel(a.id) == decorated.StrengthLevel(b.id),
                    "profile affected fighter movement, length or level");
            }
        }
    }
    var positions = new[] { new CapturePoint(-60000, -60000), new CapturePoint(60000, -60000),
        new CapturePoint(-60000, 60000), new CapturePoint(60000, 60000) };
    var seen = new bool[4];
    for (int seed = 1; seed <= 128; seed++)
    {
        var world = new PveFixedWorld(seed, false, null, strengthPreview: true);
        var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
        for (int i = 0; i < fighters.Length; i++)
            if (i < positions.Length) fighters[i] = new PveFixedFighter(i == 0 ? "you" : "bot-" + i,
                new PveFixedMotion(positions[i].x, positions[i].y, 0, 16));
            else fighters[i].alive = false;
        typeof(PveFixedWorld).GetField("nextStrengthPickupTick", flags).SetValue(world, 0);
        typeof(PveFixedWorld).GetMethod("UpdateStrengthPickupBeforeMovement", flags).Invoke(world, null);
        Check(world.StrengthPickupPending && !world.StrengthPickupVisible, "forced pickup was not announced");
        int anchor = -1;
        for (int i = 0; i < positions.Length; i++)
        {
            long dx = (long)world.StrengthPickupPosition.x - positions[i].x, dy = (long)world.StrengthPickupPosition.y - positions[i].y;
            if (dx * dx + dy * dy <= 16010L * 16010L) { Check(anchor < 0, "ambiguous pickup anchor"); anchor = i; }
        }
        Check(anchor >= 0, "pickup did not spawn near a living fighter"); seen[anchor] = true;
    }
    Check(seen.All(value => value), "pickup anchor selection ignored a living fighter");
    var legacy = new PveFixedWorld(1927, false, profile);
    var regularBonuses = (PveFixedBonuses)typeof(PveFixedWorld).GetField("playerBonuses", flags).GetValue(legacy);
    Check(regularBonuses.pickup > 0 && regularBonuses.turn > 0, "legacy profile bonuses changed");
}

static void StrengthLevelCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    PveFixedWorld Match(int playerX, int playerLength, int botX, int botLength)
    {
        var world = new PveFixedWorld(42, false, null, strengthPreview: true);
        var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
        fighters[0] = new PveFixedFighter("you", new PveFixedMotion(playerX, 0, 0, playerLength));
        fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(botX, 0, 0, botLength));
        for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
        ((List<PveFixedFruit>)world.Fruit).Clear();
        return world;
    }
    var fruitWorld = Match(0, 16, 50000, 16);
    var fruit = (List<PveFixedFruit>)fruitWorld.Fruit;
    for (int i = 0; i < 5; i++) fruit.Add(new PveFixedFruit(new CapturePoint(300, 0), 0));
    fruitWorld.Step(0, false);
    Check(fruitWorld.TryGetStrengthLevelProgress("you", out int level, out int progress, out int required) &&
        level == 1 && progress == 0 && required == 7 && fruitWorld.TryGetStrength("you", out var strength, out _) && strength.baseStrength == 110,
        "five collected fruit did not grant the first level and its real strength");
    var projectedLevel = fruitWorld.RenderState();
    Check(fruitWorld.LastPlayerLevelUpTick == 1 && fruitWorld.LastPlayerLevelUp == 1 &&
        projectedLevel.strengthLevelUpTick == 1 && projectedLevel.strengthLevelUp == 1 &&
        StrengthLevelNotice.Text(fruitWorld, 1).Contains("1 级") && StrengthLevelNotice.Text(fruitWorld, 21) == "" &&
        StrengthLevelNotice.Text(fruitWorld, 0) == "" && StrengthLevelNotice.NewSince(fruitWorld, 0, 1) &&
        !StrengthLevelNotice.NewSince(fruitWorld, 1, 1) && !StrengthLevelNotice.NewSince(fruitWorld, -1, 1),
        "level feedback was missing, repeated or out of time");
    var award = typeof(PveFixedWorld).GetMethod("AwardStrengthExperience", flags);
    award.Invoke(fruitWorld, new object[] { "you", int.MaxValue });
    Check(fruitWorld.StrengthLevel("you") == 20 && fruitWorld.LastPlayerLevelUp == 20,
        "large level award overflowed or lost the actual final level");
    int lastLevelTick = fruitWorld.LastPlayerLevelUpTick;
    award.Invoke(fruitWorld, new object[] { "you", 1 });
    Check(fruitWorld.LastPlayerLevelUpTick == lastLevelTick && fruitWorld.LastPlayerLevelUp == 20,
        "max-level experience produced a false level-up");
    var biteWorld = Match(-11700, 16, 0, 16); biteWorld.Step(0, false);
    Check(biteWorld.LastPlayerBiteCount == 3 && biteWorld.StrengthLevel("you") == 1 &&
        biteWorld.TryGetStrengthLevelProgress("you", out level, out progress, out required) && progress == 1,
        "actual body segments did not award two experience each");
    var headWorld = Match(0, 30, 0, 5); headWorld.Step(0, false);
    Check(!headWorld.Fighters[1].alive && headWorld.Fighters[0].kills == 1 && headWorld.StrengthLevel("you") == 1,
        "credited head elimination did not grant five experience");
    var blocked = Match(-3600, 16, 0, 21);
    var xp = (int[])typeof(PveFixedWorld).GetField("strengthExperience", flags).GetValue(blocked);
    xp[0] = 4;
    var fruits = (List<PveFixedFruit>)blocked.Fruit;
    fruits.Add(new PveFixedFruit(new CapturePoint(-3600, 1350), 0));
    fruits.Add(new PveFixedFruit(new CapturePoint(-3600, -1350), 0));
    blocked.Step(0, false);
    Check(blocked.LastPlayerBlockedTick == 1 && blocked.LastPlayerBiteTick < 0 && blocked.StrengthLevel("you") == 1,
        "fruit XP changed a collision already decided in the same tick");
    xp[0] = 480;
    Check(blocked.TryGetStrengthLevelProgress("you", out level, out progress, out required) && level == 20 && required == 0,
        "level cap did not stop progression");
    Check(!blocked.TryGetStrengthLevelProgress("missing", out _, out _, out _) &&
        !new PveFixedWorld(42, false, null).TryGetStrengthLevelProgress("you", out _, out _, out _), "level leaked into legacy or missing player");
    var bots = Match(50000, 16, 0, 16);
    var botFruit = (List<PveFixedFruit>)bots.Fruit;
    for (int i = 0; i < 5; i++) botFruit.Add(new PveFixedFruit(new CapturePoint(0, 0), 0));
    bots.Step(0, false);
    Check(bots.StrengthLevel("bot-1") == 1 && bots.LastPlayerLevelUpTick == -1 && StrengthLevelNotice.Text(bots, bots.Tick) == "",
        "AI level was wrong or incorrectly signalled a player upgrade");
}

static void StrengthLoopCases()
{
    int foundTurn = 0, foundStep = 0; CapturePoint target = default;
    for (int turn = 1; turn <= 8 && foundStep == 0; turn++)
    {
        var motion = new PveFixedMotion(0, 0, 0, 80);
        for (int step = 1; step <= 200 && foundStep == 0; step++)
        {
            motion.Step((byte)(step * turn), false, new PveFixedBonuses());
            if (!motion.TryCapture(out var loop)) continue;
            for (int x = -15000; x <= 15000 && foundStep == 0; x += 500)
                for (int y = -15000; y <= 15000 && foundStep == 0; y += 500)
                {
                    var p = new CapturePoint(x, y);
                    if (!CaptureGeometry.Contains(loop.polygon, p) ||
                        !CaptureGeometry.Contains(loop.polygon, new CapturePoint(x + 600, y)) ||
                        !CaptureGeometry.Contains(loop.polygon, new CapturePoint(x - 600, y)) ||
                        !CaptureGeometry.Contains(loop.polygon, new CapturePoint(x, y + 600)) ||
                        !CaptureGeometry.Contains(loop.polygon, new CapturePoint(x, y - 600))) continue;
                    bool clear = true;
                    foreach (var part in motion.Segments)
                    { long dx = (long)part.x - x, dy = (long)part.y - y; if (dx * dx + dy * dy < 2000L * 2000L) { clear = false; break; } }
                    if (clear) { foundTurn = turn; foundStep = step; target = p; }
                }
        }
    }
    Check(foundStep > 0, "could not construct a genuine loop around a safe target");
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    PveFixedWorld MakeWorld(bool protectedTarget = false, bool absentTarget = false)
    {
        var world = new PveFixedWorld(52, false, null, strengthPreview: true);
        var fighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
        var motion = new PveFixedMotion(0, 0, 0, 80);
        for (int i = 1; i < foundStep; i++) motion.Step((byte)(i * foundTurn), false, new PveFixedBonuses());
        fighters[0] = new PveFixedFighter("you", motion);
        fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(absentTarget ? 50000 : target.x, absentTarget ? 50000 : target.y, 0, 16)) { shieldTicks = protectedTarget ? 10 : 0 };
        for (int i = 2; i < fighters.Length; i++) fighters[i].alive = false;
        return world;
    }
    var match = MakeWorld(); match.Step((byte)(foundStep * foundTurn), false);
    Check(match.Fighters[1].alive && match.StrengthLoopTicks("you") == 80 && match.TryGetStrength("you", out var powered, out _) &&
        powered.effectFactor == 1100 && match.RenderState().capturePolygon.Count > 2 && match.RenderState().captureCount == 0,
        "real closure did not grant visible operation strength without elimination");
    var energyTicks = (int[])typeof(PveFixedWorld).GetField("strengthBuffTicks", flags).GetValue(match);
    energyTicks[0] = 120;
    Check(match.TryGetStrength("you", out var combined, out _) && combined.effectFactor == 1232,
        "energy and maneuver effects did not combine according to the capped formula");
    energyTicks[0] = 0;
    var protectedMatch = MakeWorld(protectedTarget: true); protectedMatch.Step((byte)(foundStep * foundTurn), false);
    var emptyMatch = MakeWorld(absentTarget: true); emptyMatch.Step((byte)(foundStep * foundTurn), false);
    Check(protectedMatch.StrengthLoopTicks("you") == 0 && emptyMatch.StrengthLoopTicks("you") == 0,
        "empty or protected loop granted an operation bonus");
    var cooldown = (int[])typeof(PveFixedWorld).GetField("strengthLoopCooldown", flags).GetValue(match);
    Check(cooldown[0] == 200, "loop bonus did not start cooldown");
    var detect = typeof(PveFixedWorld).GetMethod("DetectStrengthLoops", flags);
    var apply = typeof(PveFixedWorld).GetMethod("ApplyStrengthLoops", flags);
    detect.Invoke(match, null); apply.Invoke(match, null);
    Check(match.StrengthLoopTicks("you") == 79 && cooldown[0] == 199, "cooldown allowed the same loop to refresh");
    for (int i = 0; i < 79; i++) apply.Invoke(match, null);
    Check(match.StrengthLoopTicks("you") == 0, "operation bonus did not expire");
    var removedTarget = MakeWorld();
    var removedFighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(removedTarget);
    removedFighters[0].motion.Step((byte)(foundStep * foundTurn), false, new PveFixedBonuses());
    detect.Invoke(removedTarget, null); removedFighters[1].alive = false; apply.Invoke(removedTarget, null);
    Check(removedTarget.StrengthLoopTicks("you") == 0, "eliminated target granted a loop bonus");
    var removedOwner = MakeWorld();
    removedFighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(removedOwner);
    removedFighters[0].motion.Step((byte)(foundStep * foundTurn), false, new PveFixedBonuses());
    detect.Invoke(removedOwner, null); removedFighters[0].alive = false; apply.Invoke(removedOwner, null);
    Check(((int[])typeof(PveFixedWorld).GetField("strengthLoopTicks", flags).GetValue(removedOwner))[0] == 0,
        "eliminated owner received a loop bonus");
    var aiMatch = MakeWorld();
    var aiFighters = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(aiMatch);
    var ownerMotion = aiFighters[0].motion;
    ownerMotion.Step((byte)(foundStep * foundTurn), false, new PveFixedBonuses());
    aiFighters[0] = new PveFixedFighter("you", aiFighters[1].motion);
    aiFighters[1] = new PveFixedFighter("bot-1", ownerMotion);
    detect.Invoke(aiMatch, null); apply.Invoke(aiMatch, null);
    Check(aiFighters[0].alive && aiMatch.StrengthLoopTicks("bot-1") == 80 && aiMatch.TryGetStrength("bot-1", out var aiPower, out _) &&
        aiPower.effectFactor == 1100, "AI did not receive the same loop reward");
    var legacy = new PveFixedWorld(52, false, null);
    Check(legacy.StrengthLoopTicks("you") == 0, "operation bonus leaked into legacy rules");
}

static void StrengthPickupCases()
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var world = new PveFixedWorld(45, true, null, strengthPreview: true);
    var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
    foreach (var actor in actors) actor.shieldTicks = 1000;
    void Place(CapturePoint point)
    {
        typeof(PveFixedWorld).GetProperty("StrengthPickupPosition").SetValue(world, point);
        typeof(PveFixedWorld).GetProperty("StrengthPickupVisible").SetValue(world, true);
        typeof(PveFixedWorld).GetField("strengthPickupExpires", flags).SetValue(world, 1000);
    }
    Place(actors[0].motion.Segments[0]);
    Check(world.RenderState().strengthPickupVisible, "real pickup was not projected");
    world.Step(0, false);
    Check(!world.StrengthPickupVisible && !world.RenderState().strengthPickupVisible && world.StrengthBuffTicks("you") == 120 &&
        world.TryGetStrength("you", out var buffed, out _) && buffed.effectFactor == 1120 && world.LastPlayerPickupTick == 1, "pickup did not apply the displayed six-second effect");
    Place(actors[0].motion.Segments[0]); world.Step(0, false);
    Check(world.StrengthBuffTicks("you") == 120 && world.LastPlayerPickupTick == 2 && world.TryGetStrength("you", out buffed, out _) && buffed.effectFactor == 1120,
        "repeated pickup stacked instead of refreshing");
    for (int i = 0; i < 119; i++) world.Step(0, false);
    Check(world.StrengthBuffTicks("you") == 1, "buff duration expired early");
    world.Step(0, false);
    Check(world.StrengthBuffTicks("you") == 0 && world.TryGetStrength("you", out var neutral, out _) && neutral.effectFactor == 1000,
        "buff remained after its expiry");

    PveFixedWorld BiteWorld(bool pickup)
    {
        var match = new PveFixedWorld(42, false, null, strengthPreview: true);
        var combatants = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(match);
        combatants[0] = new PveFixedFighter("you", new PveFixedMotion(-3600, 0, 0, 16));
        combatants[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 21));
        for (int i = 2; i < combatants.Length; i++) combatants[i].alive = false;
        if (pickup)
        {
            typeof(PveFixedWorld).GetProperty("StrengthPickupPosition").SetValue(match, new CapturePoint(-3600, 0));
            typeof(PveFixedWorld).GetProperty("StrengthPickupVisible").SetValue(match, true);
            typeof(PveFixedWorld).GetField("strengthPickupExpires", flags).SetValue(match, 1000);
        }
        match.Step(0, false);
        return match;
    }
    var unpoweredBite = BiteWorld(false); var poweredBite = BiteWorld(true);
    Check(unpoweredBite.LastPlayerBlockedTick == 1 && poweredBite.LastPlayerBiteTick == 1 &&
        poweredBite.StrengthBuffTicks("you") == 120, "pickup did not change the real body collision outcome");

    var tied = new PveFixedWorld(46, true, null, strengthPreview: true);
    var tiedActors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(tied);
    tiedActors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
    tiedActors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 16));
    for (int i = 2; i < tiedActors.Length; i++) tiedActors[i].alive = false;
    typeof(PveFixedWorld).GetProperty("StrengthPickupPosition").SetValue(tied, new CapturePoint(0, 0));
    typeof(PveFixedWorld).GetProperty("StrengthPickupVisible").SetValue(tied, true);
    typeof(PveFixedWorld).GetMethod("CollectStrengthPickup", flags).Invoke(tied, null);
    Check(tied.StrengthBuffTicks("bot-1") == 120 && tied.StrengthBuffTicks("you") == 0 && tied.LastPlayerPickupTick == -1,
        "equidistant pickup did not use stable ID ordering");
    var notice = new PveFixedWorld(44, true, null, strengthPreview: true);
    var noticeActors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(notice);
    foreach (var actor in noticeActors) actor.shieldTicks = 1000;
    var update = typeof(PveFixedWorld).GetMethod("UpdateStrengthPickupBeforeMovement", flags);
    var collect = typeof(PveFixedWorld).GetMethod("CollectStrengthPickup", flags);
    var tickField = typeof(PveFixedWorld).GetProperty("Tick");
    typeof(PveFixedWorld).GetField("nextStrengthPickupTick", flags).SetValue(notice, 1);
    tickField.SetValue(notice, 1); update.Invoke(notice, null);
    var announced = notice.StrengthPickupPosition;
    Check(notice.StrengthPickupPending && !notice.StrengthPickupVisible && notice.StrengthPickupPendingTicks == 40 &&
        notice.RenderState().strengthPickupPending && notice.RenderState().strengthPickupPendingSeconds == 2,
        "pickup announcement did not expose a two-second countdown");
    typeof(PveFixedWorld).GetProperty("StrengthPickupPosition").SetValue(notice, noticeActors[0].motion.Segments[0]);
    collect.Invoke(notice, null);
    Check(notice.StrengthPickupPending && notice.StrengthBuffTicks("you") == 0, "announced pickup could be collected early");
    typeof(PveFixedWorld).GetProperty("StrengthPickupPosition").SetValue(notice, announced);
    tickField.SetValue(notice, 40); update.Invoke(notice, null);
    Check(notice.StrengthPickupPending && !notice.StrengthPickupVisible && notice.StrengthPickupPendingTicks == 1 &&
        notice.RenderState().strengthPickupPendingSeconds == 1, "announcement ended early");
    tickField.SetValue(notice, 41); update.Invoke(notice, null);
    Check(!notice.StrengthPickupPending && notice.StrengthPickupVisible && notice.StrengthPickupPosition.x == announced.x &&
        notice.StrengthPickupPosition.y == announced.y, "pickup did not appear at the announced position");
    tickField.SetValue(notice, 240); update.Invoke(notice, null);
    Check(notice.StrengthPickupVisible, "active pickup expired before ten seconds");
    tickField.SetValue(notice, 241); update.Invoke(notice, null);
    int next = (int)typeof(PveFixedWorld).GetField("nextStrengthPickupTick", flags).GetValue(notice);
    Check(!notice.StrengthPickupVisible && !notice.StrengthPickupPending && next >= 401 && next <= 481,
        "expired pickup did not schedule the next announcement within its delay");
    var pursuit = new PveFixedWorld(51, false, null, strengthPreview: true);
    var pursuitActors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(pursuit);
    pursuitActors[0] = new PveFixedFighter("you", new PveFixedMotion(60000, 0, 0, 16));
    pursuitActors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 16));
    for (int i = 2; i < pursuitActors.Length; i++) pursuitActors[i].alive = false;
    ((List<PveFixedFruit>)pursuit.Fruit).Clear();
    typeof(PveFixedWorld).GetProperty("StrengthPickupPosition").SetValue(pursuit, new CapturePoint(0, 15000));
    typeof(PveFixedWorld).GetProperty("StrengthPickupPending").SetValue(pursuit, true);
    byte direction = (byte)typeof(PveFixedWorld).GetMethod("BotHeading", flags).Invoke(pursuit, new object[] { 1 });
    Check(PveFixedMotion.DirectionY(direction) > 8000, "AI did not respond to the visible pickup announcement");
    var normal = new PveFixedWorld(45, true, null);
    Check(!normal.RenderState().strengthPickupPending && !normal.RenderState().strengthPickupVisible && normal.StrengthBuffTicks("you") == 0, "pickup leaked into legacy rules");
    var a = new PveFixedWorld(57, true, null, true); var b = new PveFixedWorld(57, true, null, true);
    foreach (var fighter in a.Fighters) fighter.shieldTicks = 1000;
    foreach (var fighter in b.Fighters) fighter.shieldTicks = 1000;
    bool sawPickup = false;
    for (int tick = 0; tick < 250; tick++)
    {
        a.Step((byte)(tick * 7), false); b.Step((byte)(tick * 7), false);
        Check(a.StrengthPickupPending == b.StrengthPickupPending && a.StrengthPickupPendingTicks == b.StrengthPickupPendingTicks &&
            a.StrengthPickupVisible == b.StrengthPickupVisible && a.StrengthPickupPosition.x == b.StrengthPickupPosition.x &&
            a.StrengthPickupPosition.y == b.StrengthPickupPosition.y, "seeded pickup sequence diverged");
        foreach (var fighter in a.Fighters)
            Check(a.StrengthBuffTicks(fighter.id) == b.StrengthBuffTicks(fighter.id), "seeded pickup owner or duration diverged");
        sawPickup |= a.StrengthPickupVisible;
    }
    Check(sawPickup, "scheduled pickup did not appear");
}

static void StrengthPracticeWorldCases()
{
    StrengthInputs Power(int value = 100) => new() { basePower = value, speed = 300, baseSpeed = 300,
        randomFactor = 1000, operationFactor = 1000 };
    PveFixedFighter Fighter(string id, int x) => new(id, new PveFixedMotion(x, 0, 0, 16));
    var weak = Fighter("weak", 0); var strong = Fighter("strong", 0);
    StrengthHeadCombat.Resolve(new[] { weak, strong }, new[] { Power(), Power(130) });
    Check(!weak.alive && strong.alive && weak.eliminationReason == "strength_head" && weak.eliminatedBy == "strong" && strong.kills == 1,
        "stronger head did not win the real head collision");
    weak = Fighter("weak", 0); strong = Fighter("strong", 0);
    int[] deferred = StrengthHeadCombat.Resolve(new[] { weak, strong }, new[] { Power(), Power(130) }, deferGrowth: true);
    Check(!weak.alive && strong.motion.Length == 16 && deferred[1] == 8, "head growth was not deferred until body decisions completed");
    weak = Fighter("weak", 0); strong = Fighter("strong", 0);
    StrengthHeadCombat.Resolve(new[] { weak, strong }, new[] { Power(), Power() });
    Check(weak.alive && strong.alive, "equal heads were eliminated");
    var ties = new List<HeadTieContact>();
    var equal = new[] { weak, strong };
    StrengthHeadCombat.Resolve(equal, new[] { Power(), Power() }, ties: ties);
    var separated = StrengthHeadCombat.SeparateTies(equal, ties, new List<BodyBite>());
    long headDx = (long)weak.motion.X - strong.motion.X, headDy = (long)weak.motion.Y - strong.motion.Y;
    Check(separated[0] && separated[1] && headDx * headDx + headDy * headDy >= 1440L * 1440L,
        "equal overlapping heads did not separate");
    var reversedWeak = Fighter("weak", 0); var reversedStrong = Fighter("strong", 0);
    var reversed = new[] { reversedStrong, reversedWeak };
    StrengthHeadCombat.Resolve(reversed, new[] { Power(), Power() }, ties: ties);
    StrengthHeadCombat.SeparateTies(reversed, ties, new List<BodyBite>());
    Check(weak.motion.X == reversedWeak.motion.X && weak.motion.Y == reversedWeak.motion.Y &&
        strong.motion.X == reversedStrong.motion.X && strong.motion.Y == reversedStrong.motion.Y,
        "head tie depends on fighter array order");
    weak = Fighter("weak", 0); strong = Fighter("strong", 0); equal = new[] { weak, strong };
    StrengthHeadCombat.Resolve(equal, new[] { Power(), Power() }, ties: ties);
    separated = StrengthHeadCombat.SeparateTies(equal, ties, new List<BodyBite> { new BodyBite("weak", "other", 1, 1, 100, 80) });
    Check(!separated[0] && !separated[1] && weak.motion.X == 0 && strong.motion.X == 0,
        "head separation overrode a same-step body bite");
    var edgeA = new PveFixedFighter("a", new PveFixedMotion(PveFixedMotion.Edge, PveFixedMotion.Edge, 0, 16));
    var edgeB = new PveFixedFighter("b", new PveFixedMotion(PveFixedMotion.Edge, PveFixedMotion.Edge, 0, 16));
    var edgePair = new[] { edgeA, edgeB };
    StrengthHeadCombat.Resolve(edgePair, new[] { Power(), Power() }, ties: ties);
    StrengthHeadCombat.SeparateTies(edgePair, ties, new List<BodyBite>());
    Check(Math.Abs(edgeA.motion.X) <= PveFixedMotion.Edge && Math.Abs(edgeB.motion.X) <= PveFixedMotion.Edge &&
        Math.Abs(edgeA.motion.X - edgeB.motion.X) >= 1440, "head tie separation failed at map corner");
    weak = Fighter("weak", 0); strong = Fighter("strong", 0);
    weak.shieldTicks = 1;
    StrengthHeadCombat.Resolve(new[] { weak, strong }, new[] { Power(), Power(130) });
    Check(weak.alive && strong.alive, "head collision ignored protection");

    var tiedWorld = new PveFixedWorld(42, false, null, strengthPreview: true);
    var field = typeof(PveFixedWorld).GetField("fighters", BindingFlags.Instance | BindingFlags.NonPublic);
    var tiedFighters = (PveFixedFighter[])field.GetValue(tiedWorld);
    tiedFighters[0] = Fighter("you", 0); tiedFighters[1] = Fighter("bot-1", 0);
    for (int i = 2; i < tiedFighters.Length; i++) tiedFighters[i].alive = false;
    tiedWorld.Step(0, false);
    long worldDx = (long)tiedFighters[0].motion.X - tiedFighters[1].motion.X;
    long worldDy = (long)tiedFighters[0].motion.Y - tiedFighters[1].motion.Y;
    Check(tiedWorld.LastPlayerHeadTieTick == 1 && tiedFighters[0].alive && tiedFighters[1].alive &&
        worldDx * worldDx + worldDy * worldDy >= 1440L * 1440L, "world did not apply and report head tie separation");

    var world = new PveFixedWorld(42, false, null, strengthPreview: true);
    var actors = (PveFixedFighter[])field.GetValue(world);
    actors[0] = Fighter("you", -11700); actors[1] = Fighter("bot-1", 0);
    for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
    world.Step(0, false);
    Check(world.StrengthPreview && !world.Finished && actors[0].alive && actors[1].alive &&
        actors[0].motion.Length > 16 && actors[1].motion.Length < 16 && world.LastPlayerBiteTick == 1 &&
        !world.LastPlayerWasBitten && world.LastPlayerBiteCount > 0,
        "practice world did not apply and report a real body bite");
    var projected = world.RenderState();
    Check(projected.snakes[0].segments.Count == actors[0].motion.Length && projected.snakes[1].segments.Count == actors[1].motion.Length,
        "practice rendering did not reflect the actual body lengths");
    Check(world.TryGetStrength("you", out var shownAttack, out _) && world.TryGetStrength("bot-1", out _, out int shownTail) &&
        shownAttack.attack > shownTail && !world.TryGetStrength("missing", out _, out _), "preview displayed invalid strength values");
    var blockedWorld = new PveFixedWorld(42, false, null, strengthPreview: true);
    var blockedActors = (PveFixedFighter[])field.GetValue(blockedWorld);
    blockedActors[0] = new PveFixedFighter("you", new PveFixedMotion(-71100, 0, 0, 5));
    blockedActors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 0, 0, 80));
    for (int i = 2; i < blockedActors.Length; i++) blockedActors[i].alive = false;
    blockedWorld.Step(0, false);
    Check(blockedWorld.LastPlayerBlockedTick == 1 && blockedActors[0].alive && blockedActors[1].motion.Length == 80,
        "weak contact did not report insufficient strength without eliminating the player");
    var blockedHead = blockedActors[0].motion.Segments[0];
    long nearestBody = long.MaxValue;
    foreach (var part in blockedActors[1].motion.Segments.Skip(1))
    {
        long dx = (long)blockedHead.x - part.x, dy = (long)blockedHead.y - part.y;
        nearestBody = Math.Min(nearestBody, dx * dx + dy * dy);
    }
    Check(nearestBody >= 1116L * 1116L, "blocked head remained inside the body");
    var separate = typeof(PveFixedMotion).GetMethod("SeparateFrom", BindingFlags.Instance | BindingFlags.NonPublic);
    var overlapA = new PveFixedMotion(0, 0, 0, 16); var overlapB = new PveFixedMotion(0, 0, 0, 16);
    separate.Invoke(overlapA, new object[] { new CapturePoint(0, 0), new CapturePoint(1800, 0) });
    separate.Invoke(overlapB, new object[] { new CapturePoint(0, 0), new CapturePoint(1800, 0) });
    Check(overlapA.X == overlapB.X && overlapA.Y == overlapB.Y && overlapA.Angle == overlapB.Angle &&
        Math.Abs(overlapA.Y) >= 1116, "exact-overlap separation was not deterministic or clear of a straight body");
    var edge = new PveFixedMotion(PveFixedMotion.Edge, PveFixedMotion.Edge, 0, 16);
    separate.Invoke(edge, new object[] { new CapturePoint(PveFixedMotion.Edge, PveFixedMotion.Edge), new CapturePoint(1800, 0) });
    Check(Math.Abs(edge.X) <= PveFixedMotion.Edge && Math.Abs(edge.Y) <= PveFixedMotion.Edge &&
        Math.Abs(edge.Y - PveFixedMotion.Edge) >= 1116, "separation failed at the world boundary");
    foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
    {
        var point = new CapturePoint(sx * PveFixedMotion.Edge, sy * PveFixedMotion.Edge);
        var tangent = new CapturePoint(sx * 1800, sy * 1800);
        var a = new PveFixedMotion(point.x, point.y, 0, 16);
        var b = new PveFixedMotion(point.x, point.y, 0, 16);
        separate.Invoke(a, new object[] { point, tangent });
        separate.Invoke(b, new object[] { point, tangent });
        long dx = (long)a.X - point.x, dy = (long)a.Y - point.y;
        Check(dx * dx + dy * dy >= 1116L * 1116L && Math.Abs(a.X) <= PveFixedMotion.Edge && Math.Abs(a.Y) <= PveFixedMotion.Edge &&
            a.X == b.X && a.Y == b.Y && a.Angle == b.Angle, "diagonal corner contact was not separated deterministically");
    }
    var first = new PveFixedWorld(73, true, null, true); var second = new PveFixedWorld(73, true, null, true);
    for (int i = 0; i < 600 && !first.Finished; i++)
    {
        first.Step((byte)(i * 13), i % 30 > 20); second.Step((byte)(i * 13), i % 30 > 20);
        Check(first.Tick == second.Tick && first.Finished == second.Finished && first.Winner == second.Winner, "strength preview diverged for identical input");
        for (int j = 0; j < first.Fighters.Count; j++)
        {
            var a = first.Fighters[j]; var b = second.Fighters[j];
            Check(a.alive == b.alive && a.kills == b.kills && a.motion.Length == b.motion.Length && a.motion.X == b.motion.X && a.motion.Y == b.motion.Y &&
                first.StrengthLevel(a.id) == second.StrengthLevel(b.id), "strength preview fighter or level diverged for identical input");
            bool progressA = first.TryGetStrengthLevelProgress(a.id, out int levelA, out int xpA, out int neededA);
            bool progressB = second.TryGetStrengthLevelProgress(b.id, out int levelB, out int xpB, out int neededB);
            Check(progressA == progressB && levelA == levelB && xpA == xpB && neededA == neededB,
                "strength experience diverged for identical input");
        }
        Check(first.EventStatus == "", "old event leaked into strength practice");
    }
}

static void StrengthBodyCombatCases()
{
    PveFixedFighter Fighter(string id, int x, int y = 0, int length = 16) => new(id, new PveFixedMotion(x, y, 0, length));
    StrengthInputs Power(int value = 100, int hardness = 0) => new() { basePower = value, hardness = hardness,
        speed = 1000, baseSpeed = 1000, randomFactor = 1000, operationFactor = 1000 };
    var attacker = Fighter("attacker", -11700); var defender = Fighter("defender", 0);
    var combat = new StrengthBodyCombat();
    var bites = combat.Resolve(0, new[] { attacker, defender }, new[] { Power(), Power() });
    Check(bites.Count == 1 && bites[0].segment == 13 && bites[0].removed == 3 &&
        attacker.motion.Length == 19 && defender.motion.Length == 13 && attacker.alive && defender.alive,
        "strong head did not consume the actual contacted tail or grow by the removed amount");
    Check(combat.Resolve(1, new[] { attacker, defender }, new[] { Power(), Power() }).Count == 0, "pair cooldown did not prevent repeat bites");
    Check(bites.Count == 1 && bites[0].removed == 3, "later steps mutated a previously returned public result");
    bites = combat.Resolve(8, new[] { attacker, defender }, new[] { Power(), Power() });
    Check(bites.Count == 1 && bites[0].removed == 1 && defender.motion.Length == 12, "bite did not recover after cooldown");
    attacker = Fighter("attacker", -4500); defender = Fighter("defender", 0);
    bites = new StrengthBodyCombat().Resolve(0, new[] { attacker, defender }, new[] { Power(300), Power() });
    Check(bites.Count == 1 && bites[0].segment == 5 && bites[0].removed == 3, "middle-body bite did not remove contacted segments");
    for (int i = 1; i < defender.motion.Length; i++)
    {
        long dx = (long)defender.motion.Segments[i - 1].x - defender.motion.Segments[i].x;
        long dy = (long)defender.motion.Segments[i - 1].y - defender.motion.Segments[i].y;
        Check(dx * dx + dy * dy <= 902L * 902L, "middle bite left a long non-collidable body gap");
    }

    attacker = Fighter("attacker", -11700); defender = Fighter("defender", 0);
    combat = new StrengthBodyCombat();
    Check(combat.Resolve(0, new[] { attacker, defender }, new[] { Power(), Power(300, 300) }).Count == 0 && attacker.alive && defender.alive,
        "weaker head consumed a stronger body or died merely from touching it");
    Check(combat.BlockedAttackers.Contains("attacker"), "a real failed bite did not produce a strength feedback signal");
    defender.shieldTicks = 1;
    Check(combat.Resolve(1, new[] { attacker, defender }, new[] { Power(), Power() }).Count == 0, "protected body was eaten");
    Check(!combat.BlockedAttackers.Contains("attacker"), "protected contact was incorrectly reported as insufficient strength");
    defender.shieldTicks = 0; attacker.shieldTicks = 1;
    Check(combat.Resolve(2, new[] { attacker, defender }, new[] { Power(), Power() }).Count == 0, "protected attacker ate a body");
    attacker.shieldTicks = 0; defender.alive = false;
    Check(combat.Resolve(3, new[] { attacker, defender }, new[] { Power(), Power() }).Count == 0, "dead body was eaten");
    attacker = Fighter("attacker", -3600); defender = Fighter("defender", 0);
    Check(new StrengthBodyCombat().Resolve(0, new[] { attacker, defender }, new[] { Power(), Power(110) }).Count == 0,
        "a weaker head bypassed the nearest protected segment to eat a weaker neighboring segment");

    string Contest(bool reverse)
    {
        var a = Fighter("a", -11700); var b = Fighter("b", -11700); var target = Fighter("target", 0);
        var actors = reverse ? new[] { target, b, a } : new[] { a, b, target };
        var entries = new[] { Power(), Power(), Power() };
        var outcome = new StrengthBodyCombat().Resolve(0, actors, entries);
        Check(outcome.Count == 1 && target.motion.Length == 13, "simultaneous bites double-consumed a body");
        return outcome[0].attacker;
    }
    Check(Contest(false) == "a" && Contest(true) == "a", "tie ownership depended on list order");
    var weaker = Fighter("a", -11700); var stronger = Fighter("b", -11700); var victim = Fighter("target", 0);
    var contested = new StrengthBodyCombat().Resolve(0, new[] { weaker, stronger, victim }, new[] { Power(), Power(130), Power() });
    Check(contested.Count == 1 && contested[0].attacker == "b", "ID order beat a stronger simultaneous attack");
    var mutualA = Fighter("a", 0);
    var mutualB = new PveFixedFighter("b", new PveFixedMotion(-11700, 0, 32768, 16));
    var beforeA = mutualA.motion.Segments.ToArray(); var beforeB = mutualB.motion.Segments.ToArray();
    var mutual = new StrengthBodyCombat().Resolve(0, new[] { mutualA, mutualB }, new[] { Power(), Power() });
    Check(mutual.Count == 2 && mutualA.motion.Length == 16 && mutualB.motion.Length == 16 &&
        mutual.Any(x => x.attacker == "a" && x.defender == "b" && x.removed == 3) &&
        mutual.Any(x => x.attacker == "b" && x.defender == "a" && x.removed == 3),
        "simultaneous mutual bites did not use the same snapshot and apply both growth awards");
    foreach (var hit in mutual)
    {
        var expected = (hit.defender == "a" ? beforeA : beforeB)[hit.segment];
        Check(hit.point.x == expected.x && hit.point.y == expected.y, "bite effect position was not captured from the shared snapshot");
    }
    var tiny = Fighter("tiny", 0, length: 5); attacker = Fighter("attacker", -2700);
    Check(new StrengthBodyCombat().Resolve(0, new[] { attacker, tiny }, new[] { Power(), Power() }).Count == 0 && tiny.motion.Length == 5,
        "minimum body length was not preserved");
    bool rejected = false;
    try { combat.Resolve(3, new[] { attacker, defender }, new[] { Power(), Power() }); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "repeated tick was accepted");
}

static void BodyStrengthCases()
{
    var baseline = new StrengthInputs { basePower = 100, length = 16, speed = 1000, baseSpeed = 1000,
        randomFactor = 1000, operationFactor = 1000 };
    Check(BodyStrength.TryCalculate(baseline, 0, out var head) && head.attack == 100 && head.defense == 100, "neutral head calculation changed");
    Check(BodyStrength.TryCalculate(baseline, 15, out var tail) && tail.defense == 55 && BodyStrength.CanEat(head.attack, tail.defense), "equal base power could not eat the tail");
    Check(!BodyStrength.CanEat(105, 100) && BodyStrength.CanEat(106, 100) && !BodyStrength.CanEat(0, 0), "strict 5% boundary is wrong");
    for (int length = 1; length <= 80; length++)
    {
        baseline.length = length; int last = int.MaxValue;
        for (int i = 0; i < length; i++)
        {
            Check(BodyStrength.TryCalculate(baseline, i, out var body) && body.defense <= last, "body defense increased toward the tail");
            last = body.defense;
        }
    }
    baseline.length = 16; baseline.hardness = 300;
    Check(BodyStrength.TryCalculate(baseline, 15, out tail) && tail.defense == 85 && !BodyStrength.CanEat(80, tail.defense), "hardness did not protect the tail");
    baseline.boosting = true; baseline.speed = 1500;
    Check(BodyStrength.TryCalculate(baseline, 0, out var boost) && boost.speedFactor == 1120 && boost.attack == 112, "actual boost speed did not change attack");
    baseline.speed = 1000;
    Check(BodyStrength.TryCalculate(baseline, 0, out boost) && boost.speedFactor == 1000, "boost without extra speed changed attack");
    baseline.basePower = int.MaxValue; baseline.matchLevel = int.MaxValue; baseline.hardness = int.MaxValue;
    baseline.speed = int.MaxValue; baseline.baseSpeed = 1; baseline.randomFactor = int.MaxValue; baseline.operationFactor = int.MaxValue;
    Check(BodyStrength.TryCalculate(baseline, 15, out var bounded) && bounded.baseStrength == 300 && bounded.effectFactor == 1250 && bounded.attack <= 420, "extreme inputs escaped bounds");
    Check(!BodyStrength.TryCalculate(baseline, 16, out _) && !BodyStrength.TryCalculate(baseline, -1, out _), "invalid body segment accepted");
    baseline.baseSpeed = 0; Check(!BodyStrength.TryCalculate(baseline, 0, out _), "zero reference speed accepted");
    Check(BodyStrength.CanEat(int.MaxValue, 0) && !BodyStrength.CanEat(-1, 0), "comparison overflowed or accepted negative power");
}

static void CompletedPveRecordingCases()
{
    int eliminated = 0;
    foreach (int seed in new[] { 0, 1, 793, 12345 })
    foreach (bool firstRun in new[] { true, false })
    {
        var live = Arena.CreateStandardPve(seed, firstRun, "玩家");
        var tape = new List<byte>();
        for (int tick = 0; tick < PveRecording.MaxTicks && live.State.phase == "running"; tick++)
        {
            byte heading = (byte)(tick / 53 * 37 + seed);
            byte boost = (byte)(tick % 120 >= 95 ? 1 : 0);
            live.SetInput("you", PveRecording.Direction(heading), boost != 0);
            live.Tick(.05f); tape.Add(heading); tape.Add(boost);
            LocalPveRound.EndIfPlayerEliminated(live, "you");
        }
        Check(live.State.phase == "ended", "sampled local match did not end within recording limit");
        if (!live.Find("you").alive) eliminated++;
        Check(PveRecording.TryReplayCompleted(seed, firstRun, null, tape.ToArray(), out var replay), "completed local recording was rejected");
        Check(replay.State.tick == live.State.tick && replay.State.phase == live.State.phase && replay.State.winner == live.State.winner,
            "local replay produced a different terminal state");
        foreach (var a in live.State.snakes)
        {
            var b = replay.Find(a.id);
            Check(b != null && a.alive == b.alive && a.kills == b.kills && a.eliminationReason == b.eliminationReason &&
                a.eliminatedBy == b.eliminatedBy && a.segments.Count == b.segments.Count, "local replay changed an outcome");
            for (int i = 0; i < a.segments.Count; i++)
                Check(a.segments[i].x == b.segments[i].x && a.segments[i].y == b.segments[i].y, "local replay changed a trajectory");
        }
        Check(!PveRecording.TryReplayCompleted(seed, firstRun, null, tape.Take(tape.Count - 2).ToArray(), out _), "truncated local recording was accepted");
        tape.Add(0); tape.Add(0);
        Check(!PveRecording.TryReplayCompleted(seed, firstRun, null, tape.ToArray(), out _), "post-finish input was accepted");
    }
    Check(eliminated > 0, "samples did not exercise player elimination");
    Check(!PveRecording.TryReplayCompleted(1, false, null, null, out _), "missing input was accepted");
    Check(!PveRecording.TryReplayCompleted(1, false, null, new byte[] { 1, 2 }, out _), "invalid boost was accepted");
}

static void PveRecordingCases()
{
    var live = Arena.CreateStandardPve(793, false, "玩家");
    var tape = new List<byte>();
    for (int tick = 0; tick < 500 && live.State.phase == "running"; tick++)
    {
        byte heading = (byte)(tick / 75 * 40);
        byte boost = (byte)(tick % 100 >= 80 ? 1 : 0);
        live.SetInput("you", PveRecording.Direction(heading), boost != 0);
        live.Tick(.05f); tape.Add(heading); tape.Add(boost);
    }
    Check(PveRecording.TryReplay(793, false, null, tape.ToArray(), out var replay), "valid recording rejected");
    Check(replay.State.tick == live.State.tick && replay.State.phase == live.State.phase, "replay timeline differs");
    for (int i = 0; i < live.State.snakes.Count; i++)
    {
        var a = live.State.snakes[i]; var b = replay.State.snakes[i];
        Check(a.alive == b.alive && a.kills == b.kills && a.segments.Count == b.segments.Count, "replay outcome differs");
        for (int j = 0; j < a.segments.Count; j++)
            Check(a.segments[j].x == b.segments[j].x && a.segments[j].y == b.segments[j].y, "replay trajectory differs");
    }
    Check(!PveRecording.TryReplay(1, false, null, null, out _), "null recording accepted");
    Check(!PveRecording.TryReplay(1, false, null, new byte[] { 1 }, out _), "partial frame accepted");
    Check(!PveRecording.TryReplay(1, false, null, new byte[] { 1, 2 }, out _), "invalid boost accepted");
    Check(!PveRecording.TryReplay(1, false, null, new byte[(PveRecording.MaxTicks + 1) * 2], out _), "oversized recording accepted");
}

static void PveReplayFoundation()
{
    foreach (int seed in new[] { 0, 1, 12345, int.MaxValue })
    foreach (bool first in new[] { true, false })
    {
        var a = Arena.CreateStandardPve(seed, first, "玩家");
        var b = Arena.CreateStandardPve(seed, first, "玩家");
        Check(a.State.snakes.Count == 8 && a.State.remaining == (first ? 60f : 90f), "PvE setup differs from requested mode");
        for (int tick = 0; tick < 400 && a.State.phase == "running" && b.State.phase == "running"; tick++)
        {
            Point input = tick % 160 < 80 ? new Point(1, 0) : new Point(0, 1);
            bool boost = tick % 100 >= 80;
            a.SetInput("you", input, boost); b.SetInput("you", input, boost);
            a.Tick(.05f); b.Tick(.05f);
            Check(a.State.tick == b.State.tick && a.State.phase == b.State.phase && a.State.snakes.Count == b.State.snakes.Count, "matching input changed simulation structure");
            for (int i = 0; i < a.State.snakes.Count; i++)
            {
                var left = a.State.snakes[i]; var right = b.State.snakes[i];
                Check(left.alive == right.alive && left.kills == right.kills && left.segments.Count == right.segments.Count, "matching input changed an outcome");
                for (int j = 0; j < left.segments.Count; j++)
                    Check(left.segments[j].x == right.segments[j].x && left.segments[j].y == right.segments[j].y, "matching input changed a trajectory");
            }
        }
    }
}

static void ArenaBufferReuse()
{
    var arena = new Arena(123, fruitCount: 0, enableCaptures: true);
    var first = arena.AddSnake("first", "first", false);
    var second = arena.AddSnake("second", "second", false);
    first.shield = second.shield = 0;
    first.segments = new() { new(0, 0), new(-1, 0), new(-2, 0) };
    second.segments = new() { new(0, 0), new(1, 0), new(2, 0) };
    var resolve = typeof(Arena).GetMethod("ResolveCollisions", BindingFlags.Instance | BindingFlags.NonPublic);
    resolve.Invoke(arena, null);
    Check(!first.alive && !second.alive, "initial collision did not happen");
    first.alive = second.alive = true;
    second.segments = new() { new(40, 0), new(41, 0), new(42, 0) };
    resolve.Invoke(arena, null);
    Check(first.alive && second.alive, "previous collision leaked into the next resolution");

    var quiet = new Arena(124, roundSeconds: 60, fruitCount: 0, enableCaptures: true);
    for (int i = 0; i < 8; i++)
    {
        var snake = quiet.AddSnake("p" + i, "player", false);
        snake.shield = 0; snake.angle = 0;
        for (int j = 0; j < snake.segments.Count; j++) snake.segments[j] = new Point(-j * Arena.SegmentSpacing, -70 + i * 20);
        quiet.SetInput(snake.id, new Point(1, 0), false);
    }
    quiet.DisableStar(); quiet.Start();
    for (int i = 0; i < 20; i++) quiet.Tick(.05f);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 200; i++) quiet.Tick(.05f);
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(quiet.State.tick == 220 && bytes < 200L * 256, "ordinary arena ticks allocate too much or ended unexpectedly: " + bytes);
}

static void LocalCaptureResolution()
{
    var arena = new Arena(52, fruitCount: 0, enableCaptures: true);
    var owner = arena.AddSnake("you", "you", false);
    var first = arena.AddSnake("first", "first", true);
    var second = arena.AddSnake("second", "second", true);
    var protectedTarget = arena.AddSnake("protected", "protected", true);
    owner.shield = first.shield = second.shield = 0;
    protectedTarget.shield = 1;
    owner.segments = new List<Point> { new(3, 1), new(3, 2), new(3, 3), new(2, 4), new(1, 4), new(0, 4), new(0, 0), new(4, 0) };
    first.segments = new List<Point> { new(1, 1), new(1.1f, 1) };
    second.segments = new List<Point> { new(2, 2), new(2.1f, 2) };
    protectedTarget.segments = new List<Point> { new(1.5f, 1.5f), new(1.6f, 1.5f) };
    typeof(Arena).GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(arena, new object[] { new Dictionary<string, Point> { ["you"] = new Point(3, -1) } });
    Check(!first.alive && !second.alive && protectedTarget.alive, "local captures did not respect all victims and shields");
    Check(first.eliminationReason == "capture" && first.eliminatedBy == "you", "local capture cause not recorded");
    Check(owner.kills == 2 && owner.segments.Count == 10, "local captures did not immediately credit and grow the owner");
    Check(arena.State.captureOwner == "you" && arena.State.captureCount == 2, "local capture feedback differs from the resolved state");
    var highlights = new MatchHighlights(); highlights.Observe(arena.State, "you");
    Check(highlights.BestCapture == 2 && highlights.PeakLength == 10, "actual capture was not reflected in the highlight");
    first.alive = true;
    typeof(Arena).GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(arena, new object[] { new Dictionary<string, Point> { ["you"] = owner.segments[0] } });
    Check(first.alive && owner.kills == 2, "previous capture leaked into a later stationary tick");

    var mutual = new Arena(53, fruitCount: 0, enableCaptures: true);
    var left = mutual.AddSnake("you", "you", false); var right = mutual.AddSnake("bot", "bot", true);
    left.shield = right.shield = 0;
    left.segments = new List<Point> { new(3, 1), new(3, 2), new(3, 3), new(2, 4), new(1, 4), new(0, 4), new(0, 0), new(4, 0) };
    right.segments = new List<Point> { new(1, 1), new(1, 2), new(1, 3), new(2, 4), new(3, 4), new(4, 4), new(4, 0), new(0, 0) };
    typeof(Arena).GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(mutual, new object[] { new Dictionary<string, Point> { ["you"] = new(3, -1), ["bot"] = new(1, -1) } });
    Check(!left.alive && !right.alive && left.kills == 0 && right.kills == 0, "local mutual captures incorrectly credited a dead owner");
    Check(mutual.State.captureOwner == "you" && mutual.State.captureCount == 0 && mutual.State.capturePolygon.Count > 2,
        "local mutual capture displayed an uncredited elimination");
}

static void HighlightSummary()
{
    var highlights = new MatchHighlights();
    var world = new ArenaState();
    var player = new SnakeState { id = "you", segments = new List<Point> { new(), new(), new() } };
    world.snakes.Add(player);
    highlights.Observe(world, "you");
    Check(highlights.PeakLength == 3 && highlights.BestCapture == 0, "empty match produced a highlight");
    player.segments.Add(new Point()); player.segments.Add(new Point());
    world.captureTick = 1; world.captureOwner = "bot"; world.captureCount = 3;
    highlights.Observe(world, "you");
    Check(highlights.PeakLength == 5 && highlights.BestCapture == 0, "opponent's capture credited to player");
    world.captureTick = 2; world.captureOwner = "you"; world.captureCount = 2; player.alive = false;
    highlights.Observe(world, "you");
    Check(highlights.BestCapture == 0, "eliminated captor received a highlight");
    player.alive = true; world.captureTick = 3; world.captureCount = 2;
    highlights.Observe(world, "you");
    Check(highlights.BestCapture == 2, "player capture not retained");
    player.segments.RemoveAt(4); world.captureCount = 7; // Same event must not be processed twice.
    highlights.Observe(world, "you");
    Check(highlights.BestCapture == 2 && highlights.PeakLength == 5, "duplicate event or shrinking changed the high point");
    world.captureTick = 4; world.captureCount = 1; highlights.Observe(world, "you");
    Check(highlights.BestCapture == 2, "later smaller capture replaced best");
    highlights.Reset();
    Check(highlights.BestCapture == 0 && highlights.PeakLength == 0, "previous match leaked into next result");
}

static void SkinVisualFallback()
{
    var profile = new PlayerProgress { selectedSkin = 2, ownedSkins = new List<int> { 0, 2 } };
    bool Available(int index) => index == 0 || index == 1;
    Check(SkinCatalog.ResolveVisual(1, Available) == 1, "available visual unexpectedly changed");
    Check(SkinCatalog.ResolveVisual(profile.selectedSkin, Available) == 0, "missing owned visual did not use default");
    Check(SkinCatalog.ResolveVisual(4, Available) == 0, "missing reference did not use default");
    Check(SkinCatalog.ResolveVisual(100, Available) == 0 && SkinCatalog.ResolveVisual(-1, Available) == 0, "invalid index did not use default");
    Check(SkinCatalog.ResolveVisual(2, _ => false) == -1 && SkinCatalog.ResolveVisual(2, null) == -1, "missing default was reported as available");
    Check(profile.selectedSkin == 2 && profile.ownedSkins.SequenceEqual(new[] { 0, 2 }), "visual fallback changed saved ownership");
}

static void SkinIdentities()
{
    Check(SkinCatalog.OwnedSkinCount == 5 && SkinCatalog.IndexOf("lanlight") == 0 && SkinCatalog.IndexOf("crystal") == 3,
        "original inventory IDs changed");
    Check(SkinCatalog.IndexOf("reference-wenda") == 4 && SkinCatalog.Get(4).ReferencePreview && SkinCatalog.Get(5).ReferencePreview,
        "reference previews must remain distinct from owned skins");
    Check(SkinCatalog.IndexOf("tidal") == 24 && SkinCatalog.IsOwnedSkin(24) && !SkinCatalog.IsOwnedSkin(4), "new owned identity displaced a reference");
    for (int i = 0; i < SkinCatalog.All.Length; i++)
        Check(SkinCatalog.DisplayIndex(SkinCatalog.DisplayPosition(i)) == i, "display order did not preserve IDs");
    Check(SkinCatalog.IndexOf("missing") == -1, "unknown identity should not map to a skin");
}

static void DepthGoals()
{
    var profile = new PlayerProgress { tutorialComplete = true };
    Check(!DepthChallenge.IsUnlocked(profile) && DepthChallenge.CompletedMatches(profile) == 1, "old profile migration did not preserve tutorial progress");
    profile.completedMatches = 3;
    Check(DepthChallenge.IsUnlocked(profile), "depth did not unlock after three matches");
    var world = new ArenaState();
    var player = new SnakeState { id = "you", alive = true };
    var boss = new SnakeState { id = "bot-0", alive = true };
    world.snakes.Add(player); world.snakes.Add(boss);
    Check(!DepthChallenge.GoalReached(1, world, "you"), "empty first room completed");
    player.kills = 1; Check(DepthChallenge.GoalReached(1, world, "you"), "first room kill not recognized");
    player.starPoints = 1; Check(DepthChallenge.GoalReached(2, world, "you"), "star room not recognized");
    world.phase = "running"; world.remaining = 20;
    Check(DepthChallenge.Evaluate(3, world, "you") == DepthChallenge.RoomStatus.InProgress, "living boss ended room early");
    boss.alive = false; boss.eliminatedBy = "another-bot";
    Check(!DepthChallenge.GoalReached(3, world, "you"), "another AI's kill incorrectly completed the boss room");
    Check(DepthChallenge.Evaluate(3, world, "you") == DepthChallenge.RoomStatus.Lost && DepthChallenge.FailureReason(3, world, "you").Contains("其他对手"), "unreachable boss objective was not ended and explained");
    boss.eliminatedBy = "you"; Check(DepthChallenge.GoalReached(3, world, "you") && DepthChallenge.Evaluate(3, world, "you") == DepthChallenge.RoomStatus.Won, "player's boss kill not recognized");
    player.alive = false; Check(!DepthChallenge.GoalReached(3, world, "you") && DepthChallenge.Evaluate(3, world, "you") == DepthChallenge.RoomStatus.Lost, "dead player completed the room");
    player.alive = true; boss.alive = true; world.phase = "ended"; world.remaining = 0;
    Check(DepthChallenge.Evaluate(3, world, "you") == DepthChallenge.RoomStatus.Lost && DepthChallenge.FailureReason(3, world, "you").Contains("时间到"), "time limit failure not explained");
}

static void DepthRoomFlow()
{
    string summary = DepthChallenge.BonusSummary(2, 1, 2);
    Check(summary.Contains("护盾 4 秒") && summary.Contains("加速 3 秒") && summary.Contains("增长 6 节"), "bonus summary disagrees with active effects");
    Check(DepthChallenge.BonusChoice(0, 1, 0, 0).Contains("2 → 4 秒"), "stacked capture shield preview is incorrect");
    Check(DepthChallenge.BonusChoice(1, 0, 0, 0).Contains("0 → 3 秒"), "rebound preview is incorrect");
    Check(DepthChallenge.BonusChoice(2, 0, 0, 2).Contains("6 → 6 节"), "capped growth preview is incorrect");
    Check(DepthChallenge.GrowthBoostPenalty(0) == 0 && DepthChallenge.GrowthBoostPenalty(1) == .25f && DepthChallenge.GrowthBoostPenalty(2) == .5f && DepthChallenge.GrowthBoostPenalty(99) == .5f && DepthChallenge.GrowthBoostPenalty(-1) == 0, "growth boost cost did not respect its bounds");
    Check(DepthChallenge.BonusChoice(2, 0, 0, 0).Contains("加速消耗 +0% → +25%") && DepthChallenge.BonusChoice(2, 0, 0, 1).Contains("加速消耗 +25% → +50%"), "growth choice did not disclose its cost");
    Check(summary.Contains("加速消耗 +50%"), "build summary omitted the boost cost");
    string capped = DepthChallenge.BonusSummary(int.MaxValue, -1, int.MaxValue);
    Check(capped.Contains("护盾 4 秒") && capped.Contains("加速 0 秒") && capped.Contains("增长 6 节"), "bonus summary did not enforce limits");
    var hintWorld = new ArenaState { phase = "running" };
    Check(DepthChallenge.ObjectiveHint(2, hintWorld, "you").Contains("即将出现"), "depth star waiting hint missing");
    hintWorld.starVisible = true;
    Check(DepthChallenge.ObjectiveHint(2, hintWorld, "you").Contains("自动拾取"), "depth star pickup hint missing");
    hintWorld.starHolder = "bot";
    Check(DepthChallenge.ObjectiveHint(2, hintWorld, "you").Contains("对手"), "depth opposing carrier hint missing");
    hintWorld.starHolder = "you"; hintWorld.starProgress = .25f;
    Check(DepthChallenge.ObjectiveHint(2, hintWorld, "you").Contains("8 秒"), "depth star hint used the wrong holding duration");
    var hintBoss = new SnakeState { id = "boss", alive = true, boss = true, bossShield = 3 };
    hintWorld.snakes.Add(hintBoss);
    Check(DepthChallenge.ObjectiveHint(3, hintWorld, "you").Contains("护盾 3") && !DepthChallenge.ObjectiveHint(3, hintWorld, "you").Contains("0秒"), "depth boss showed a nonexistent event timer");
    hintBoss.bossWindup = .5f;
    Check(DepthChallenge.ObjectiveHint(3, hintWorld, "you").Contains("蓄力"), "depth boss windup not shown");
    hintWorld.phase = "ended";
    Check(DepthChallenge.ObjectiveHint(2, hintWorld, "you") == "", "depth hint persisted after the room ended");
    var noStar = new Arena(3, fruitCount: 0);
    var player = noStar.AddSnake("you", "you", false); var bot = noStar.AddSnake("bot", "bot", true);
    player.shield = bot.shield = 100;
    noStar.DisableStar(); noStar.Start();
    for (int i = 0; i < 370; i++) noStar.Tick(.1f);
    Check(noStar.State.phase == "running" && !noStar.State.starVisible, "disabled star appeared in a non-star room");
    var starRoom = new Arena(4, fruitCount: 0);
    player = starRoom.AddSnake("you", "you", false); bot = starRoom.AddSnake("bot", "bot", true);
    player.shield = bot.shield = 100;
    starRoom.ConfigureStar(5, player.id); starRoom.Start();
    starRoom.DisableStar(); // Configuration cannot be changed during a match.
    for (int i = 0; i < 52; i++) starRoom.Tick(.1f);
    Check(starRoom.State.starVisible, "star room lost its configured objective");
}

static void DepthBonuses()
{
    int BoostLength(float penalty)
    {
        var trial = new Arena(19, fruitCount: 0, enableCaptures: true);
        var snake = trial.AddSnake("you", "you", false);
        var dummy = trial.AddSnake("other", "other", false);
        snake.shield = dummy.shield = 100;
        trial.SetBonuses(snake.id, new CombatBonuses { boostCostPenalty = penalty });
        trial.Start(); trial.SetInput(snake.id, new Point(1, 0), true);
        for (int i = 0; i < 200; i++) trial.Tick(.05f);
        return snake.segments.Count;
    }
    int normalLength = BoostLength(0), higherLength = BoostLength(.25f), highestLength = BoostLength(.5f);
    Check(normalLength > higherLength && higherLength > highestLength, "growth penalty did not increase paid boost consumption");
    var arena = new Arena(23, fruitCount: 0);
    var victim = arena.AddSnake("victim", "victim", false);
    var hunter = arena.AddSnake("hunter", "hunter", false);
    void Place(SnakeState snake, Point head, Point direction)
    {
        snake.shield = 0; snake.angle = (float)Math.Atan2(direction.y, direction.x); snake.segments.Clear();
        for (int i = 0; i < Arena.StartingLength; i++) snake.segments.Add(head - direction * (i * Arena.SegmentSpacing));
        arena.SetInput(snake.id, direction, false);
    }
    Place(victim, new Point(3, 0), new Point(0, 1)); Place(hunter, new Point(0, 0), new Point(-1, 0));
    arena.SetBonuses(hunter.id, new CombatBonuses { bonusGrowthPerKill = 3 });
    arena.Start(); arena.Tick(.01f);
    Check(!victim.alive && hunter.kills == 1 && hunter.segments.Count == 15, "depth growth bonus did not apply to a real elimination");

    arena = new Arena(24, fruitCount: 0, enableCaptures: true);
    var player = arena.AddSnake("you", "you", false);
    var other = arena.AddSnake("other", "other", false);
    Place(player, new Point(109.2f, 0), new Point(1, 0)); Place(other, new Point(-80, -80), new Point(1, 0));
    arena.SetStartingLength(player.id, 16);
    arena.SetBonuses(player.id, new CombatBonuses { reboundFreeSeconds = 3, boostCostPenalty = .5f }); arena.Start();
    arena.SetInput(player.id, new Point(1, 0), false); arena.Tick(.05f);
    Check(player.reboundBoostReady && !player.reboundBoostWaitRelease && player.reboundBoostTime == 0, "rebound did not store a charge");
    Check(DepthChallenge.ReboundBoostHint(player).Contains("下次加速"), "stored rebound charge was not explained");
    arena.SetInput(player.id, new Point(-1, 0), false);
    for (int i = 0; i < 100; i++) arena.Tick(.05f);
    Check(player.reboundBoostReady && player.segments.Count == 16, "unused rebound charge expired before the next boost");
    arena.SetInput(player.id, new Point(-1, 0), true);
    for (int i = 0; i < 48; i++) arena.Tick(.05f);
    Check(player.alive && !player.reboundBoostReady && player.reboundBoostTime > 0 && player.segments.Count == 16, "charged boost consumed trail during the free window");
    Check(DepthChallenge.ReboundBoostHint(player).Contains("免费加速中"), "active free burst was not explained");
    arena.SetInput(player.id, new Point(-1, 0), false); arena.Tick(.05f);
    Check(player.reboundBoostTime == 0 && !player.reboundBoostReady, "released burst retained its unused free time");
    arena.SetInput(player.id, new Point(-1, 0), true);
    for (int i = 0; i < 55; i++) arena.Tick(.05f);
    Check(player.segments.Count < 16, "free charge incorrectly applied to a second burst");

    arena = new Arena(26, fruitCount: 0, enableCaptures: true);
    player = arena.AddSnake("you", "you", false); other = arena.AddSnake("other", "other", false);
    Place(player, new Point(109.2f, 0), new Point(1, 0)); Place(other, new Point(-80, -80), new Point(1, 0));
    arena.SetStartingLength(player.id, 16); arena.SetBonuses(player.id, new CombatBonuses { reboundFreeSeconds = 3 }); arena.Start();
    arena.SetInput(player.id, new Point(1, 0), true); arena.Tick(.05f);
    Check(player.reboundBoostReady && player.reboundBoostWaitRelease && player.reboundBoostTime == 0, "held boost used the newly earned charge");
    Check(DepthChallenge.ReboundBoostHint(player).Contains("松开"), "required release was not explained");
    arena.SetInput(player.id, new Point(-1, 0), true);
    for (int i = 0; i < 55; i++) arena.Tick(.05f);
    int afterPaidBurst = player.segments.Count;
    Check(afterPaidBurst < 16 && player.reboundBoostReady && player.reboundBoostWaitRelease, "holding boost gained free time without releasing");
    arena.SetInput(player.id, new Point(-1, 0), false); arena.Tick(.05f);
    Check(player.reboundBoostReady && !player.reboundBoostWaitRelease, "releasing boost did not arm the charge");
    arena.SetInput(player.id, new Point(-1, 0), true);
    for (int i = 0; i < 48; i++) arena.Tick(.05f);
    Check(player.segments.Count == afterPaidBurst && player.reboundBoostTime > 0, "next burst did not use the stored charge");
    for (int i = 0; i < 14; i++) arena.Tick(.05f);
    Check(player.reboundBoostTime == 0 && !player.reboundBoostReady, "free burst exceeded its three-second cap");

    arena = new Arena(27, fruitCount: 0, enableCaptures: true);
    player = arena.AddSnake("you", "you", false); other = arena.AddSnake("other", "other", false);
    Place(player, new Point(109.2f, 0), new Point(1, 0)); Place(other, new Point(-80, -80), new Point(1, 0));
    arena.SetStartingLength(player.id, 16); arena.Start(); arena.Tick(.05f);
    Check(!player.reboundBoostReady && player.reboundBoostTime == 0, "rebound charge leaked into a new room without the bonus");
    Check(DepthChallenge.ReboundBoostHint(player) == "", "inactive charge left a HUD hint");

    arena = new Arena(25, fruitCount: 0, enableCaptures: true);
    player = arena.AddSnake("you", "you", false); other = arena.AddSnake("other", "other", false);
    player.shield = other.shield = 0; player.segments.Clear(); other.segments.Clear();
    foreach (var point in new[] { new Point(3, 1), new Point(3, 2), new Point(3, 3), new Point(2, 4),
        new Point(1, 4), new Point(0, 4), new Point(0, 0), new Point(4, 0) }) player.segments.Add(point);
    other.segments.Add(new Point(1, 1)); other.segments.Add(new Point(1.1f, 1));
    arena.SetBonuses(player.id, new CombatBonuses { captureShieldSeconds = 2 }); arena.Start();
    var heads = new Dictionary<string, Point> { [player.id] = new Point(3, -1), [other.id] = other.segments[0] };
    typeof(Arena).GetMethod("ResolveCaptures", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, new object[] { heads });
    Check(!other.alive && player.shield == 2 && player.kills == 1, "capture shield did not activate after a real capture");
}

static void DepthBoss()
{
    var arena = new Arena(74, fruitCount: 0);
    var player = arena.AddSnake("you", "you", false);
    var boss = arena.AddSnake("bot-0", "boss", true);
    void Place(SnakeState snake, Point head, Point direction)
    {
        snake.shield = 0; snake.angle = (float)Math.Atan2(direction.y, direction.x); snake.segments.Clear();
        for (int i = 0; i < Arena.StartingLength; i++) snake.segments.Add(head - direction * (i * Arena.SegmentSpacing));
        arena.SetInput(snake.id, direction, false);
    }
    Place(player, new Point(0, 0), new Point(-1, 0)); Place(boss, new Point(-40, -40), new Point(0, 1));
    arena.ConfigureBoss(boss.id); arena.Start();
    bool telegraphed = false, charged = false;
    for (int i = 0; i < 105 && player.alive && boss.alive; i++)
    {
        arena.Tick(.05f); telegraphed |= boss.bossWindup > 0; charged |= boss.bossRushing;
    }
    Check(telegraphed && charged, "boss did not visibly wind up and charge");
    Check(boss.bossShield == 3, "boss shield changed without a hit");

    void Hit()
    {
        Place(player, new Point(0, 0), new Point(-1, 0)); Place(boss, new Point(3, 0), new Point(0, 1));
        arena.Tick(.01f);
    }
    void WaitCooldown()
    {
        Place(player, new Point(0, 0), new Point(-1, 0)); Place(boss, new Point(-40, -40), new Point(0, 1));
        for (int i = 0; i < 22; i++) arena.Tick(.05f);
    }
    Hit(); Check(boss.alive && boss.bossShield == 2 && player.kills == 0, "first hit must reduce shield, not eliminate");
    Hit(); Check(boss.alive && boss.bossShield == 2, "hit cooldown did not prevent repeated shield damage");
    WaitCooldown(); Hit(); Check(boss.alive && boss.bossShield == 1, "second shield hit did not register");
    WaitCooldown(); Hit(); Check(!boss.alive && boss.bossShield == 0 && boss.eliminatedBy == player.id && player.kills == 1,
        "final shield hit did not eliminate the boss and credit the player");
}

sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;
    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) { this.respond = respond; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}
