#!/usr/bin/env python3
"""Compare candidate offline opening encounters without modifying the game.

0: current v13; 1: hold initial radial heading through player protection;
2: spawn bot-1 four units closer; 3: narrow arc + same heading through player
protection; 4: as 3 but release ten ticks early; 5: as 4 but hold only when
player is not boosting, aims toward a nearby target and no third head is near.
Samples are fixed automated inputs, not estimates of player/device outcomes.
The original AI still runs while held; its steering and boost can be suppressed,
so this intervention is not a faithful normal-threat-avoidance implementation.
"""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
parser.add_argument('--seeds', type=int, default=200)
parser.add_argument('--variants', default='0,1,2,3,4,5')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000: parser.error('seeds must be 1..10000')
try:
    variants = [int(v) for v in args.variants.split(',')]
except ValueError:
    parser.error('variants must be a comma-separated list of numbers 0..5')
if not variants or any(v < 0 or v > 5 for v in variants): parser.error('variants must be 0..5')
source = args.root.resolve() / 'unity/Assets/Snake/Core'
if not (source / 'PveFixedWorld.cs').is_file(): parser.error('missing core sources')
program = r'''using SnakeClub.Core;
int n = int.Parse(args[0]);
foreach (int variant in args[1].Split(',').Select(int.Parse))
foreach (bool first in new[]{false,true})
for (int policy=0; policy<4; policy++)
{
    PveFixedWorld.EvalEncounterVariant=variant;
    int expiryReached=0, expiryFinished=0, expiryPlayable=0, finishedBeforeExpiry=0, expiryKill=0, expiryHeadLoss=0, expiryPlayerDead=0;
    int targetDead=0,targetGold=0,target3=0,target6=0,target10=0,target20=0;
    int near3=0,near6=0,near10=0,near20=0,noGold=0;
    int bite15=0, damage15=0, damageFirst=0, dead15=0, bite60=0, dead60=0, kills15=0,kills60=0;
    long eaten15=0,lost15=0,eaten60=0,lost60=0;
    int shieldEnd=first?80:60;
    for (int seed=1; seed<=n; seed++)
    {
        var w=new PveFixedWorld(seed,first,null,true,localAdvantage:true,localOpeningFood:true,
            localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,
            localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,
            localMultiBite:true,localHeadTieAfterBite:true,localOpeningBoost:true);
        if (w.RulesVersion != PveFixedRecording.LocalStrengthOpeningBoostRules)
            throw new InvalidOperationException("Expected the v13 baseline configuration");
        byte heading=0; int firstB=-1,firstD=-1; bool got15=false, observed=false;
        while (!w.Finished && w.Tick<1200)
        {
            if (w.Tick%8==0 && (policy==1 || policy==2))
            {
                var s=w.RenderState(); var me=s.snakes[0].segments[0];
                double nearest=double.MaxValue; float tx=0,ty=0;
                if (policy==1)
                    foreach (var f in w.Fruit)
                    {
                        float x=f.position.x/1000f, y=f.position.y/1000f;
                        double dx=x-me.x,dy=y-me.y,d=dx*dx+dy*dy;
                        if(d<nearest){nearest=d;tx=x;ty=y;}
                    }
                else
                    foreach(var f in s.snakes.Skip(1))
                    {
                        if(!f.alive || f.segments.Count<=5) continue;
                        var p=f.segments[f.segments.Count-1];
                        double dx=p.x-me.x,dy=p.y-me.y,d=dx*dx+dy*dy;
                        if(d<nearest){nearest=d;tx=p.x;ty=p.y;}
                    }
                if(nearest<double.MaxValue)
                    heading=(byte)(((int)Math.Round(Math.Atan2(ty-me.y,tx-me.x)*128/Math.PI)+256)&255);
            }
            int prevKills=w.Fighters[0].kills;
            w.Step(heading,policy==3,true);
            if(w.Tick==shieldEnd)
            {
                observed=true; expiryReached++;
                if(w.Fighters[0].kills>prevKills)expiryKill++;
                if(!w.Fighters[0].alive)expiryPlayerDead++;
                if(!w.Fighters[0].alive && w.Fighters[0].eliminationReason=="strength_head")expiryHeadLoss++;
                if(!w.Fighters[1].alive)targetDead++;
                if(w.Finished) expiryFinished++;
                else
                {
                    expiryPlayable++;
                    var s=w.RenderState(); var me=s.snakes[0].segments[0]; double best=double.MaxValue;
                    foreach(var f in s.snakes.Skip(1))
                        for(int j=1;j<f.segments.Count;j++)
                        {
                            if(!f.strengthWeakSegments[j])continue;
                            double dx=f.segments[j].x-me.x,dy=f.segments[j].y-me.y,d=dx*dx+dy*dy;
                            if(d<best)best=d;
                        }
                    var target=s.snakes.Find(x=>x.id=="bot-1");
                    if(target!=null && target.alive && target.segments.Count>1 && target.strengthWeakSegments[target.segments.Count-1])
                    {
                        targetGold++;var p=target.segments[target.segments.Count-1];
                        double x=p.x-me.x,y=p.y-me.y,d=x*x+y*y;
                        if(d<=9)target3++;if(d<=36)target6++;if(d<=100)target10++;if(d<=400)target20++;
                    }
                    if(best==double.MaxValue)noGold++;
                    else {if(best<=9)near3++;if(best<=36)near6++;if(best<=100)near10++;if(best<=400)near20++;}
                }
            }
            if(firstB<0 && w.PlayerSegmentsEaten>0)firstB=w.Tick;
            if(firstD<0 && w.PlayerSegmentsLostToBites>0)firstD=w.Tick;
            if(w.Tick==300)
            {
                kills15+=w.Fighters[0].kills;got15=true;
                if(!w.Fighters[0].alive)dead15++;
                if(firstB>0)bite15++;if(firstD>0)damage15++;
                if(firstD>0 && (firstB<0 || firstD<firstB))damageFirst++;
                eaten15+=w.PlayerSegmentsEaten;lost15+=w.PlayerSegmentsLostToBites;
            }
        }
        if(!observed) finishedBeforeExpiry++;
        if(!got15)
        {
            kills15+=w.Fighters[0].kills;if(firstB>0)bite15++;if(firstD>0)damage15++;
            if(firstD>0 && (firstB<0||firstD<firstB))damageFirst++;
            if(!w.Fighters[0].alive)dead15++;
            eaten15+=w.PlayerSegmentsEaten;lost15+=w.PlayerSegmentsLostToBites;
        }
        kills60+=w.Fighters[0].kills;if(firstB>0)bite60++;if(!w.Fighters[0].alive)dead60++;
        eaten60+=w.PlayerSegmentsEaten;lost60+=w.PlayerSegmentsLostToBites;
    }
    Console.WriteLine($"v={variant} first={first} policy={policy} n={n} expiryReached={expiryReached} expiryFinished={expiryFinished} expiryPlayable={expiryPlayable} " +
        $"finishedBeforeExpiry={finishedBeforeExpiry} expiryKill={expiryKill} expiryHeadLoss={expiryHeadLoss} expiryPlayerDead={expiryPlayerDead} targetDead={targetDead} " +
        $"targetGold={targetGold} targetGoldWithin3/6/10/20={target3}/{target6}/{target10}/{target20} " +
        $"anyGold3/6/10/20={near3}/{near6}/{near10}/{near20} noGold={noGold} " +
        $"bite15={bite15} damage15={damage15} damageFirst={damageFirst} dead15={dead15} kills15={kills15} " +
        $"eaten15={eaten15} lost15={lost15} bite60={bite60} dead60={dead60} kills60={kills60} eaten60={eaten60} lost60={lost60}");
}
'''

