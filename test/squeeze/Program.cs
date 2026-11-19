using SnakeClub.Core;

// Shared by the default v3 suite and the standalone headless entry.
public static class SqueezeEscapeRegression
{
    public static void Run()
    {

        // Follow a deterministic nearest-tail policy through a known real v9 trap.
        var w = new PveFixedWorld(35, true, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
            localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true, localSqueezeEscape: true);
        byte heading = 0;
        var tape = new List<byte>();
        int start = -1, observed = 0, oldEaten = 0, oldLost = 0;
        while (!w.Finished)
        {
            if (w.Tick % 8 == 0)
            {
                var playerMotion = w.Fighters[0].motion;
                long best = long.MaxValue;
                int dx = 0, dy = 0;
                foreach (var fighter in w.Fighters)
                {
                    if (fighter.id == "you" || !fighter.alive || fighter.motion.Length <= 5) continue;
                    var tail = fighter.motion.Segments[fighter.motion.Length - 1];
                    int x = tail.x - playerMotion.X, y = tail.y - playerMotion.Y;
                    long distance = (long)x * x + (long)y * y;
                    if (distance < best) { best = distance; dx = x; dy = y; }
                }
                if (best < long.MaxValue)
                    heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
            }
            w.Step(heading, false, true);
            tape.Add(heading); tape.Add(2);
            if (start < 0 && w.SqueezeEscapeUsed)
            {
                start = w.Tick;
                oldEaten = w.PlayerSegmentsEaten; oldLost = w.PlayerSegmentsLostToBites;
                if (w.SqueezeEscapeTicks != 40 || w.LastSqueezeEscapeTick != start)
                    throw new Exception("incorrect initial duration");
                if (StrengthContactNotice.Text(w, w.Tick, out bool activeWarning) != "遇强弹开 · 脱困保护中" || !activeWarning)
                    throw new Exception("real escape activation did not replace the misleading bite advice");
                var timers = (int[])typeof(PveFixedWorld).GetField("botTimer", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).GetValue(w);
                if (timers.Skip(1).Any(timer => timer != 0))
                    throw new Exception("cached AI targets were not invalidated on escape");
            }
            else if (start >= 0 && w.Tick <= start + 40)
            {
                observed++;
                if (w.PlayerSegmentsEaten != oldEaten || w.PlayerSegmentsLostToBites != oldLost)
                    throw new Exception("combat during escape");
                int expected = start + 40 - w.Tick;
                if (w.SqueezeEscapeTicks != expected)
                    throw new Exception($"countdown tick={w.Tick} actual={w.SqueezeEscapeTicks} expected={expected}");
            }
        }
        if (start != 468 || observed != 40) throw new Exception($"fixture start={start} observed={observed}");
        if (!PveFixedRecording.TryReplayStrength(35, true, tape.ToArray(), out var replay,
            true, true, true, true, true, true, true, true, true, true))
            throw new Exception("v10 replay rejected");
        if (StrengthStateDigest.Compute(w) != StrengthStateDigest.Compute(replay)) throw new Exception("replay mismatch");
        Console.WriteLine($"PASS v10 real trap start={start}, protected 40 subsequent ticks, no bites, replay ticks={w.Tick}.");

        // The escape flag is independent of birth protection, but gates both head
        // and body combat in either direction; removing it restores real collisions.
        var player = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
        var other = new PveFixedFighter("bot", new PveFixedMotion(0, 0, 0, 16));
        var actors = new[] { player, other };
        var powers = new[] {
            new StrengthInputs { basePower=100, speed=1000, baseSpeed=1000, randomFactor=1000, operationFactor=1000 },
            new StrengthInputs { basePower=300, speed=1000, baseSpeed=1000, randomFactor=1000, operationFactor=1000 }
        };
        player.squeezeEscapeTicks=1;
        StrengthHeadCombat.Resolve(actors,powers);
        if(!player.alive || !other.alive || player.shieldTicks != 0) throw new Exception("escape did not separately protect head combat");
        player.squeezeEscapeTicks=0;
        StrengthHeadCombat.Resolve(actors,powers);
        if(player.alive) throw new Exception("head collision remained protected after escape");
        player = new PveFixedFighter("you", new PveFixedMotion(0, 0, 0, 16));
        other = new PveFixedFighter("bot", new PveFixedMotion(13500, 0, 0, 16));
        actors = new[] { player, other };
        powers[0].basePower=300;powers[1].basePower=100;
        player.squeezeEscapeTicks=1;
        if(new StrengthBodyCombat().Resolve(1,actors,powers).Count != 0) throw new Exception("escape allowed player bite");
        player.squeezeEscapeTicks=0;
        if(new StrengthBodyCombat().Resolve(1,actors,powers).Count == 0) throw new Exception("body bite remained blocked after escape");
        Console.WriteLine("PASS v10 independent protection gates head and body combat and releases at expiry.");
        player = new PveFixedFighter("you", new PveFixedMotion(13500, 0, 0, 16));
        other = new PveFixedFighter("bot", new PveFixedMotion(0, 0, 0, 16));
        actors = new[] { player, other }; powers[0].basePower=100;powers[1].basePower=300;
        player.squeezeEscapeTicks=1;
        if(new StrengthBodyCombat().Resolve(1,actors,powers).Count != 0) throw new Exception("escape allowed AI bite");
        player.squeezeEscapeTicks=0;
        if(new StrengthBodyCombat().Resolve(1,actors,powers).Count == 0) throw new Exception("AI bite remained blocked after escape");

        var isolated = new PveFixedWorld(35,true,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true);
        if(isolated.SqueezeEscapeUsed || isolated.SqueezeEscapeTicks != 0 || isolated.LastSqueezeEscapeTick != -1) throw new Exception("escape leaked across worlds");
        byte travel = 0;
        while(!isolated.SqueezeEscapeUsed && !isolated.Finished) {
         if(isolated.Tick%8==0) {
          var own=isolated.Fighters[0].motion; long nearest=long.MaxValue;int x=0,y=0;
          foreach(var f in isolated.Fighters){if(f.id=="you"||!f.alive||f.motion.Length<=5)continue;var pos=f.motion.Segments[f.motion.Length-1];int dx=pos.x-own.X,dy=pos.y-own.Y;long dist=(long)dx*dx+(long)dy*dy;if(dist<nearest){nearest=dist;x=dx;y=dy;}}
          if(nearest<long.MaxValue) travel=(byte)(((int)Math.Round(Math.Atan2(y,x)*128/Math.PI)+256)&255);
         }
         isolated.Step(travel,false,true);
        }
        if(!isolated.SqueezeEscapeUsed) throw new Exception("escape fixture missing");
        var projection = isolated.RenderState();
        if(projection.snakes.Any(s => s.strengthWeakSegments.Any(value => value)) || projection.snakes[0].strengthThreatSegments.Any(value => value) || !string.IsNullOrEmpty(projection.strengthHeadThreat)) throw new Exception("escape showed actionable strength threats");
        var isolatedPlayer=isolated.Fighters[0].motion;
        var fruit=(List<PveFixedFruit>)isolated.Fruit;
        fruit[0]=new PveFixedFruit(new CapturePoint(isolatedPlayer.X,isolatedPlayer.Y),fruit[0].color);
        int lengthBefore=isolatedPlayer.Length; int xBefore=isolatedPlayer.X, yBefore=isolatedPlayer.Y;
        isolated.Step(travel,false,true);
        if(isolatedPlayer.Length != lengthBefore+1) throw new Exception("normal fruit could not be eaten during escape");
        if(isolatedPlayer.X==xBefore && isolatedPlayer.Y==yBefore) throw new Exception("escape blocked actual movement");
        Console.WriteLine("PASS v10 suppresses threats, preserves movement and fruit, and isolates world state.");

        // Reproduce the same real pre-contact state from the recorded inputs. The
        // counter setup is controlled to verify defender changes and one-use limits;
        // the next blocked contact itself comes from the actual simulation step.
        PveFixedWorld AtTrap() {
         var x=new PveFixedWorld(35,true,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true);
         for(int i=0;i<464;i++)x.Step(tape[i*2],false,true);
         return x;
        }
        var changed=AtTrap(); var privateFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var streakField=typeof(PveFixedWorld).GetField("squeezeBlockedStreak",privateFlags);
        var defenderField=typeof(PveFixedWorld).GetField("squeezeBlockedDefender",privateFlags);
        streakField.SetValue(changed,3);defenderField.SetValue(changed,"another-defender");
        changed.Step(tape[464*2],false,true);
        if(changed.LastPlayerBlockedTick!=465 || changed.SqueezeEscapeUsed || (int)streakField.GetValue(changed)!=1) throw new Exception("changing defender did not reset streak");
        streakField.SetValue(changed,3);defenderField.SetValue(changed,"bot-4");
        changed.Step(tape[465*2],false,true);
        if(changed.LastPlayerBlockedTick!=466 || !changed.SqueezeEscapeUsed || changed.SqueezeEscapeTicks!=40) throw new Exception("same defender did not trigger after four contacts");
        var spent=AtTrap(); typeof(PveFixedWorld).GetProperty("SqueezeEscapeUsed").SetValue(spent,true);
        streakField.SetValue(spent,3);defenderField.SetValue(spent,"bot-4");
        spent.Step(tape[464*2],false,true);
        if(spent.LastPlayerBlockedTick!=465 || spent.SqueezeEscapeTicks!=0 || spent.LastSqueezeEscapeTick!=-1) throw new Exception("spent escape was refreshed");
        Console.WriteLine("PASS v10 real contacts reset when defender changes and cannot refresh a spent escape.");

        // In a controlled overlapping-head case, the final protected simulation step
        // cannot kill either fighter; after expiry the normal head rule resumes.
        var edge = new PveFixedWorld(42,false,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true);
        var edgeActors=(PveFixedFighter[])edge.Fighters;
        edgeActors[0]=new PveFixedFighter("you",new PveFixedMotion(0,0,0,16));
        edgeActors[1]=new PveFixedFighter("bot-1",new PveFixedMotion(0,0,0,16));
        for(int i=2;i<edgeActors.Length;i++)edgeActors[i].alive=false;
        edgeActors[0].squeezeEscapeTicks=1;
        edge.Step(0,false,true);
        if(!edgeActors[0].alive || !edgeActors[1].alive || edge.SqueezeEscapeTicks!=0)throw new Exception("last protected head step failed");
        edge.Step(0,false,true);
        if(edgeActors[0].alive && edgeActors[1].alive)throw new Exception("overlapping heads did not resume combat after expiry");
        Console.WriteLine("PASS v10 overlapping heads are protected through the last step, then resume combat.");

        byte AiChoice(bool escaping) {
         var botWorld=new PveFixedWorld(23,false,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true);
         var bots=(PveFixedFighter[])botWorld.Fighters;
         bots[0]=new PveFixedFighter("you",new PveFixedMotion(18000,0,0,16));
         bots[1]=new PveFixedFighter("bot-1",new PveFixedMotion(0,0,0,16));
         for(int i=2;i<bots.Length;i++)bots[i].alive=false;
         bots[0].squeezeEscapeTicks=escaping?40:0;
         var snacks=(List<PveFixedFruit>)botWorld.Fruit;
         for(int i=0;i<snacks.Count;i++)snacks[i]=new PveFixedFruit(new CapturePoint(90000,90000),0);
         snacks[0]=new PveFixedFruit(new CapturePoint(-6000,0),0);
         return (byte)typeof(PveFixedWorld).GetMethod("BotHeading",privateFlags).Invoke(botWorld,new object[]{1});
        }
        if(PveFixedMotion.DirectionX(AiChoice(false))<=0 || PveFixedMotion.DirectionX(AiChoice(true))>=0)throw new Exception("AI did not exclude escaping player as edible target");
        Console.WriteLine("PASS v10 AI chooses an ordinary fruit over the escaping player body.");
        var interrupted=new PveFixedWorld(1,true,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true);
        streakField.SetValue(interrupted,3);defenderField.SetValue(interrupted,"bot-4");
        interrupted.Step(0,false,true);
        if(interrupted.LastPlayerBlockedTick==interrupted.Tick || (int)streakField.GetValue(interrupted)!=0 || defenderField.GetValue(interrupted)!=null || interrupted.SqueezeEscapeUsed) throw new Exception("a non-blocked step did not reset the streak");
        Console.WriteLine("PASS v10 an intervening step without a block clears the streak.");
        bool rejected = false;
        try { _ = new PveFixedWorld(1, false, null, true, localSqueezeEscape: true); }
        catch (ArgumentException) { rejected = true; }
        if (!rejected || PveFixedRecording.TryReplayStrength(1, false, new byte[] { 0, 0 }, out _, localSqueezeEscape: true))
            throw new Exception("escape variant accepted without its v9 prerequisites");
        Console.WriteLine("PASS v10 requires the complete offline v9 configuration.");

        // Mutate only event timestamps/counts in a separate triggered fixture to
        // verify presentation precedence without changing simulation outcomes.
        var feedback = AtTrap();
        for (int i = 464; i < 468; i++) feedback.Step(tape[i * 2], false, true);
        if (feedback.SqueezeEscapeTicks != 40 || feedback.LastPlayerBlockedTick != feedback.Tick)
            throw new Exception("combined feedback fixture did not trigger actual escape");
        var damageTick = typeof(PveFixedWorld).GetProperty("LastPlayerDamageTick");
        var damageCount = typeof(PveFixedWorld).GetProperty("LastPlayerDamageCount");
        var attackTick = typeof(PveFixedWorld).GetProperty("LastPlayerAttackTick");
        var attackCount = typeof(PveFixedWorld).GetProperty("LastPlayerAttackCount");
        damageTick.SetValue(feedback, feedback.Tick); damageCount.SetValue(feedback, 2);
        attackTick.SetValue(feedback, -1);
        if (StrengthContactNotice.Text(feedback, feedback.Tick, out var warn) != "身体被吃 2 节 · 遇强弹开" || !warn)
            throw new Exception("escape text masked simultaneous damage");
        attackTick.SetValue(feedback, feedback.Tick); attackCount.SetValue(feedback, 1);
        if (StrengthContactNotice.Text(feedback, feedback.Tick, out warn) != "互吃 +1/-2 · 弹开" || !warn)
            throw new Exception("escape text masked simultaneous mutual bite");
        attackTick.SetValue(feedback, -1); damageTick.SetValue(feedback, feedback.Tick + 1);
        if (StrengthContactNotice.Text(feedback, feedback.Tick + 1, out warn) != "身体被吃掉 2 节" || !warn)
            throw new Exception("escape text masked a newer damage event");
        if (StrengthContactNotice.Text(feedback, feedback.Tick + 21, out _) != "")
            throw new Exception("expired events stayed visible during escape");
        Console.WriteLine("PASS v10 escape notice preserves simultaneous, newer, and expired event precedence.");
        damageTick.SetValue(feedback, -1);
        feedback.Fighters[0].squeezeEscapeTicks = 0;
        if (StrengthContactNotice.Text(feedback, feedback.Tick, out warn) != "强度不足 · 试试更弱的部位" || !warn)
            throw new Exception("expired escape retained special block notice");
        var v9Feedback = new PveFixedWorld(1, false, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true, localAlignedStart: true,
            localCappedStart: true, localStableBlocked: true, localRecoveryPriority: true);
        typeof(PveFixedWorld).GetProperty("Tick").SetValue(v9Feedback, 1);
        typeof(PveFixedWorld).GetProperty("LastPlayerBlockedTick").SetValue(v9Feedback, 1);
        v9Feedback.Fighters[0].squeezeEscapeTicks = 1; // invalid controlled state checks version gating
        if (StrengthContactNotice.Text(v9Feedback, 1, out warn) != "强度不足 · 试试更弱的部位" || !warn)
            throw new Exception("old rule displayed the v10-only escape notice");
        Console.WriteLine("PASS escape-only block notice respects duration and rule version.");
    }
}
