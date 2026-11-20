#!/usr/bin/env python3
"""Compare opening-guidance overlap using fixed offline inputs.

Sample every five ticks while birth protection is active. Projection assumes
an immediately centered 70-degree orthographic camera and a 540x960 screen.
Count opponent head centers and nearby fruit centers inside the former and
candidate panels. Camera lag, actual sprite pixels, other HUD, touch, device
hardware and human choices are omitted; a projected center is not proof that
an actor or pickup is actually invisible.

Policies: 0 unsteered, 1 nearest fruit, 2 nearest opponent tail, 3 nearest
fruit with pulsed boost. Only panel geometry differs between the two passes.
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
int n=int.Parse(args[0]);
foreach(bool first in new[]{false,true}) for(int policy=0;policy<4;policy++) foreach(bool bottom in new[]{false,true}) {
 int samples=0,headCovered=0,fruitCovered=0,gamesHead=0,gamesFruit=0;
 for(int seed=1;seed<=n;seed++) {
  var w=new PveFixedWorld(seed,first,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,localMultiBite:true,localHeadTieAfterBite:true,localOpeningBoost:true,localFreeBoost:true);
  byte heading=0;bool anyHead=false,anyFruit=false;
  while(!w.Finished && w.Tick<(first?80:60)) {
   if(w.Tick%8==0 && policy!=0) {
    var s=w.RenderState();var me=s.snakes[0].segments[0];double best=double.PositiveInfinity,tx=0,ty=0;
    if(policy==1 || policy==3) foreach(var f in w.Fruit){double dx=f.position.x/1000.0-me.x,dy=f.position.y/1000.0-me.y,d=dx*dx+dy*dy;if(d<best){best=d;tx=dx;ty=dy;}}
    else foreach(var s2 in s.snakes.Skip(1)){if(!s2.alive || s2.segments.Count==0)continue;var tail=s2.segments[^1];double dx=tail.x-me.x,dy=tail.y-me.y,d=dx*dx+dy*dy;if(d<best){best=d;tx=dx;ty=dy;}}
    if(double.IsFinite(best))heading=(byte)(((int)Math.Round(Math.Atan2(ty,tx)*128/Math.PI)+256)&255);
   }
   w.Step(heading, policy==3 && w.Tick%80<30,policy!=0);
   if(w.Finished || !w.Fighters[0].alive || w.Fighters[0].shieldTicks<=0 || w.Tick%5!=0)continue;
   var state=w.RenderState();var player=state.snakes[0];var center=player.segments[0];double size=GrowthView.CameraSize(player.segments.Count,player.boosting,0);
   bool covered(Point p,double h) { double dx=p.x-center.x,dy=p.y-center.y;double up=(h-29)*Math.Cos(70*Math.PI/180)+(dy+10.5)*Math.Sin(70*Math.PI/180);double x=540+dx*960/size,y=960-up*960/size;return x>=100&&x<=980&&y>=(bottom?1790:1320)&&y<=(bottom?1890:1420); }
   bool hc=state.snakes.Skip(1).Any(s=>s.alive&&s.segments.Count>0&&covered(s.segments[0],.72));
   bool fc=w.Fruit.Any(f=>{double dx=f.position.x/1000.0-center.x,dy=f.position.y/1000.0-center.y;return dx*dx+dy*dy<=144&&covered(new Point(f.position.x/1000f,f.position.y/1000f),.52);});
   samples++;if(hc){headCovered++;anyHead=true;}if(fc){fruitCovered++;anyFruit=true;}
  }
  if(anyHead)gamesHead++;if(anyFruit)gamesFruit++;
 }
 Console.WriteLine($"first={first} policy={policy} bottom={bottom} games={n} samples={samples} headSamples={headCovered} gamesHead={gamesHead} nearbyFruitSamples={fruitCovered} gamesFruit={gamesFruit}");
}
'''

with tempfile.TemporaryDirectory(prefix='snake-guidance-occlusion-') as temp:
    folder = Path(temp)
    shutil.copytree(source, folder / 'Core')
    fingerprint = hashlib.sha256()
    for item in sorted((folder / 'Core').glob('*.cs')):
        fingerprint.update(item.name.encode('utf-8') + b'\0' + item.read_bytes() + b'\0')
    (folder / 'Program.cs').write_text(program)
    (folder / 'Eval.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="Core/*.cs"/></ItemGroup></Project>')
    print(f'core_sha256={fingerprint.hexdigest()} seeds=1..{args.seeds}', flush=True)
    print('Approximate projection and scripted inputs only; not human or device evidence.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(folder / 'Eval.csproj'), '-c', 'Release', '--', str(args.seeds)], check=False).returncode)
