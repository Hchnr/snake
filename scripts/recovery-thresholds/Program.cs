using SnakeClub.Core;

int sampleCount = int.Parse(args[1]);
if (args[0] == "trace")
{
    Trace(sampleCount);
    return;
}

foreach (bool firstRun in new[] { false, true })
foreach (int policy in new[] { 0, 1 })
foreach (int maxLength in new[] { 16, 20, 24, 999 })
{
    int hitSamples = 0, firstTriggered = 0, everTriggered = 0;
    int playerCollectedCases = 0, aiCollectedCases = 0, playerFruit = 0, aiFruit = 0;
    int restored = 0, deaths = 0, censored = 0, observed = 0;
    long postTicks = 0, lost = 0, eaten = 0, growth = 0;
#if EXPERIMENTAL
    PveFixedWorld.RecoverySupplyLengthLimit = maxLength;
#else
    throw new InvalidOperationException("Evaluation requires isolated candidate core");
#endif
    for (int seed = 1; seed <= sampleCount; seed++)
    {
        var world = new PveFixedWorld(seed, firstRun, null, true, localAdvantage: true,
            localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true,
            localSafeRecovery: true, localAlignedStart: true, localCappedStart: true,
            localStableBlocked: true);
        byte heading = 0;
        int hitTick = -1, lengthBeforeHit = 0, lengthAfterHit = 0;
        int lostAtHit = 0, eatenAtHit = 0;
        bool recovered = false;
        while (!world.Finished && (hitTick < 0 ? world.Tick < 300 : world.Tick < hitTick + 200))
        {
            var player = world.Fighters[0].motion;
            if (world.Tick % 8 == 0)
            {
                long closest = long.MaxValue;
                int directionX = 0, directionY = 0;
                if (policy == 0)
                {
                    foreach (var fruit in world.Fruit)
                    {
                        int x = fruit.position.x - player.X, y = fruit.position.y - player.Y;
                        long distance = (long)x * x + (long)y * y;
                        if (distance < closest) { closest = distance; directionX = x; directionY = y; }
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
                        if (distance < closest) { closest = distance; directionX = x; directionY = y; }
                    }
                }
                if (closest < long.MaxValue)
                    heading = (byte)(((int)Math.Round(Math.Atan2(directionY, directionX) * 128 / Math.PI) + 256) & 255);
            }

            int priorLost = world.PlayerSegmentsLostToBites;
            int lengthBeforeStep = player.Length;
            world.Step(heading, false, true);
            if (hitTick < 0 && world.PlayerSegmentsLostToBites > priorLost)
            {
                hitTick = world.Tick;
                hitSamples++;
                lengthBeforeHit = lengthBeforeStep;
                lengthAfterHit = player.Length;
                lostAtHit = world.PlayerSegmentsLostToBites;
                eatenAtHit = world.PlayerSegmentsEaten;
                if (world.LastRecoverySupplyTick == world.Tick) firstTriggered++;
            }
            if (hitTick >= 0 && player.Length >= lengthBeforeHit && world.Fighters[0].alive) recovered = true;
        }
        if (hitTick < 0) continue;

        Console.WriteLine($"ROW {firstRun} {policy} {maxLength} {seed} {hitTick} {world.Tick - hitTick} {world.Fighters[0].alive} {recovered} {world.PlayerSegmentsLostToBites - lostAtHit} {world.PlayerSegmentsEaten - eatenAtHit} {world.RecoverySupplyCollectedByPlayer} {world.RecoverySupplyCollectedByOpponents} {world.Fighters[0].motion.Length - lengthAfterHit}");
        if (world.RecoverySuppliesUsed > 0) everTriggered++;
        if (world.RecoverySupplyCollectedByPlayer > 0) playerCollectedCases++;
        if (world.RecoverySupplyCollectedByOpponents > 0) aiCollectedCases++;
        playerFruit += world.RecoverySupplyCollectedByPlayer;
        aiFruit += world.RecoverySupplyCollectedByOpponents;
        if (recovered) restored++;
        if (!world.Fighters[0].alive) deaths++;
        int followTicks = world.Tick - hitTick;
        postTicks += followTicks;
        if (followTicks < 200) censored++; else observed++;
        lost += world.PlayerSegmentsLostToBites - lostAtHit;
        eaten += world.PlayerSegmentsEaten - eatenAtHit;
        growth += world.Fighters[0].motion.Length - lengthAfterHit;
    }
    Console.WriteLine($"first={firstRun} policy={policy} threshold={maxLength} hitSamples={hitSamples} firstTrigger={firstTriggered} anyTrigger={everTriggered} playerCollectedCases={playerCollectedCases} playerCollectedFruit={playerFruit} aiCollectedCases={aiCollectedCases} aiCollectedFruit={aiFruit} restored={restored} dead={deaths} full10s={observed} censored={censored} avgFollowTicks={(double)postTicks / hitSamples:F1} postLost={lost} postEaten={eaten} sumFinalLengthChange={growth}");
}

