using SnakeClub.Core;

int samples = int.Parse(args[0]);
Console.WriteLine($"v9/v10; seeds=1..{samples}; nearest fruit/tail, reselect every 8 ticks, no boost");
Console.WriteLine("Scripted strategies are not human play. Outcomes after activation may diverge, including RNG and AI decisions.");
foreach (bool firstRun in new[] { false, true })
foreach (int policy in new[] { 0, 1 })
{
    int activations = 0, baselineDead = 0, candidateDead = 0;
    int baselineLong = 0, candidateLong = 0, improved = 0, worsened = 0;
    int baselineMaximum = 0, candidateMaximum = 0, activationsBeforeEnd = 0;
    long baselineBlocks = 0, candidateBlocks = 0, baselineEaten = 0, candidateEaten = 0, baselineLost = 0, candidateLost = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        Result baseline = Evaluate(seed, firstRun, policy, false);
        Result candidate = Evaluate(seed, firstRun, policy, true);
        if (candidate.used) activations++;
        if (candidate.used && candidate.tick > candidate.started + PveFixedWorld.SqueezeEscapeDurationTicks) activationsBeforeEnd++;
        if (!baseline.alive) baselineDead++;
        if (!candidate.alive) candidateDead++;
        if (baseline.longest >= 4) baselineLong++;
        if (candidate.longest >= 4) candidateLong++;
        if (candidate.longest < baseline.longest) improved++;
        if (candidate.longest > baseline.longest) worsened++;
        baselineMaximum = Math.Max(baselineMaximum, baseline.longest);
        candidateMaximum = Math.Max(candidateMaximum, candidate.longest);
        baselineBlocks += baseline.blocks; candidateBlocks += candidate.blocks;
        baselineEaten += baseline.eaten; candidateEaten += candidate.eaten;
        baselineLost += baseline.lost; candidateLost += candidate.lost;
        if (seed == 35 && firstRun && policy == 1)
            Console.WriteLine($"seed35 baseline_tick={baseline.tick} baseline_alive={baseline.alive} baseline_longest={baseline.longest} " +
                $"v10_tick={candidate.tick} v10_alive={candidate.alive} v10_longest={candidate.longest} activation_tick={candidate.started}");
    }
    Console.WriteLine($"first={firstRun} policy={(policy == 0 ? "nearest-fruit" : "nearest-tail")} samples={samples} " +
        $"activated={activations} ran_beyond_protection={activationsBeforeEnd} " +
        $"dead_v9={baselineDead} dead_v10={candidateDead} longest_ge4_v9={baselineLong} longest_ge4_v10={candidateLong} " +
        $"max_streak_v9={baselineMaximum} max_streak_v10={candidateMaximum} paired_shorter={improved} paired_longer={worsened} " +
        $"blocks_v9={baselineBlocks} blocks_v10={candidateBlocks} eaten_v9={baselineEaten} eaten_v10={candidateEaten} " +
        $"lost_v9={baselineLost} lost_v10={candidateLost}");
}

static Result Evaluate(int seed, bool firstRun, int policy, bool escape)
{
    var world = new PveFixedWorld(seed, firstRun, null, strengthPreview: true,
        localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
        localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
        localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true,
        localSqueezeEscape: escape);
    byte heading = 0;
    int consecutive = 0, longest = 0, blocks = 0;
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
        }
        else consecutive = 0;
    }
    return new Result(world.Tick, world.Fighters[0].alive, world.SqueezeEscapeUsed,
        world.LastSqueezeEscapeTick, longest, blocks, world.PlayerSegmentsEaten, world.PlayerSegmentsLostToBites);
}

readonly record struct Result(int tick, bool alive, bool used, int started, int longest, int blocks, int eaten, int lost);
