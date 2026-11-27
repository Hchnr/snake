using SnakeClub.Core;

int samples = int.Parse(args[0]);
Console.WriteLine("v16; scripted nearest fruit/tail every 8 ticks, no boost; not human play");
foreach (bool first in new[] { false, true })
foreach (int policy in new[] { 0, 1 })
{
    int used = 0, observedAfter = 0, repeat = 0, max = 0;
    var cases = new List<string>();
    for (int seed = 1; seed <= samples; seed++)
    {
        var result = Evaluate(seed, first, policy);
        if (result.used)
        {
            used++;
            if (result.tick > result.started + PveFixedWorld.SqueezeEscapeDurationTicks) observedAfter++;
            cases.Add($"{seed}:{result.started}:{result.tick}:{result.longestAfter}");
        }
        if (result.longestAfter >= 4) repeat++;
        max = Math.Max(max, result.longestAfter);
    }
    Console.WriteLine($"first={first} policy={(policy == 0 ? "fruit" : "tail")} seeds={samples} " +
        $"used={used} observed_after={observedAfter} after_streak_ge4={repeat} max_after={max} " +
        $"cases=seed:activation:end:max_after[{string.Join(",", cases)}]");
}
static Result Evaluate(int seed, bool firstRun, int policy)
{
    var world = new PveFixedWorld(seed, firstRun, null, strengthPreview: true,
        localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
        localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
        localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true,
        localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: true,
        localOpeningBoost: true, localFreeBoost: true, localOpeningDefense: true, localOpeningFruit: true);
    byte heading = 0;
    int consecutive = 0, longest = 0, blocks = 0, after = 0, longestAfter = 0;
    while (!world.Finished)
    {
        if (world.Tick % 8 == 0)
        {
            var player = world.Fighters[0].motion;
            long nearest = long.MaxValue;
            int dx = 0, dy = 0;
            if (policy == 0)
            {
                foreach (var fruit in world.Fruit)
                {
                    int x = fruit.position.x - player.X, y = fruit.position.y - player.Y;
                    long distance = (long)x * x + (long)y * y;
                    if (distance < nearest) { nearest = distance; dx = x; dy = y; }
                }
            }
            else
            {
                foreach (var fighter in world.Fighters)
                {
                    if (fighter.id == "you" || !fighter.alive || fighter.motion.Length <= PveFixedMotion.MinimumLength) continue;
                    var tail = fighter.motion.Segments[fighter.motion.Length - 1];
                    int x = tail.x - player.X, y = tail.y - player.Y;
                    long distance = (long)x * x + (long)y * y;
                    if (distance < nearest) { nearest = distance; dx = x; dy = y; }
                }
            }
            if (nearest < long.MaxValue)
                heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
        }
        world.Step(heading, false, true);
        if (world.LastPlayerBlockedTick == world.Tick)
        {
            blocks++; consecutive++; longest = Math.Max(longest, consecutive);
            if (world.SqueezeEscapeUsed && world.SqueezeEscapeTicks == 0)
            { after++; longestAfter = Math.Max(longestAfter, after); }
        }
        else { consecutive = 0; after = 0; }
    }
    return new Result(world.Tick, world.Fighters[0].alive, world.SqueezeEscapeUsed,
        world.LastSqueezeEscapeTick, longest, blocks, world.PlayerSegmentsEaten, world.PlayerSegmentsLostToBites, longestAfter);
}

readonly record struct Result(int tick, bool alive, bool used, int started, int longest, int blocks, int eaten, int lost, int longestAfter);
