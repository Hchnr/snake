using System;
using System.Collections.Generic;
using System.Text;
using SnakeClub.Core;

namespace SnakeClub.Tests
{
    // Mutable, non-authoritative prototype diagnostic, not a release ruleset.
    // Hashes observable state and selected hidden simulation state every step;
    // arbitrary seeds, inputs and platforms still need independent verification.
    public static class StrengthPveReplayVector
    {
        private static ulong hash;
        private static void Add(int value)
        {
            unchecked { for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; } }
        }
        private static void Add(string value)
        {
            if (value == null) { Add(-1); return; }
            Add(value.Length); foreach (char c in value) Add(c);
        }
        // Test helper for detecting changes in the diagnostic's hidden inputs.
        public static string HiddenFingerprint(PveFixedWorld world)
        {
            hash = 14695981039346656037UL;
            world.AppendReplayDiagnostics(Add);
            return hash.ToString("x16");
        }
        // Fingerprint of observable terminal state only, used by diagnostics.
        public static string Terminal(PveFixedWorld world) => StrengthStateDigest.Compute(world);
        public static string Compute()
        {
            var result = new StringBuilder("strength=");
            foreach (int seed in new[] { 1, 2, 73 })
            foreach (bool first in new[] { false, true })
            for (int pattern = 0; pattern < 3; pattern++)
            {
                hash = 14695981039346656037UL;
                // Include the clipped-normal corner case in both runtimes,
                // independently of whether these sampled matches reach a wall.
                foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
                {
                    var corner = new CapturePoint(sx * PveFixedMotion.Edge, sy * PveFixedMotion.Edge);
                    var motion = new PveFixedMotion(corner.x, corner.y, 0, 16);
                    motion.SeparateFrom(corner, new CapturePoint(sx * 1800, sy * 1800));
                    Add(motion.X); Add(motion.Y); Add(motion.Angle);
                }
                var snapshot = pattern == 2 ? StrengthEvalStartConfig.Capture(new PlayerProgress { experience = 700, tutorialComplete = !first }) : null;
                var world = new PveFixedWorld(seed, first, null, strengthPreview: true, strengthSnapshot: snapshot);
                int flags = 0;
                while (!world.Finished)
                {
                    int tick = world.Tick;
                    byte heading = pattern == 0 ? (byte)0 : pattern == 1 ? (byte)(tick / 53 * 37 + seed) : (byte)(tick * 17);
                    bool boost = pattern == 1 ? tick % 120 >= 95 : pattern == 2 && tick % 30 < 15;
                    world.Step(heading, boost);
                    world.AppendReplayDiagnostics(Add);
                    Add(world.Tick); Add(world.Finished ? 1 : 0); Add(world.Winner);
                    Add(world.StrengthPickupVisible ? 1 : 0); Add(world.StrengthPickupPending ? 1 : 0); Add(world.StrengthPickupPendingTicks);
                    Add(world.StrengthPickupPosition.x); Add(world.StrengthPickupPosition.y);
                    Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerDamageTick); Add(world.LastPlayerDamageCount);
                    Add(world.LastPlayerBlockedTick); Add(world.LastPlayerHeadTieTick);
                    Add(world.LastPlayerLevelUpTick); Add(world.LastPlayerLevelUp);
                    Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
                    Add(world.LastPlayerAttackPoint.x); Add(world.LastPlayerAttackPoint.y);
                    Add(world.LastPlayerDamagePoint.x); Add(world.LastPlayerDamagePoint.y);
                    if (world.StrengthPickupVisible) flags |= 1;
                    if (world.LastPlayerAttackTick >= 0 || world.LastPlayerDamageTick >= 0) flags |= 2;
                    foreach (var fighter in world.Fighters)
                    {
                        Add(fighter.id); Add(fighter.alive ? 1 : 0); Add(fighter.shieldTicks); Add(fighter.kills);
                        Add(fighter.eliminatedBy); Add(fighter.eliminationReason); Add(fighter.motion.Angle);
                        Add(fighter.motion.Length); Add(fighter.motion.ReboundTicks); Add(fighter.motion.ReboundCooldown);
                        Add(fighter.motion.Boosting ? 1 : 0); Add(world.StrengthBuffTicks(fighter.id)); Add(world.StrengthLoopTicks(fighter.id));
                        bool hasLevel = world.TryGetStrengthLevelProgress(fighter.id, out int level, out int xp, out int required);
                        Add(hasLevel ? 1 : 0); Add(level); Add(xp); Add(required);
                        bool hasPower = world.TryGetStrength(fighter.id, out var power, out int tail);
                        Add(hasPower ? 1 : 0); Add(power.attack); Add(power.defense); Add(tail);
                        if (world.StrengthBuffTicks(fighter.id) > 0) flags |= 4;
                        if (world.StrengthLoopTicks(fighter.id) > 0) flags |= 8;
                        if (level > 0) flags |= 16;
                        foreach (var part in fighter.motion.Segments) { Add(part.x); Add(part.y); }
                    }
                    foreach (var fruit in world.Fruit) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
                }
                if (result.Length > 9) result.Append(',');
                result.Append(seed).Append(':').Append(first ? 1 : 0).Append(':').Append(pattern).Append(':')
                    .Append(world.Tick).Append(':').Append(flags).Append(':').Append(hash.ToString("x16"));
            }
            return result.ToString();
        }

        // Separate local-rule vectors preserve the existing server matrix.
        public static string ComputeRecovery() => ComputeRecovery(false);

        public static string ComputeSafeRecovery() => ComputeRecovery(true);

        public static string ComputeAlignedStart() => ComputeRecovery(true, true);

        public static string ComputeCappedStart() => ComputeRecovery(true, true, true);

        public static string ComputeStableBlocked() => ComputeRecovery(true, true, true, true) + "|" + StableBlockedFixture();

        public static string ComputeRecoveryPriority() => ComputeRecovery(true, true, true, true, true) + "|" + RecoveryPriorityFixture();

        public static string ComputeSqueezeEscape() => ComputeRecovery(true, true, true, true, true, true) + "|" + SqueezeEscapeFixture();

        public static string ComputeMultiBite() => ComputeRecovery(true, true, true, true, true, true, true) + "|" + MultiBiteFixture();

        private static string ComputeRecovery(bool safe, bool aligned = false, bool capped = false, bool stableBlocked = false, bool recoveryPriority = false, bool squeezeEscape = false, bool multiBite = false)
        {
            string prefix = multiBite ? "multi-bite=" : squeezeEscape ? "squeeze-escape=" : recoveryPriority ? "priority-recovery=" : stableBlocked ? "stable-blocked=" : capped ? "capped-start=" : aligned ? "aligned-start=" : safe ? "safe-recovery=" : "recovery=";
            var result = new StringBuilder(prefix);
            foreach (int seed in squeezeEscape ? new[] { 2, 9, 35, 68, 92, 105, 192 } : recoveryPriority ? new[] { 2, 9, 68, 92, 105, 192 } : aligned ? new[] { 2, 68, 92, 105, 192 } : safe ? new[] { 2, 68, 105, 192 } : new[] { 2, 68, 73, 105 })
            foreach (bool first in new[] { false, true })
            for (int pattern = 0; pattern < (recoveryPriority ? 8 : capped ? 7 : aligned ? 4 : safe ? 3 : 2); pattern++)
            {
                hash = 14695981039346656037UL;
                var world = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true,
                    localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: safe, localAlignedStart: aligned,
                    localCappedStart: capped, localStableBlocked: stableBlocked, localRecoveryPriority: recoveryPriority, localSqueezeEscape: squeezeEscape, localMultiBite: multiBite);
                int flags = 0; byte heading = pattern == 1 ? (byte)64 : (byte)0;
                while (!world.Finished)
                {
                    if (pattern == 3) heading = world.Tick < 10 ? (byte)0 : (byte)64;
                    if (capped && pattern == 4) heading = 128;
                    if (capped && pattern == 5) heading = world.Tick < 30 ? (byte)0 : (byte)128;
                    if (capped && pattern == 6) heading = world.Tick < 35 ? (byte)129 : (byte)32;
                    if ((pattern == 2 || pattern == 7) && world.Tick % 8 == 0)
                    {
                        var player = world.Fighters[0].motion;
                        long nearest = long.MaxValue; int dx = 0, dy = 0;
                        foreach (var opponent in world.Fighters)
                        {
                            if (opponent.id == "you" || !opponent.alive || opponent.motion.Length <= PveFixedMotion.MinimumLength) continue;
                            var tail = opponent.motion.Segments[opponent.motion.Length - 1];
                            int x = tail.x - player.X, y = tail.y - player.Y; long distance = (long)x * x + (long)y * y;
                            if (distance < nearest) { nearest = distance; dx = x; dy = y; }
                        }
                        if (nearest < long.MaxValue)
                        {
                            long best = long.MinValue;
                            for (int i = 0; i < 256; i++)
                            {
                                long dot = (long)dx * PveFixedMotion.DirectionX((byte)i) + (long)dy * PveFixedMotion.DirectionY((byte)i);
                                if (dot > best) { best = dot; heading = (byte)i; }
                            }
                        }
                    }
                    // v6 includes actual steering intent in the local tape;
                    // exercise no input, an immediate turn and a later turn.
                    bool steered = aligned && (pattern == 1 || pattern == 3 || pattern == 7 || (pattern == 2 && world.Tick >= 6) ||
                        (capped && (pattern == 4 || pattern == 6 || (pattern == 5 && world.Tick >= 30))));
                    world.Step(heading, false, steered);
                    world.AppendReplayDiagnostics(Add);
                    Add(world.Tick); Add(world.Finished ? 1 : 0); Add(world.Winner);
                    Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
                    foreach (var actor in world.Fighters)
                    {
                        Add(actor.id); Add(actor.alive ? 1 : 0); Add(actor.shieldTicks); Add(actor.kills);
                        Add(actor.motion.Angle); Add(actor.motion.Length); Add(actor.motion.Boosting ? 1 : 0);
                        foreach (var segment in actor.motion.Segments) { Add(segment.x); Add(segment.y); }
                    }
                    foreach (var fruit in world.Fruit) { Add(fruit.position.x); Add(fruit.position.y); Add(fruit.color); }
                    if (world.RecoverySuppliesUsed > 0) flags |= 1;
                    if (world.RecoverySupplyCollectedByPlayer > 0) flags |= 2;
                    if (world.RecoverySupplyCollectedByOpponents > 0) flags |= 4;
                    if (world.RecoverySuppliesUsed > 1) flags |= 8;
                    if (capped && world.OpeningTurnAssistUsed) flags |= 16;
                    if (squeezeEscape && world.SqueezeEscapeUsed) flags |= 64;
                    if (squeezeEscape && world.SqueezeEscapeTicks > 0) flags |= 128;
                    if (squeezeEscape && world.SqueezeEscapeUsed && world.SqueezeEscapeTicks == 0) flags |= 256;
                    if (recoveryPriority) for (int i = 0; i < world.Fruit.Count; i++)
                        if (world.RecoveryPriorityTicks(i) > 0) { flags |= 32; break; }
                }
                if (result.Length > prefix.Length) result.Append(',');
                result.Append(seed).Append(':').Append(first ? 1 : 0).Append(':').Append(pattern).Append(':')
                    .Append(world.Tick).Append(':').Append(flags).Append(':').Append(hash.ToString("x16"));
            }
            return result.ToString();
        }

        private static string MultiBiteFixture()
        {
            hash = 14695981039346656037UL;
            // True world Step: two AIs contact distinct segments of the player in one tick.
            var world = new PveFixedWorld(1, false, null, true, localAdvantage:true, localOpeningFood:true, localOpeningEncounter:true,
                localRecoverySupply:true, localSafeRecovery:true, localAlignedStart:true, localCappedStart:true,
                localStableBlocked:true, localRecoveryPriority:true, localSqueezeEscape:true, localMultiBite:true);
            var actors = (PveFixedFighter[])world.Fighters;
            actors[0] = new PveFixedFighter("you", new PveFixedMotion(0,0,0,16));
            actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-4500,400,16384,80));
            actors[2] = new PveFixedFighter("bot-2", new PveFixedMotion(-11700,-400,49152,80));
            for(int i=3;i<actors.Length;i++) actors[i].alive=false;
            for (int step=0;step<24 && !world.Finished;step++) {
                world.Step((byte)(step*13),false,true);
                if (step==0 && (world.LastPlayerDamageTick!=1 || world.LastPlayerDamageCount!=6 || world.PlayerSegmentsLostToBites!=6 || !world.LastPlayerWasBitten || world.LastPlayerBiteCount!=6)) throw new InvalidOperationException("multi-damage fixture failed");
                world.AppendReplayDiagnostics(Add);Add(world.Tick);Add(world.LastPlayerDamageTick);Add(world.LastPlayerDamageCount);Add(world.LastPlayerBiteCount);Add(world.LastPlayerWasBitten?1:0);Add(world.PlayerSegmentsLostToBites);
                foreach(var fighter in world.Fighters) { Add(fighter.id);Add(fighter.alive?1:0);Add(fighter.motion.X);Add(fighter.motion.Y);Add(fighter.motion.Angle);Add(fighter.motion.Length);foreach(var part in fighter.motion.Segments){Add(part.x);Add(part.y);} }
            }
            // Resolve identical disjoint bites twice, reversing input array order.
            foreach(bool reverse in new[]{false,true}) {
                var a=new PveFixedFighter("a",new PveFixedMotion(-4500,800,16384,16));
                var b=new PveFixedFighter("b",new PveFixedMotion(-11700,-800,49152,16));
                var t=new PveFixedFighter("target",new PveFixedMotion(0,0,0,16));
                var fighters=reverse?new[]{t,b,a}:new[]{a,b,t};
                var inputs=new StrengthInputs[fighters.Length];
                for(int k=0;k<fighters.Length;k++) inputs[k]=new StrengthInputs {basePower=fighters[k].id=="target"?100:300,speed=1000,baseSpeed=1000,randomFactor=1000,operationFactor=1000};
                var bites=new StrengthBodyCombat().ResolveBuffered(0,fighters,inputs,false,true);
                if(bites.Count!=2 || t.motion.Length!=10) throw new InvalidOperationException("separate bites fixture failed");
                foreach(var bite in bites) {Add(bite.attacker);Add(bite.defender);Add(bite.segment);Add(bite.removed);}
                foreach(var fighter in new[]{a,b,t}) {Add(fighter.motion.Length);foreach(var part in fighter.motion.Segments){Add(part.x);Add(part.y);}}
            }
            // Mutual bites preserve both snapshot decisions and award each attacker.
            var ma=new PveFixedFighter("a",new PveFixedMotion(0,0,0,16));
            var mb=new PveFixedFighter("b",new PveFixedMotion(-11700,0,32768,16));
            var power=new StrengthInputs {basePower=100,speed=1000,baseSpeed=1000,randomFactor=1000,operationFactor=1000};
            var mutual=new StrengthBodyCombat().ResolveBuffered(0,new[]{ma,mb},new[]{power,power},false,true);
            if(mutual.Count!=2 || ma.motion.Length!=16 || mb.motion.Length!=16) throw new InvalidOperationException("mutual bite fixture failed");
            foreach(var bite in mutual){Add(bite.attacker);Add(bite.defender);Add(bite.segment);Add(bite.removed);}
            foreach(var fighter in new[]{ma,mb})foreach(var part in fighter.motion.Segments){Add(part.x);Add(part.y);}
            return "multi-fixture="+hash.ToString("x16");
        }


        // Real seed-35 match with a deterministic script. The one-shot
        // protection must trigger on real repeated blocks, expire, and not
        // allow the protected player to participate in body combat.
        private static string SqueezeEscapeFixture()
        {
            var world = new PveFixedWorld(35, true, null, true, localAdvantage: true, localOpeningFood: true,
                localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true,
                localAlignedStart: true, localCappedStart: true, localStableBlocked: true,
                localRecoveryPriority: true, localSqueezeEscape: true);
            byte heading = 0; int previousRemaining = 0, activeTicks = 0, starts = 0;
            bool ended = false, expiryOverlap = false, reboundResumed = false; int expiryTick = -1; hash = 14695981039346656037UL;
            while (!world.Finished && world.Tick < 560)
            {
                if (world.Tick % 8 == 0)
                {
                    var player = world.Fighters[0].motion; long nearest = long.MaxValue; int dx = 0, dy = 0;
                    foreach (var enemy in world.Fighters)
                    {
                        if (enemy.id == "you" || !enemy.alive || enemy.motion.Length <= PveFixedMotion.MinimumLength) continue;
                        var tail = enemy.motion.Segments[enemy.motion.Length - 1];
                        int x = tail.x - player.X, y = tail.y - player.Y; long distance = (long)x * x + (long)y * y;
                        if (distance < nearest) { nearest = distance; dx = x; dy = y; }
                    }
                    if (nearest < long.MaxValue) heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
                }
                if (world.Tick >= 464) heading = 64;
                int previousTick = world.Tick, remainingBefore = world.SqueezeEscapeTicks;
                world.Step(heading, false, true);
                int remaining = world.SqueezeEscapeTicks;
                if (remaining > previousRemaining) starts++;
                if (remainingBefore > 0)
                {
                    activeTicks++;
                    if (!world.Fighters[0].alive || world.LastPlayerBlockedTick == world.Tick ||
                        world.LastPlayerAttackTick == world.Tick || world.LastPlayerDamageTick == world.Tick)
                        throw new InvalidOperationException("Squeeze protection did not prevent head loss or body combat");
                    if (remaining != remainingBefore - 1 && world.Fighters[0].alive)
                        throw new InvalidOperationException("Squeeze countdown changed unexpectedly");
                }
                var projection = world.RenderState();
                var playerProjection = projection.snakes[0];
                if (remaining > 0)
                {
                    if (playerProjection.shield <= 0 || !string.IsNullOrEmpty(projection.strengthHeadThreat))
                        throw new InvalidOperationException("Protected state or head threat was projected incorrectly");
                    foreach (var snake in projection.snakes)
                    {
                        foreach (bool marked in snake.strengthWeakSegments) if (marked) throw new InvalidOperationException("Protected player saw an edible-body marker");
                        foreach (bool marked in snake.strengthThreatSegments) if (marked) throw new InvalidOperationException("Protected player saw a body-threat marker");
                    }
                }
                if (remainingBefore > 0 && remaining == 0 && world.Fighters[0].alive)
                {
                    long nearest = long.MaxValue; var own = world.Fighters[0].motion;
                    foreach (var foe in world.Fighters) if (foe.id != "you" && foe.alive)
                        for (int i = 1; i < foe.motion.Length; i++)
                        { long dx = (long)own.X - foe.motion.Segments[i].x, dy = (long)own.Y - foe.motion.Segments[i].y; nearest = Math.Min(nearest, dx * dx + dy * dy); }
                    expiryOverlap = nearest < 1116L * 1116L; expiryTick = world.Tick;
                }
                if (world.SqueezeEscapeUsed && remaining == 0 && world.Fighters[0].alive) ended = true;
                if (expiryTick >= 0 && world.Tick == expiryTick + 1 && remainingBefore == 0 && world.LastPlayerBlockedTick == world.Tick) reboundResumed = true;
                Add(expiryOverlap ? 1 : 0); Add(reboundResumed ? 1 : 0);
                Add((int)(playerProjection.shield * 1000)); Add(projection.strengthHeadThreat);
                world.AppendReplayDiagnostics(Add); Add(previousTick); Add(heading); Add(world.Tick); Add(remaining);
                Add(world.LastPlayerBlockedTick); Add(world.LastPlayerAttackTick); Add(world.LastPlayerDamageTick);
                Add(world.PlayerSegmentsEaten); Add(world.PlayerSegmentsLostToBites);
                foreach (var fighter in world.Fighters)
                { Add(fighter.alive ? 1 : 0); Add(fighter.motion.Angle); foreach (var part in fighter.motion.Segments) { Add(part.x); Add(part.y); } }
                previousRemaining = remaining;
            }
            if (starts != 1 || !world.SqueezeEscapeUsed || activeTicks != PveFixedWorld.SqueezeEscapeDurationTicks || !ended || !expiryOverlap || !reboundResumed)
                throw new InvalidOperationException("Seed 35 failed squeeze lifecycle, overlap at expiry or resumed rebound: starts=" + starts + " active=" + activeTicks + " ended=" + ended + " overlap=" + expiryOverlap + " rebound=" + reboundResumed);
            return "squeeze-fixture=" + hash.ToString("x16");
        }

        // The spawned food comes from a real bite; repeated controlled AI
        // contacts exercise the priority boundary in both runtimes.
        private static string RecoveryPriorityFixture()
        {
            var world = new PveFixedWorld(42, false, null, true, localAdvantage: true, localOpeningFood: true,
                localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true,
                localAlignedStart: true, localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true);
            var actors = (PveFixedFighter[])world.Fighters;
            actors[0] = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
            actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(-11700, 0, 0, 16));
            for (int i = 2; i < actors.Length; i++) actors[i].alive = false;
            var fruits = (List<PveFixedFruit>)world.Fruit;
            for (int i = 0; i < fruits.Count; i++) fruits[i] = new PveFixedFruit(new CapturePoint(90000, 90000), i % 4);
            world.Step(0, false);
            if (world.LastPlayerDamageTick != 1 || world.RecoverySuppliesUsed != 1)
                throw new InvalidOperationException("Priority fixture did not spawn from a real bite");
            int index = -1;
            for (int i = 0; i < fruits.Count; i++) if (world.IsRecoveryFruit(i)) { index = i; break; }
            if (index < 0) throw new InvalidOperationException("Priority fixture has no recovery fruit");
            var point = fruits[index].position;
            actors[0] = new PveFixedFighter("you", new PveFixedMotion(-50000, -50000, 0, 16)) { shieldTicks = 2000 };
            hash = 14695981039346656037UL;
            while (world.Tick < 1 + PveFixedWorld.RecoveryPriorityDurationTicks)
            {
                actors[1] = new PveFixedFighter("bot-1", new PveFixedMotion(point.x, point.y, 0, 16)) { shieldTicks = 2000 };
                world.Step(0, false);
                world.AppendReplayDiagnostics(Add);
                Add(world.Tick); Add(world.RecoveryPriorityTicks(index)); Add(world.IsRecoveryFruit(index) ? 1 : 0);
                Add(world.RecoverySupplyCollectedByOpponents);
                if (world.Tick < 1 + PveFixedWorld.RecoveryPriorityDurationTicks &&
                    (!world.IsRecoveryFruit(index) || world.RecoverySupplyCollectedByOpponents != 0))
                    throw new InvalidOperationException("AI collected during recovery priority");
            }
            if (world.IsRecoveryFruit(index) || world.RecoverySupplyCollectedByOpponents < 1)
                throw new InvalidOperationException("AI could not collect recovery fruit after priority expired: tick=" + world.Tick + " marked=" + world.IsRecoveryFruit(index) + " collected=" + world.RecoverySupplyCollectedByOpponents);
            return "priority-fixture=" + hash.ToString("x16");
        }

        // Controlled same-frame bite + blocked contact: random match samples
        // alone do not guarantee exercising this v8-specific branch.
        private static string StableBlockedFixture()
        {
            var world = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningFood: true,
                localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true,
                localAlignedStart: true, localCappedStart: true, localStableBlocked: true);
            var fighters = (PveFixedFighter[])world.Fighters;
            fighters[0] = new PveFixedFighter("you", new PveFixedMotion(-300, 0, 0, 16));
            fighters[1] = new PveFixedFighter("bot-1", new PveFixedMotion(0, 1800, 16384, 80));
            fighters[2] = new PveFixedFighter("bot-2", new PveFixedMotion(13500, 0, 0, 16));
            for (int i = 3; i < fighters.Length; i++) fighters[i].alive = false;
            world.Step(0, false);
            if (world.LastPlayerAttackTick != 1 || world.LastPlayerAttackCount != 1 || world.LastPlayerBlockedTick != 1)
                throw new InvalidOperationException("Stable blocked fixture did not exercise both actual outcomes");
            hash = 14695981039346656037UL;
            world.AppendReplayDiagnostics(Add);
            Add(world.LastPlayerAttackTick); Add(world.LastPlayerAttackCount); Add(world.LastPlayerBlockedTick);
            foreach (var fighter in world.Fighters)
            {
                Add(fighter.id); Add(fighter.alive ? 1 : 0); Add(fighter.motion.Angle);
                foreach (var part in fighter.motion.Segments) { Add(part.x); Add(part.y); }
            }
            return "stable-fixture=" + hash.ToString("x16");
        }
    }
}