def once(text, before, after):
    if text.count(before)!=1: raise RuntimeError('Core source differs; update diagnostic deliberately')
    return text.replace(before,after,1)

with tempfile.TemporaryDirectory(prefix='snake-opening-eval-') as temp:
    folder=Path(temp)
    shutil.copytree(source,folder/'Core')
    fingerprint = hashlib.sha256()
    for item in sorted((folder / 'Core').glob('*.cs')):
        fingerprint.update(item.name.encode('utf-8') + b'\0')
        fingerprint.update(item.read_bytes())
        fingerprint.update(b'\0')
    core=folder/'Core/PveFixedWorld.cs'
    code=core.read_text()
    code=once(code,'private readonly int[] botTimer = new int[8];',
        'public static int EvalEncounterVariant;\n        private byte evalEncounterHeading;\n        private readonly int[] botTimer = new int[8];')
    code=once(code,'botDirection[i] = (byte)heading;',
        'botDirection[i] = (byte)heading;\n                if (i == 1) evalEncounterHeading = (byte)heading;')
    code=once(code,'heading = HeadingAwayFromPlayer(head);',
        'heading = EvalEncounterVariant >= 3 ? 0 : HeadingAwayFromPlayer(head);')
    code=once(code,'angle = unchecked((byte)((angle % 65) - 32)); // seeded arc around the initial direction',
        'angle = unchecked((byte)(EvalEncounterVariant >= 3 ? (angle % 17) - 8 : (angle % 65) - 32));')
    code=once(code,'radius += 5000;','radius += EvalEncounterVariant == 2 ? 1000 : 5000;')
    before='''fighter.motion.Step(i == 0 ? heading : BotHeading(i), i == 0 ? boost : botBoost[i],
                    i == 0 ? playerBonuses : new PveFixedBonuses());'''
    after='''byte actualHeading = i == 0 ? heading : BotHeading(i);
                if (i == 1 && LocalOpeningBoost && Tick <= (maxTicks == 60 * TickRate ? 80 : 60) - (EvalEncounterVariant >= 4 ? 10 : 0))
                {
                    bool hold = EvalEncounterVariant == 1 || ((EvalEncounterVariant == 3 || EvalEncounterVariant == 4) && fighter.motion.X < PveFixedMotion.Edge - 4000);
                    if (EvalEncounterVariant == 5 && !boost && fighter.motion.X < PveFixedMotion.Edge - 4000)
                    {
                        int dx = fighter.motion.X - fighters[0].motion.X, dy = fighter.motion.Y - fighters[0].motion.Y;
                        int distance = PveFixedMotion.Distance(dx,dy); byte playerDirection=(byte)(fighters[0].motion.Angle >> 8);
                        long dot=(long)dx*PveFixedMotion.DirectionX(playerDirection)+(long)dy*PveFixedMotion.DirectionY(playerDirection);
                        bool clear=true;
                        for(int other=2;other<fighters.Length;other++)
                        {
                            if(!fighters[other].alive)continue;
                            long ox=(long)fighter.motion.X-fighters[other].motion.X,oy=(long)fighter.motion.Y-fighters[other].motion.Y;
                            if(ox*ox+oy*oy<6000L*6000L){clear=false;break;}
                        }
                        hold=distance>=3000 && distance<=16000 && Math.Abs(dy)<=3000 && dot>(long)distance*9238 && clear;
                    }
                    if(hold) { actualHeading=EvalEncounterVariant==5 ? AvoidLethalHead(i,evalEncounterHeading) : evalEncounterHeading; botBoost[i]=false; }
                }
                fighter.motion.Step(actualHeading, i == 0 ? boost : botBoost[i],
                    i == 0 ? playerBonuses : new PveFixedBonuses());'''
    code=once(code,before,after)
    core.write_text(code)
    (folder/'Program.cs').write_text(program)
    (folder/'Eval.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="Core/*.cs"/></ItemGroup></Project>')
    print(f'core_sha256={fingerprint.hexdigest()} seeds=1..{args.seeds} variants={args.variants}', flush=True)
    print('Isolated deterministic samples only; no human or mobile inference.',flush=True)
    raise SystemExit(subprocess.run(['dotnet','run','--project',str(folder/'Eval.csproj'),'-c','Release','--',str(args.seeds),','.join(map(str,variants))],check=False).returncode)
