using System.Text.Json;
using SnakeClub.Core;

if (args.Length >= 2 && args[0] == "replay")
{
    int seed = int.Parse(args[1]);
    int variant = args.Length > 2 ? int.Parse(args[2]) : 0;
    var match = new DailyMatch(seed);
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
    ("expedition rewards respect time and bonus limits", ExpeditionRewards),
    ("wall bounce grants a brief pulse with a cooldown", BouncePulse),
    ("daily replay reproduces states and published score formula", DailyReplay),
    ("daily challenges use their own seed and are bounded", DailySeeds)
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
        int peak = Arena.StartingLength;
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
        int expected = 10 * (peak - Arena.StartingLength) + 100 * me.kills + a.State.tick / 20 + 30 * me.starPoints;
        Check(a.Finished && b.Finished && a.Score == expected, "daily score formula or completion incorrect");
        Check(a.State.tick <= DailyMatch.MaxTicks && a.Score >= 0 && a.Score <= DailyMatch.MaxScore, "daily bounds exceeded");
    }
}
static void DailySeeds()
{
    var a = new DailyMatch(12345); var b = new DailyMatch(12346);
    Check(a.State.snakes.Count == 8 && a.State.fruits.Count == 200, "daily setup incorrect");
    Check(Point.Distance(a.State.snakes[0].segments[0], b.State.snakes[0].segments[0]) > .01f, "different seeds produced identical spawn");
    Check(a.State.snakes[0].segments.Count == 8 && !a.State.snakes[0].bot && a.State.snakes.Skip(1).All(s => s.bot), "daily player/AI setup incorrect");
}