// Trace current input and a broad snapshot each tick; the helper adds selected
// hidden fields. This is a deterministic regression fingerprint, not proof of
// equivalence for all inputs or a cryptographic integrity check.
static void Trace(int samples)
{
    foreach (bool firstRun in new[] { false, true })
    foreach (int policy in new[] { 0, 1 })
    for (int seed = 1; seed <= samples; seed++)
    {
        var world = new PveFixedWorld(seed, firstRun, null, true, localAdvantage: true,
            localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true,
            localSafeRecovery: true, localAlignedStart: true, localCappedStart: true,
            localStableBlocked: true);
        byte heading = 0;
        while (!world.Finished)
        {
            var player = world.Fighters[0].motion;
            if (policy == 0 && world.Tick % 8 == 0)
            {
                long closest = long.MaxValue;
                int directionX = 0, directionY = 0;
                foreach (var fruit in world.Fruit)
                {
                    int x = fruit.position.x - player.X, y = fruit.position.y - player.Y;
                    long distance = (long)x * x + (long)y * y;
                    if (distance < closest) { closest = distance; directionX = x; directionY = y; }
                }
                heading = (byte)(((int)Math.Round(Math.Atan2(directionY, directionX) * 128 / Math.PI) + 256) & 255);
            }
            if (policy == 1) heading = (byte)((world.Tick / 53 * 37 + seed) & 255);
            world.Step(heading, false, true);
            ulong hash = 14695981039346656037UL;
            void Add(int value)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++)
                    {
                        hash ^= (byte)(value >> (8 * i));
                        hash *= 1099511628211UL;
                    }
                }
            }
            Add(world.Tick); Add(heading); Add(world.Finished ? 1 : 0);
            Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
            Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerBlockedTick);
            Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
            Add(world.RecoverySuppliesUsed); Add(world.LastRecoverySupplyTick); Add(world.NextRecoverySupplyTick);
            Add(world.RecoverySupplyCollectedByPlayer); Add(world.RecoverySupplyCollectedByOpponents);
            foreach (var fighter in world.Fighters)
            {
                Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks);
                Add(fighter.motion.X); Add(fighter.motion.Y); Add(fighter.motion.Angle); Add(fighter.motion.Length);
                foreach (var part in fighter.motion.Segments) { Add(part.x); Add(part.y); }
            }
            for (int i = 0; i < world.Fruit.Count; i++)
            {
                Add(world.Fruit[i].position.x); Add(world.Fruit[i].position.y);
                Add(world.Fruit[i].color); Add(world.IsRecoveryFruit(i) ? 1 : 0);
            }
            var hidden = SnakeClub.Tests.StrengthPveReplayVector.HiddenFingerprint(world);
            var terminal = world.Finished ? StrengthStateDigest.Compute(world) : "-";
            Console.WriteLine($"{firstRun} {policy} {seed} {world.Tick} {hash:x16} {hidden} {terminal} {world.RecoverySuppliesUsed} {world.RecoverySupplyCollectedByPlayer} {world.RecoverySupplyCollectedByOpponents}");
        }
    }
}
