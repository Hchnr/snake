using System; using System.Collections.Generic; using SnakeClub.Core;
public static class StrengthHeadTieRegression {
 static StrengthInputs Power() => new(){basePower=100,speed=1000,baseSpeed=1000,randomFactor=1000,operationFactor=1000};
 static PveFixedFighter F(string id,int x,int y) => new(id,new PveFixedMotion(x,y,0,16));
 static void Check(bool ok,string what){if(!ok)throw new Exception(what);}
 public static void Run(){
 {var a=F("a",0,0);var b=F("b",0,1000);var c=F("c",11700,0);var f=new[]{a,b,c};var p=new[]{Power(),Power(),Power()};var ties=new List<HeadTieContact>();StrengthHeadCombat.Resolve(f,p,true,ties);var bites=new StrengthBodyCombat().ResolveBuffered(1,f,p,true,true);var used=StrengthHeadCombat.SeparateTies(f,ties,bites,true);long x=(long)a.motion.X-b.motion.X,y=(long)a.motion.Y-b.motion.Y;Check(bites.Count==1 && used[0] && used[1] && x*x+y*y>=1440L*1440L,"actual tie and third-body bite separate");}
 {var a=F("a",0,0);var b=F("b",5000,0);var f=new[]{a,b};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,0),new CapturePoint(0,1000),1000000)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);Check(!used[0] && a.motion.X==0 && b.motion.X==5000,"stale tie cannot teleport live heads");}
 {var a=F("a",0,0);var b=F("b",0,1000);b.shieldTicks=1;var f=new[]{a,b};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,0),new CapturePoint(0,1000),1000000)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);Check(!used[0] && a.motion.Y==0 && b.motion.Y==1000,"protected head cannot separate");}
 foreach(int x in new[]{PveFixedMotion.Edge-100,-PveFixedMotion.Edge+100}) {var a=F("a",x,0);var b=F("b",x+1,1000);var f=new[]{a,b};var ties=new List<HeadTieContact>();StrengthHeadCombat.Resolve(f,new[]{Power(),Power()},true,ties);var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);long dx=(long)a.motion.X-b.motion.X,dy=(long)a.motion.Y-b.motion.Y;Check(used[0] && Math.Abs(a.motion.X)<=PveFixedMotion.Edge && Math.Abs(b.motion.X)<=PveFixedMotion.Edge && dx*dx+dy*dy>=1440L*1440L,"wall pair bounded and separated");}
 {var a=F("a",0,0);var b=F("b",0,800);var c=F("c",1000,0);var f=new[]{a,b,c};var ties=new List<HeadTieContact>();StrengthHeadCombat.Resolve(f,new[]{Power(),Power(),Power()},true,ties);var used=StrengthHeadCombat.SeparateTies(f,ties,new(),true);Check((used[0] && used[1] && !used[2]) || (!used[0] && !used[1] && !used[2]),"three-way ties use at most one disjoint pair");}

 {var a=F("a",0,0);var b=F("b",0,1000);var c=F("c",11700,0);var f=new[]{a,b,c};var p=new[]{Power(),Power(),Power()};var ties=new List<HeadTieContact>();StrengthHeadCombat.Resolve(f,p,true,ties);var bites=new StrengthBodyCombat().ResolveBuffered(1,f,p,true,true);var used=StrengthHeadCombat.SeparateTies(f,ties,bites,false);Check(bites.Count==1 && !used[0] && !used[1],"v11 preserves prior suppression");}
 for(int seed=1;seed<=8;seed++) {
 var w=new PveFixedWorld(seed,false,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,localMultiBite:true,localHeadTieAfterBite:true);
 var tape=new List<byte>();while(!w.Finished){byte h=(byte)((w.Tick*13+seed)%256);tape.Add(h);tape.Add(2);w.Step(h,false,true);}
 Check(w.RulesVersion==PveFixedRecording.LocalStrengthHeadTieRules && PveFixedRecording.TryReplayLocalStrength(w.RulesVersion,seed,false,tape.ToArray(),out var replay) && StrengthStateDigest.Compute(w)==StrengthStateDigest.Compute(replay),"v12 versioned recording "+seed);
 }

 {var a=F("a",0,0);var b=F("b",0,1000);var c=F("c",0,-1500);var f=new[]{a,b,c};var ties=new List<HeadTieContact>();StrengthHeadCombat.Resolve(f,new[]{Power(),Power(),Power()},true,ties);var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true); long dx=(long)a.motion.X-c.motion.X,dy=(long)a.motion.Y-c.motion.Y;long ex=(long)b.motion.X-c.motion.X,ey=(long)b.motion.Y-c.motion.Y;Check(!used[0] && !used[1] && a.motion.X==0 && a.motion.Y==0 && b.motion.X==0 && b.motion.Y==1000,"third-head/body constrained fixture has no safe candidate and preserves positions");}
 {var a=F("a",0,-10);var b=F("b",0,10);var f=new[]{a,b,F("c",1500,0),F("d",750,1299),F("e",-750,1299),F("f",-1500,0),F("g",-750,-1299),F("h",750,-1299)};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,-10),new CapturePoint(0,10),400)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);Check(!used[0] && a.motion.X==0 && a.motion.Y==-10 && b.motion.X==0 && b.motion.Y==10,"no safe separation preserves heads");}

 {var a=F("a",0,0);var b=F("b",0,1000);var c=F("c",0,2900);var d=F("d",0,3900);var f=new[]{a,b,c,d};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,0),new CapturePoint(0,1000),1000000),new(2,3,new CapturePoint(0,2900),new CapturePoint(0,3900),1000000)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);long x=(long)b.motion.X-c.motion.X,y=(long)b.motion.Y-c.motion.Y;Check(used[0] && used[1] && (x*x+y*y>=1440L*1440L || !used[2]),"later ordinary pair does not create overlap with safely separated head");}

 {var rnd=new Random(441); for(int n=0;n<10000;n++) {int x=rnd.Next(-105000,105001),y=rnd.Next(-105000,105001),dx=rnd.Next(-1000,1001),dy=rnd.Next(-1000,1001); if((long)dx*dx+(long)dy*dy>=1440L*1440L)continue; var a=F("a",x,y);var b=F("b",x+dx,y+dy);var aa=F("a",x,y);var bb=F("b",x+dx,y+dy);var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(x,y),new CapturePoint(x+dx,y+dy),(long)dx*dx+(long)dy*dy)};var old=StrengthHeadCombat.SeparateTies(new[]{a,b},new(ties),new(),false);var next=StrengthHeadCombat.SeparateTies(new[]{aa,bb},new(ties),new(),true);if(old[0]!=next[0] || a.motion.X!=aa.motion.X || a.motion.Y!=aa.motion.Y || b.motion.X!=bb.motion.X || b.motion.Y!=bb.motion.Y || a.motion.Angle!=aa.motion.Angle || b.motion.Angle!=bb.motion.Angle)throw new Exception("safe ordinary tie changed at "+n); } }

 {var a=F("a",0,0);var b=F("b",0,1000);var d=F("d",0,-1200);var f=new[]{a,b,d};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,0),new CapturePoint(0,1000),1000000)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);long ax=(long)a.motion.X-d.motion.X,ay=(long)a.motion.Y-d.motion.Y;Check(ax*ax+ay*ay>=1200L*1200L && (used[0] || (a.motion.X==0 && a.motion.Y==0)),"existing third overlap cannot deepen");}

 {var a=F("a",0,0);var b=F("b",0,1000);var d=F("d",1000,-130);var f=new[]{a,b,d};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,0),new CapturePoint(0,1000),1000000)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);long x=(long)a.motion.X-d.motion.X,y=(long)a.motion.Y-d.motion.Y;Check(used[0] && a.motion.X==0 && a.motion.Y==-260 && x*x+y*y==1000L*1000L+130L*130L,"equal third-head distance is permitted");}

 {var a=F("a",0,0);var b=F("b",0,1000);var c=F("c",0,2900);var d=F("d",11700,3500);var f=new[]{a,b,c,d};var ties=new List<HeadTieContact>{new(0,1,new CapturePoint(0,0),new CapturePoint(0,1000),1000000)};var used=StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);long prior=(long)(b.motion.X-c.motion.X)*(b.motion.X-c.motion.X)+(long)(b.motion.Y-c.motion.Y)*(b.motion.Y-c.motion.Y);var contact=new BlockedBodyContact("c","d",d.motion.Segments[13],new CapturePoint(1,0),360000,13);StableBlockedSeparation.Apply(f,new List<BodyBite>(),new[]{contact},used,out _,true);long after=(long)(b.motion.X-c.motion.X)*(b.motion.X-c.motion.X)+(long)(b.motion.Y-c.motion.Y)*(b.motion.Y-c.motion.Y);Check(used[0] && prior>=1440L*1440L && after<1440L*1440L,"known limitation later body rebound can introduce head collision");}

 {var a=new PveFixedFighter("a",new PveFixedMotion(0,0,35592,16));var b=new PveFixedFighter("b",new PveFixedMotion(452,-172,17897,16));long D(CapturePoint x,CapturePoint y){long dx=(long)x.x-y.x,dy=(long)x.y-y.y;return dx*dx+dy*dy;}long Nearest(PveFixedFighter head,PveFixedFighter body){long best=long.MaxValue;for(int i=1;i<body.motion.Length;i++)best=Math.Min(best,D(head.motion.Segments[0],body.motion.Segments[i]));return best;}var oldAB=Nearest(a,b);var oldBA=Nearest(b,a);var ties=new List<HeadTieContact>{new(0,1,a.motion.Segments[0],b.motion.Segments[0],D(a.motion.Segments[0],b.motion.Segments[0]))};StrengthHeadCombat.SeparateTies(new[]{a,b},ties,new(),true);long radius=1116L*1116L;Check((oldAB<radius || Nearest(a,b)>=radius)&&(oldBA<radius || Nearest(b,a)>=radius),"separating pair does not introduce first head/body contact");}
 {var a=F("a",0,0);var b=F("b",0,1000);var c=F("c",-3200,-1055);var f=new[]{a,b,c};long Before(){long best=long.MaxValue;for(int i=1;i<a.motion.Length;i++){var p=a.motion.Segments[i];long x=(long)c.motion.X-p.x,y=(long)c.motion.Y-p.y;best=Math.Min(best,x*x+y*y);}return best;}long before=Before();var ties=new List<HeadTieContact>();StrengthHeadCombat.Resolve(f,new[]{Power(),Power(),Power()},true,ties);StrengthHeadCombat.SeparateTies(f,ties,new(){new BodyBite("a","other",1,1,1,1)},true);long after=Before();Check(before==1273025 && after>=1116L*1116L,"third head cannot be moved into body by relaxed segments");}
    // A real seeded Step sequence: v11 leaves the tie overlapping while a
    // third body is eaten, then loses the player. V12 separates that pair.
    foreach (bool enabled in new[] { false, true })
    {
      var world = new PveFixedWorld(14, true, null, true, localAdvantage: true, localOpeningFood: true,
        localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true,
        localAlignedStart: true, localCappedStart: true, localStableBlocked: true,
        localRecoveryPriority: true, localSqueezeEscape: true, localMultiBite: true, localHeadTieAfterBite: enabled);
      byte heading = 0;
      while (!world.Finished && world.Tick < 293)
      {
        if (world.Tick % 8 == 0)
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
          if (nearest < long.MaxValue) heading = (byte)(((int)Math.Round(Math.Atan2(dy, dx) * 128 / Math.PI) + 256) & 255);
        }
        world.Step(heading, false, true);
        if (world.Tick == 291)
          Check(world.LastPlayerHeadTieTick == (enabled ? 291 : -1) && world.PlayerSegmentsEaten == 20 && world.PlayerSegmentsLostToBites == 13,
            "seed 14 simultaneous bite and tie state");
      }
      Check(enabled ? world.Tick == 293 && world.Fighters[0].alive : world.Tick == 292 && !world.Fighters[0].alive && world.Fighters[0].eliminatedBy == "bot-2",
        "seed 14 tie separation changes immediate head outcome");
    }

 }
}
