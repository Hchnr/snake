#!/usr/bin/env python3
"""Sample current weak-body guidance in fixed offline matches.

Projection assumes an immediately centered 70-degree orthographic camera,
540x960 viewport and standard view. It omits camera/zoom lag, overlays,
rendered marker extent, real touch, mobile hardware and human decisions.
Policy 0 remains unsteered; 1 follows nearest fruit, 2 nearest tail,
3 follows fruit while pulsing boost. Samples are taken every 10 ticks.
"""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=200)
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('seeds must be 1..10000')
source = Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core'
program = r'''using SnakeClub.Core;
int count = int.Parse(args[0]);
foreach (bool first in new[] { false, true })
for (int policy = 0; policy < 4; policy++)
{
    int snapshots=0, hasVisible=0, onlyOffscreen=0, noWithin30=0, genericMissing=0, genericDifferent=0, genericAngle45=0;
    int gamesOnlyOffscreen=0, gamesNoArrow=0, gamesWrongDirection=0, selectedSamples=0, gamesSelected=0, selectedEpisodes=0, selectedSwitches=0;
    for (int seed=1; seed<=count; seed++)
    {
        var w=new PveFixedWorld(seed,first,null,true,localAdvantage:true,localOpeningFood:true,
            localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,
            localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,
            localMultiBite:true,localHeadTieAfterBite:true,localOpeningBoost:true,localFreeBoost:true);
        byte heading=0; bool gameOff=false, gameMissing=false, gameWrong=false, gameSelected=false; string previousId=""; int previousPart=-1;
        while (!w.Finished && w.Tick<300)
        {
            if (w.Tick%8==0 && (policy==1 || policy==2 || policy==3))
            {
                var s=w.RenderState(); var me=s.snakes[0].segments[0]; double best=double.MaxValue, tx=0,ty=0;
                if (policy==1 || policy==3)
                    foreach(var fruit in w.Fruit)
                    { double dx=fruit.position.x/1000.0-me.x,dy=fruit.position.y/1000.0-me.y,d=dx*dx+dy*dy; if(d<best){best=d;tx=dx;ty=dy;} }
                else
                    foreach(var other in s.snakes.Skip(1))
                    { if(!other.alive || other.segments.Count<=5)continue; var tail=other.segments[^1];double dx=tail.x-me.x,dy=tail.y-me.y,d=dx*dx+dy*dy;if(d<best){best=d;tx=dx;ty=dy;} }
                if(best<double.MaxValue) heading=(byte)(((int)Math.Round(Math.Atan2(ty,tx)*128/Math.PI)+256)&255);
            }
            w.Step(heading, policy==3 && w.Tick%80<30, policy!=0);
            if(w.Finished || w.Tick<(first?80:60) || w.Tick%10!=0 || !w.Fighters[0].alive)continue;
            var state=w.RenderState(); var player=state.snakes[0]; var center=player.segments[0];
            double size=GrowthView.CameraSize(player.segments.Count, player.boosting, 0);
            Point Screen(Point p, double height)
            {
                double dx=p.x-center.x,dy=p.y-center.y;
                double cameraUp=(height-29)*Math.Cos(70*Math.PI/180)+(dy+10.5)*Math.Sin(70*Math.PI/180);
                return new Point((float)(540+dx*960/size), (float)(960-cameraUp*960/size));
            }
            bool picked = WeakBodyEdgeIndicator.TrySelect(state, "you", p => Screen(p,.72), new WeakBodyViewport(0,0,1080,1920), previousId, previousPart, out var choice);
            if (picked)
            {
                var marked = state.snakes.Find(s => s.id == choice.id);
                if (marked == null || !marked.strengthWeakSegments[choice.segment]) throw new InvalidOperationException("Selected segment is not eligible");
                if (previousPart < 0) selectedEpisodes++;
                else if (previousId != choice.id || previousPart != choice.segment) selectedSwitches++;
                selectedSamples++; gameSelected=true; previousId=choice.id; previousPart=choice.segment;
            }
            else { previousId=""; previousPart=-1; }
            bool visible=false; double closest=double.PositiveInfinity; string nearest=""; Point weak=default;
            foreach(var snake in state.snakes.Skip(1))
            {
                if(!snake.alive)continue;
                for(int i=1;i<snake.segments.Count && i<snake.strengthWeakSegments.Count;i++)
                {
                    if(!snake.strengthWeakSegments[i])continue;
                    var p=snake.segments[i]; double dx=p.x-center.x,dy=p.y-center.y,d=dx*dx+dy*dy;
                    if(d>900)continue;
                    var screen=Screen(p,.45);
                    if(screen.x>=0 && screen.x<=1080 && screen.y>=0 && screen.y<=1920)visible=true;
                    if(d<closest){closest=d; nearest=snake.id; weak=screen;}
                }
            }
            snapshots++;
            if(visible){hasVisible++;continue;}
            if(!double.IsFinite(closest)){noWithin30++;continue;}
            onlyOffscreen++;gameOff=true;
            SnakeState generic=null; double genericD=double.PositiveInfinity; Point genericPoint=default;
            foreach(var snake in state.snakes.Skip(1))
            {
                if(!snake.alive || snake.segments.Count==0)continue;
                var screen=Screen(snake.segments[0],.72);
                if(OpponentEdgeIndicator.TryCandidate(state,"you",snake,screen,out float dist,out _) && dist<genericD)
                {generic=snake;genericD=dist;genericPoint=screen;}
            }
            if(generic==null){genericMissing++;gameMissing=true;continue;}
            if(generic.id!=nearest)genericDifferent++;
            Point meScreen=Screen(center,.72);
            double gx=genericPoint.x-meScreen.x,gy=genericPoint.y-meScreen.y, wx=weak.x-meScreen.x,wy=weak.y-meScreen.y;
            double den=Math.Sqrt((gx*gx+gy*gy)*(wx*wx+wy*wy));
            if(den>0 && (gx*wx+gy*wy)/den<Math.Cos(Math.PI/4)) {genericAngle45++;gameWrong=true;}
        }
        if(gameOff)gamesOnlyOffscreen++;if(gameMissing)gamesNoArrow++;if(gameWrong)gamesWrongDirection++;if(gameSelected)gamesSelected++;
    }
    Console.WriteLine($"first={first} policy={policy} n={count} samples={snapshots} visible={hasVisible} onlyOffscreen={onlyOffscreen} noWithin30={noWithin30} genericMissing={genericMissing} genericDifferent={genericDifferent} genericAngle45={genericAngle45} gamesOnlyOffscreen={gamesOnlyOffscreen} gamesNoArrow={gamesNoArrow} gamesWrongDirection={gamesWrongDirection} selectedSamples={selectedSamples} gamesSelected={gamesSelected} selectedEpisodes={selectedEpisodes} selectedSwitches={selectedSwitches}");
}
'''

with tempfile.TemporaryDirectory(prefix='snake-weak-edge-eval-') as temp:
    folder = Path(temp)
    shutil.copytree(source, folder / 'Core')
    fingerprint = hashlib.sha256()
    for item in sorted((folder / 'Core').glob('*.cs')):
        fingerprint.update(item.name.encode('utf-8') + b'\0' + item.read_bytes() + b'\0')
    (folder / 'Program.cs').write_text(program)
    (folder / 'Eval.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="Core/*.cs"/></ItemGroup></Project>')
    print(f'core_sha256={fingerprint.hexdigest()} seeds=1..{args.seeds}', flush=True)
    print('Approximate screen projections and fixed inputs only; no human or phone inference.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(folder / 'Eval.csproj'), '-c', 'Release', '--', str(args.seeds)], check=False).returncode)
