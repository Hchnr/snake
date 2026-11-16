using SnakeClub.Core;

var cases = new (string name, Action run)[]
{
    ("fruit grows the snake and respawns", Fruit),
    ("wall reflects movement without killing the snake", Wall),
    ("hitting another snake rewards the survivor", BodyCollision),
    ("head-on collisions compare lengths", HeadCollision),
    ("spawn shields prevent collisions", Shield),
    ("boost costs length but stops at the minimum", Boost),
    ("round timer finishes and chooses the longest survivor", Timer),
    ("seeded simulations stay deterministic", Deterministic)
};
foreach (var test in cases)
{
    try { test.run(); Console.WriteLine("PASS " + test.name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + test.name + ": " + ex.Message); Environment.ExitCode = 1; }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Place(Arena arena, SnakeState snake, Point head, Point forward, int length = Arena.StartingLength)
{
    snake.angle = (float)Math.Atan2(forward.y, forward.x);
    snake.shield = 0;
    snake.segments.Clear();
    for (int i = 0; i < length; i++) snake.segments.Add(head - forward * (i * Arena.SegmentSpacing));
    arena.SetInput(snake.id, forward, false);
}

static (Arena, SnakeState, SnakeState) Two(float halfSize = 110, float seconds = 180)
{
    var arena = new Arena(42, halfSize, seconds, 0);
    var a = arena.AddSnake("a", "A", false);
    var b = arena.AddSnake("b", "B", false);
    Place(arena, a, new Point(0, 0), new Point(1, 0));
    Place(arena, b, new Point(-30, -30), new Point(1, 0));
    arena.Start();
    return (arena, a, b);
}

static void Fruit()
{
    var (arena, a, _) = Two();
    arena.State.fruits.Add(new FruitState { position = new Point(.2f, 0), color = 1 });
    arena.Tick(.05f);
    Check(a.segments.Count == Arena.StartingLength + 1, "fruit did not add a segment");
    Check(arena.State.fruits.Count == 1 && Point.Distance(arena.State.fruits[0].position, a.segments[0]) > 1.25f, "fruit did not respawn");
}

static void Wall()
{
    var (arena, a, b) = Two(10);
    Place(arena, a, new Point(9.2f, 0), new Point(1, 0));
    Place(arena, b, new Point(-4, -4), new Point(0, 1));
    arena.Tick(.05f);
    Check(a.alive && a.segments[0].x <= 10 - Arena.CollisionRadius, "snake died or crossed the wall");
    Check(Math.Cos(a.angle) < 0, "heading did not reflect");
    float bouncedX = a.segments[0].x;
    arena.Tick(.05f);
    Check(a.segments[0].x < bouncedX, "snake did not move back into the arena");
}

static void BodyCollision()
{
    var (arena, a, b) = Two();
    Place(arena, a, new Point(3, 0), new Point(0, 1));
    Place(arena, b, new Point(0, 0), new Point(-1, 0));
    arena.Tick(.01f);
    Check(!a.alive && b.alive, "head/body collision produced the wrong survivor");
    Check(b.kills == 1 && b.segments.Count == 12, "winner did not receive the kill and growth");
}

static void HeadCollision()
{
    var (arena, a, b) = Two();
    Place(arena, a, new Point(0, 0), new Point(1, 0), 8);
    Place(arena, b, new Point(1, 0), new Point(1, 0), 10);
    arena.Tick(.01f);
    Check(!a.alive && b.alive && b.kills == 1, "longer snake should win a head-on collision");

    (arena, a, b) = Two();
    Place(arena, a, new Point(0, 0), new Point(1, 0));
    Place(arena, b, new Point(1, 0), new Point(1, 0));
    arena.Tick(.01f);
    Check(!a.alive && !b.alive && arena.State.winner == "", "equal head-on collision should eliminate both");
}

static void Shield()
{
    var (arena, a, b) = Two();
    Place(arena, a, new Point(0, 0), new Point(1, 0));
    Place(arena, b, new Point(1, 0), new Point(1, 0));
    a.shield = 1;
    arena.Tick(.01f);
    Check(a.alive && b.alive, "spawn shield did not prevent collision");
}

static void Boost()
{
    var (arena, a, _) = Two();
    arena.SetInput(a.id, new Point(1, 0), true);
    for (int i = 0; i < 250; i++) arena.Tick(.05f);
    Check(a.alive && a.segments.Count == Arena.MinimumLength && !a.boosting, "boost must consume length and stop at the minimum");
}

static void Timer()
{
    var (arena, a, b) = Two(seconds: .05f);
    Place(arena, b, new Point(-30, -30), new Point(1, 0), 10);
    arena.Tick(.05f);
    Check(arena.State.phase == "ended" && arena.State.winner == b.id, "timer did not select the longest survivor");
}

static void Deterministic()
{
    var a = new Arena(123);
    var b = new Arena(123);
    for (int i = 0; i < 5; i++) { a.AddSnake("s" + i, "AI", true); b.AddSnake("s" + i, "AI", true); }
    a.Start(); b.Start();
    for (int i = 0; i < 400; i++) { a.Tick(.05f); b.Tick(.05f); }
    for (int i = 0; i < a.State.snakes.Count; i++)
    {
        var x = a.State.snakes[i]; var y = b.State.snakes[i];
        Check(x.alive == y.alive && x.segments.Count == y.segments.Count && Point.Distance(x.segments[0], y.segments[0]) < .0001f, "seeded simulation diverged");
    }
}
