#!/usr/bin/env python3
"""Compare v15 with v16 in the actual deterministic offline rules.

Seven scripted policies are not representative of people, inputs may diverge
as the simulation evolves, and these counts do not prove player outcomes.
Policy 0 is no steering; 1 is straight with boost; 2/3 pursue the nearest
tail without/with periodic boost; 4/5 pursue fruit likewise; 6 boosts late.
Fruit counts are instrumented in a disposable core copy at the actual pickup.
The expiry window is truncated if a round finishes before tick 360.
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
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('seeds must be 1..10000')
source = args.root.resolve() / 'unity/Assets/Snake/Core'
if not (source / 'PveFixedWorld.cs').is_file():
    parser.error('missing core sources')
program = r'''using SnakeClub.Core;
int count=int.Parse(args[0]);
foreach (bool openingFruit in new[] {false,true})
foreach (bool first in new[] {false,true})
for (int policy=0;policy<7;policy++)
{
    int bite15=0, damage15=0, damageFirst=0, deaths15=0, kills15=0, deaths60=0, kills60=0, expirySamples=0, expiryDamaged=0, finished60=0, wins60=0, diedBeforeBite=0;
    long fruits15=0, fruits60=0;
    long eaten15=0,lost15=0,length15=0,eaten60=0,lost60=0,spent=0;
    for(int seed=1;seed<=count;seed++)
    {
        var world=new PveFixedWorld(seed,first,null,true,localAdvantage:true,localOpeningFood:true,
            localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,
            localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,
            localMultiBite:true,localHeadTieAfterBite:true,localOpeningBoost:true,localFreeBoost:true,localOpeningDefense:true,localOpeningFruit:openingFruit);
        if (world.RulesVersion != (openingFruit ? PveFixedRecording.LocalStrengthOpeningFruitRules : PveFixedRecording.LocalStrengthOpeningDefenseRules))
            throw new InvalidOperationException("Expected explicit v15 or v16 local rules");
        byte heading=0; int firstB=-1,firstD=-1; bool saw15=false, observedExpiry=false, hurtAfterExpiry=false; int expiryLoss=0;
        while(!world.Finished && world.Tick<1200)
        {
            if (world.Tick%8==0 && policy>=2 && policy<=5)
            {
                var state=world.RenderState(); var me=state.snakes[0].segments[0]; double best=double.MaxValue; float tx=0,ty=0;
                if(policy==4 || policy==5)
                    foreach(var fruit in world.Fruit)
                    { double dx=fruit.position.x/1000.0-me.x,dy=fruit.position.y/1000.0-me.y,d=dx*dx+dy*dy; if(d<best){best=d;tx=(float)dx;ty=(float)dy;} }
                else
                    foreach(var other in state.snakes.Skip(1))
                    { if(!other.alive || other.segments.Count<=5)continue;var tail=other.segments[^1];double dx=tail.x-me.x,dy=tail.y-me.y,d=dx*dx+dy*dy;if(d<best){best=d;tx=(float)dx;ty=(float)dy;} }
                if(best<double.MaxValue) heading=(byte)(((int)Math.Round(Math.Atan2(ty,tx)*128/Math.PI)+256)&255);
            }
            bool boost=policy==1 || ((policy==3 || policy==5) && world.Tick%80<30) || (policy==6 && world.Tick>=80 && world.Tick<180);
            world.Step(heading,boost,policy>=2 && policy<=5);
            if(firstB<0 && world.PlayerSegmentsEaten>0)firstB=world.Tick;
            if(firstD<0 && world.PlayerSegmentsLostToBites>0)firstD=world.Tick;
            if (observedExpiry && world.Tick <= 360 && world.PlayerSegmentsLostToBites > expiryLoss) hurtAfterExpiry=true;
            if(world.Tick==300)
            {
                if(world.Fighters[0].alive && !world.Finished) { observedExpiry=true; expiryLoss=world.PlayerSegmentsLostToBites; }
                saw15=true; fruits15+=world.EvalPlayerFruitCount; if(firstB>0)bite15++;if(firstD>0)damage15++;if(firstD>0&&(firstB<0||firstD<firstB))damageFirst++;
                if(!world.Fighters[0].alive)deaths15++;kills15+=world.Fighters[0].kills;
                eaten15+=world.PlayerSegmentsEaten;lost15+=world.PlayerSegmentsLostToBites;length15+=world.Fighters[0].motion.Length;
            }
        }
        if(!saw15)
        {
            fruits15+=world.EvalPlayerFruitCount;
            if(firstB>0)bite15++;if(firstD>0)damage15++;if(firstD>0&&(firstB<0||firstD<firstB))damageFirst++;
            if(!world.Fighters[0].alive)deaths15++;kills15+=world.Fighters[0].kills;
            eaten15+=world.PlayerSegmentsEaten;lost15+=world.PlayerSegmentsLostToBites;length15+=world.Fighters[0].motion.Length;
        }
        fruits60+=world.EvalPlayerFruitCount;
        if(observedExpiry) { expirySamples++; if(hurtAfterExpiry) expiryDamaged++; }
        if(world.Finished) { finished60++; if(world.Winner=="you") wins60++; }
        if(!world.Fighters[0].alive) { deaths60++; if(firstB<0) diedBeforeBite++; } kills60+=world.Fighters[0].kills;
        eaten60+=world.PlayerSegmentsEaten;lost60+=world.PlayerSegmentsLostToBites;spent+=world.FreeBoostTicksUsed;
    }
    Console.WriteLine($"version={(openingFruit ? 16 : 15)} fruits15={fruits15} fruits60={fruits60} first={first} policy={policy} n={count} bite15={bite15} damage15={damage15} damageFirst={damageFirst} dead15={deaths15} kills15={kills15} eaten15={eaten15} lost15={lost15} length15={length15} dead60={deaths60} kills60={kills60} eaten60={eaten60} lost60={lost60} spent={spent} expirySamples={expirySamples} expiryDamaged={expiryDamaged} finished60={finished60} wins60={wins60} diedBeforeBite={diedBeforeBite}");
}
'''
with tempfile.TemporaryDirectory(prefix='snake-opening-fruit-eval-') as temp:
    folder = Path(temp)
    shutil.copytree(source, folder / 'Core')
    fingerprint = hashlib.sha256()
    for item in sorted((folder / 'Core').glob('*.cs')):
        fingerprint.update(item.name.encode('utf-8') + b'\0' + item.read_bytes() + b'\0')
    world_source = folder / 'Core/PveFixedWorld.cs'
    world_code = world_source.read_text()
    marker = '        public const int TickRate = 20;'
    pickup = '                    fighter.motion.Grow(1);\n                    if (StrengthPreview) AwardStrengthExperience(fighter.id, 1);'
    if world_code.count(marker) != 1 or world_code.count(pickup) != 1:
        raise SystemExit('Fruit-count instrumentation anchors changed; inspect before evaluation.')
    world_code = world_code.replace(marker, marker + '\n        public int EvalPlayerFruitCount;')
    world_code = world_code.replace(pickup, '                    if (j == 0) EvalPlayerFruitCount++;\n' + pickup)
    world_source.write_text(world_code)
    (folder / 'Program.cs').write_text(program)
    (folder / 'Eval.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="Core/*.cs"/></ItemGroup></Project>')
    print(f'core_sha256={fingerprint.hexdigest()} seeds=1..{args.seeds}', flush=True)
    print('Fixed offline input samples only; no human or mobile inference.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(folder / 'Eval.csproj'), '-c', 'Release', '--', str(args.seeds)], check=False).returncode)
