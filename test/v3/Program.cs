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
    ("first seven active days grow linearly and later growth slows", GrowthCurve),
    ("talent points, rank limits and PvE/PvP caps are enforced", TalentBudget),
    ("recommended talents allocate available points deterministically", TalentRecommendation),
    ("expedition rewards respect time and bonus limits", ExpeditionRewards),
    ("expedition fragments unlock stories without spending inventory", CaveAlbum),
    ("wall bounce grants a brief pulse with a cooldown", BouncePulse),
    ("daily replay reproduces states and published score formula", DailyReplay),
    ("daily challenges use their own seed and are bounded", DailySeeds),
    ("legacy daily replays preserve their original result", LegacyDailyReplay),
    ("daily-2 recordings remain compatible with daily-3", PreviousDailyReplay),
    ("drag steering works at any screen position and tracks one finger", FullScreenDrag),
    ("drag steering recovers missing touch ends and transfers control", TouchRecovery),
    ("music respects interaction, suspension and bounded loop samples", MusicBehavior),
    ("pending settlement retries back off and stay bound to their account", PendingRetries),
    ("danger cue follows live, unshielded collision hazards", NearbyDanger),
    ("capture geometry is deterministic and includes its boundary", CaptureGeometryCases),
    ("a starting-length player can actually close a loop", CapturePlayable),
    ("closed loops eliminate unshielded targets and grow the survivor", CaptureResolution),
    ("local arena captures multiple opponents without defeating protected targets", LocalCaptureResolution),
    ("match highlights preserve only real, credited capture peaks", HighlightSummary),
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

static void ExpeditionRewards()
{
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
    Check((int)snakeType.GetField("kills").GetValue(snakes[0]) == 0 && (int)snakeType.GetField("kills").GetValue(snakes[1]) == 0,
        "an eliminated captor incorrectly received growth");
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
    Check(owner.kills == 2 && owner.segments.Count == 10, "local captures did not immediately credit and grow the owner");
    Check(arena.State.captureOwner == "you" && arena.State.captureCount == 2, "local capture feedback differs from the resolved state");
    var highlights = new MatchHighlights(); highlights.Observe(arena.State, "you");
    Check(highlights.BestCapture == 2 && highlights.PeakLength == 10, "actual capture was not reflected in the highlight");
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
    boss.alive = false; boss.eliminatedBy = "another-bot";
    Check(!DepthChallenge.GoalReached(3, world, "you"), "another AI's kill incorrectly completed the boss room");
    boss.eliminatedBy = "you"; Check(DepthChallenge.GoalReached(3, world, "you"), "player's boss kill not recognized");
    player.alive = false; Check(!DepthChallenge.GoalReached(3, world, "you"), "dead player completed the room");
}

static void DepthRoomFlow()
{
    string summary = DepthChallenge.BonusSummary(2, 1, 2);
    Check(summary.Contains("护盾 4 秒") && summary.Contains("加速 3 秒") && summary.Contains("增长 6 节"), "bonus summary disagrees with active effects");
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
