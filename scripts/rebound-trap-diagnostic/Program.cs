using SnakeClub.Core;

// Diagnostic only. Every branch constructs the same real v9 world and uses
// the same player policy through tick 464. Inputs are never injected as state.
const int branchStart = 464, firstObserved = 465, lastObserved = 478;
string commonFingerprint = null;
for (int branch = -1; branch < 8; branch++)
{
    var world = new PveFixedWorld(35, true, null, strengthPreview: true,
        localAdvantage: true, localOpeningFood: true, localOpeningEncounter: true,
        localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
        localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true);
    if (world.RulesVersion != PveFixedRecording.LocalStrengthRecoveryPriorityRules)
        throw new InvalidOperationException("This diagnostic must run the isolated v9 rules.");
    byte heading = 0;
    int blocked = 0, consecutive = 0, maxConsecutive = 0;
    while (!world.Finished && world.Tick < 510)
    {
        if (world.Tick % 8 == 0) heading = NearestTail(world, heading);
        if (world.Tick == branchStart)
        {
            string fingerprint = Fingerprint(world);
            if (commonFingerprint == null) commonFingerprint = fingerprint;
            else if (commonFingerprint != fingerprint)
                throw new InvalidOperationException("Branches differed in the selected state fingerprint at tick 464.");
        }
        if (branch >= 0 && world.Tick >= branchStart) heading = (byte)(branch * 32);
        world.Step(heading, false, true);
        if (world.Tick >= firstObserved && world.Tick <= lastObserved)
        {
            if (world.LastPlayerBlockedTick == world.Tick)
            {
                blocked++;
                consecutive++;
                maxConsecutive = Math.Max(maxConsecutive, consecutive);
            }
            else consecutive = 0;
        }
    }
    string label = branch < 0 ? "nearest-tail" : "fixed-" + (branch * 32);
    Console.WriteLine($"{label}: tick={world.Tick} alive={world.Fighters[0].alive} " +
        $"blocks_465_to_478={blocked} longest_streak={maxConsecutive} " +
        $"segments_lost_total={world.PlayerSegmentsLostToBites}");
    if (blocked != 14 || maxConsecutive != 14)
        throw new InvalidOperationException($"The documented 14-step trap changed for {label}; inspect the new behavior.");
}
Console.WriteLine($"REPRODUCED KNOWN ISSUE: v9 seed 35; common selected tick-464 fingerprint {commonFingerprint}; " +
    "all eight fixed directions and nearest-tail had 14 consecutive blocked ticks.");

static byte NearestTail(PveFixedWorld world, byte previous)
{
    var player = world.Fighters[0].motion;
    long closest = long.MaxValue;
    int dx = 0, dy = 0;
    foreach (var fighter in world.Fighters)
    {
        if (fighter.id == "you" || !fighter.alive || fighter.motion.Length <= PveFixedMotion.MinimumLength) continue;
        var tail = fighter.motion.Segments[fighter.motion.Length - 1];
        int x = tail.x - player.X, y = tail.y - player.Y;
        long distance = (long)x * x + (long)y * y;
        if (distance < closest) { closest = distance; dx = x; dy = y; }
    }
    return closest == long.MaxValue ? previous :
        (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
}

static string Fingerprint(PveFixedWorld world)
{
    ulong hash = 14695981039346656037UL;
    void Add(int value)
    {
        unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (8 * i)); hash *= 1099511628211UL; } }
    }
    Add(world.Tick);
    world.AppendReplayDiagnostics(Add);
    foreach (var fighter in world.Fighters)
    {
        Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks); Add(fighter.motion.Angle); Add(fighter.motion.Length);
        foreach (var segment in fighter.motion.Segments) { Add(segment.x); Add(segment.y); }
    }
    for (int i = 0; i < world.Fruit.Count; i++)
    {
        Add(world.Fruit[i].position.x); Add(world.Fruit[i].position.y); Add(world.Fruit[i].color);
        Add(world.IsRecoveryFruit(i) ? 1 : 0);
    }
    return hash.ToString("x16");
}
