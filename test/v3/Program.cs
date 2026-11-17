using System.Text.Json;
using System.Collections;
using System.Reflection;
using SnakeClub.Core;

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
    ("drag steering works at any screen position and tracks one finger", FullScreenDrag),
    ("drag steering recovers missing touch ends and transfers control", TouchRecovery),
    ("audio feedback rebases after interruption without replaying old events", AudioFeedbackCursorCases),
    ("music respects interaction, suspension and bounded loop samples", MusicBehavior),
    ("settlement retry delays do not block other runs or accounts", IndependentSettlementRetries),
    ("pending settlement retries back off and stay bound to their account", PendingRetries),
    ("danger cue follows live, unshielded collision hazards", NearbyDanger),
    ("result celebration only follows a real win and respects reduced effects", ResultMotionCases),
    ("character motion follows real turns, boost and reduced effects", CharacterMotionCases),
    ("elimination transitions require a real displayed life and reset cleanly", EliminationTransitionCases),
    ("match outcomes explain actual collision and capture results", MatchOutcomeCases),
    ("capture sweep follows even-odd geometry for concave and crossing loops", CaptureSweepCases),
    ("capture geometry is deterministic and includes its boundary", CaptureGeometryCases),
    ("a starting-length player can actually close a loop", CapturePlayable),
    ("closed loops eliminate unshielded targets and grow the survivor", CaptureResolution),
    ("local arena reuses collision buffers without stale outcomes", ArenaBufferReuse),
    ("local arena captures multiple opponents without defeating protected targets", LocalCaptureResolution),
    ("match highlights preserve only real, credited capture peaks", HighlightSummary),
    ("off-screen objective markers follow real targets and screen direction", EventTargetIndicatorCases),
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
    var home = MusicComposer.Create(false); var battle = MusicComposer.Create(true);
    Check(home.Length == MusicComposer.SampleRate * MusicComposer.Seconds && battle.Length == home.Length, "incorrect loop duration");
    Check(home[0] == 0 && Math.Abs(home[^1]) < .01 && battle[0] == 0 && Math.Abs(battle[^1]) < .01, "loop boundary is not faded");
    Check(home.All(x => float.IsFinite(x) && Math.Abs(x) < .4f) && home.Any(x => Math.Abs(x) > .05f), "home loop is clipped or silent");
    Check(battle.All(x => float.IsFinite(x) && Math.Abs(x) < .4f) && battle.Any(x => Math.Abs(x) > .05f), "battle loop is clipped or silent");
    Check(!home.SequenceEqual(battle), "home and battle loops are identical");
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
    player.shield = 0; other.alive = false;
    Check(!DangerProbe.Find(state, "you", out _), "dead opponent incorrectly indicated danger");
    other.alive = true; other.segments[2] = new Point(30, 0); other.segments[0] = new Point(3, 0);
    Check(DangerProbe.Find(state, "you", out cue) && cue.x == 3, "dangerous opposing head was missed");
    other.segments.RemoveAt(3);
    other.segments.RemoveAt(2);
    Check(!DangerProbe.Find(state, "you", out _), "shorter opposing head incorrectly indicated danger");
    other.segments[0] = new Point(30, 0); other.segments[1] = new Point(1, 0);
    Check(!DangerProbe.Find(state, "you", out _), "non-colliding neck incorrectly indicated danger");
    state.phase = "ended"; other.segments.Add(new Point(1, 0));
    Check(!DangerProbe.Find(state, "you", out _), "ended match retained danger warning");
}

static void FirstRunHintsCases()
{
    Check(FirstRunHints.Select(39, false, false, false, 0) == "", "hint appeared before two seconds");
    Check(FirstRunHints.Select(40, false, false, false, 0).Contains("拖动"), "movement hint was missing");
    Check(FirstRunHints.Select(400, false, false, false, 0).Contains("拖动"), "timer advanced the tutorial without steering");
    Check(FirstRunHints.Select(100, true, false, false, 0) == "", "movement hint did not disappear after steering");
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
    var wrapped = new CharacterMotion();
    wrapped.Update(0, 3.13f, false, .05f, false, true);
    wrapped.Update(1, -3.13f, false, .05f, false, true);
    Check(wrapped.LeanDegrees < 0 && wrapped.LeanDegrees > -2, "angle wrapping generated a false large turn");
    wrapped.Update(2, 0, true, .05f, false, false);
    for (int i = 0; i < 30; i++) wrapped.Update(2, 0, true, .05f, false, false);
    Check(Math.Abs(wrapped.LeanDegrees) < .01f && Math.Abs(wrapped.Scale - 1) < .001f, "paused motion did not settle");
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
}

static void EventTargetIndicatorCases()
{
    var world = new ArenaState { phase = "running", eventStatus = "active", eventKind = "bounty", eventTarget = "bot" };
    var bot = new SnakeState { id = "bot", segments = new List<Point> { new(12, -4) } }; world.snakes.Add(bot);
    Check(EventTargetIndicator.TryTarget(world, "you", out var target, out var name) && target.x == 12 && target.y == -4 && name == "悬赏", "bounty target not shown");
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
    Check(SkinCatalog.OwnedSkinCount == 4 && SkinCatalog.IndexOf("lanlight") == 0 && SkinCatalog.IndexOf("crystal") == 3,
        "original inventory IDs changed");
    Check(SkinCatalog.IndexOf("reference-wenda") == 4 && SkinCatalog.Get(4).ReferencePreview && SkinCatalog.Get(5).ReferencePreview,
        "reference previews must remain distinct from owned skins");
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
    string capped = DepthChallenge.BonusSummary(int.MaxValue, -1, int.MaxValue);
    Check(capped.Contains("护盾 4 秒") && capped.Contains("加速 0 秒") && capped.Contains("增长 6 节"), "bonus summary did not enforce limits");
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
    arena.SetBonuses(player.id, new CombatBonuses { reboundFreeSeconds = 3 }); arena.Start();
    arena.SetInput(player.id, new Point(1, 0), true); arena.Tick(.05f);
    arena.SetInput(player.id, new Point(-1, 0), true);
    for (int i = 0; i < 48; i++) arena.Tick(.05f);
    Check(player.alive && player.segments.Count == 16, "rebound boost consumed trail during the free window");

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
