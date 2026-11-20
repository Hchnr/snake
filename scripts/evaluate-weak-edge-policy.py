#!/usr/bin/env python3
"""Compare fixed offline policies with and without following weak-body guidance.

Every eight ticks both policies chase marked weak segments within 8 world units
and the approximated viewport, else seek the closest fruit. The second policy
instead follows the offscreen selector if it has a target. Simulated view is a
centered 70-degree orthographic camera at 540x960, with no camera lag, overlays,
human reaction, physical device or actual rendering. Neither policy guarantees
safe movement. Divergent paths may also diverge AI and random state.
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
foreach(bool first in new[]{false,true})
foreach(bool pulse in new[]{false,true})
foreach(bool followOffscreen in new[]{false,true})
{
 int bites=0,damageFirst=0,deaths=0,headDeaths=0,kills=0,guideGames=0,guideDecisions=0;
 long eaten=0,lost=0,biteTicks=0;
 for(int seed=1;seed<=n;seed++)
 {
  var w=new PveFixedWorld(seed,first,null,true,localAdvantage:true,localOpeningFood:true,
   localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,
   localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,
   localMultiBite:true,localHeadTieAfterBite:true,localOpeningBoost:true,localFreeBoost:true);
  byte heading=0;int firstB=-1,firstD=-1;string prev="";int prevPart=-1;bool guided=false;
  while(!w.Finished && w.Tick<300)
  {
   if(w.Tick%8==0 && w.Fighters[0].alive)
   {
    var state=w.RenderState();var me=state.snakes[0];var head=me.segments[0];
    double size=GrowthView.CameraSize(me.segments.Count,me.boosting,0), best=double.PositiveInfinity;
    Point Project(Point p)
    {
     double up=(.72-29)*Math.Cos(70*Math.PI/180)+(p.y-head.y+10.5)*Math.Sin(70*Math.PI/180);
     return new Point((float)(540+(p.x-head.x)*960/size),(float)(960-up*960/size));
    }
    Point target=default; bool found=false;
    foreach(var other in state.snakes.Skip(1))
     if(other.alive)
      for(int j=1;j<other.segments.Count && j<other.strengthWeakSegments.Count;j++)
      {
       if(!other.strengthWeakSegments[j])continue;
       var pos=other.segments[j];double dx=pos.x-head.x,dy=pos.y-head.y,d=dx*dx+dy*dy;
       var screen=Project(pos);
       if(d<=64 && d<best && screen.x>=0 && screen.x<=1080 && screen.y>=0 && screen.y<=1920)
       {best=d;target=pos;found=true;}
      }
    bool hint=WeakBodyEdgeIndicator.TrySelect(state,"you",Project,new WeakBodyViewport(0,0,1080,1920),prev,prevPart,out var choice);
    if(hint){prev=choice.id;prevPart=choice.segment;}else{prev="";prevPart=-1;}
    if(!found && followOffscreen && hint) { target=choice.position;found=true;guided=true;guideDecisions++; }
    if(!found)
    {
     best=double.PositiveInfinity;
     foreach(var f in w.Fruit){double dx=f.position.x/1000.0-head.x,dy=f.position.y/1000.0-head.y,d=dx*dx+dy*dy;if(d<best){best=d;target=new Point(f.position.x/1000f,f.position.y/1000f);found=true;}}
    }
    if(found)
    {
     Point targetScreen=Project(target), playerScreen=Project(head);
     heading=(byte)(((int)Math.Round(Math.Atan2(-(targetScreen.y-playerScreen.y),targetScreen.x-playerScreen.x)*128/Math.PI)+256)&255);
    }
   }
   w.Step(heading,pulse && w.Tick%80<30,true);
   if(firstB<0 && w.PlayerSegmentsEaten>0)firstB=w.Tick;
   if(firstD<0 && w.PlayerSegmentsLostToBites>0)firstD=w.Tick;
  }
  if(firstB>0){bites++;biteTicks+=firstB;}
  if(firstD>0 && (firstB<0 || firstD<firstB))damageFirst++;
  if(!w.Fighters[0].alive){deaths++;if(w.Fighters[0].eliminationReason=="strength_head")headDeaths++;}
  if(guided)guideGames++;
  kills+=w.Fighters[0].kills;eaten+=w.PlayerSegmentsEaten;lost+=w.PlayerSegmentsLostToBites;
 }
 Console.WriteLine($"first={first} pulse={pulse} followOffscreen={followOffscreen} n={n} bite15={bites} meanBiteTick={(bites>0?(double)biteTicks/bites:0):F1} damageFirst={damageFirst} dead15={deaths} headDead15={headDeaths} kills15={kills} eaten15={eaten} lost15={lost} guidedGames={guideGames} guideDecisions={guideDecisions}");
}
'''

with tempfile.TemporaryDirectory(prefix='snake-weak-policy-') as temp:
    folder = Path(temp)
    shutil.copytree(source, folder / 'Core')
    fingerprint = hashlib.sha256()
    for item in sorted((folder / 'Core').glob('*.cs')):
        fingerprint.update(item.name.encode('utf-8') + b'\0' + item.read_bytes() + b'\0')
    (folder / 'Program.cs').write_text(program)
    (folder / 'Eval.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="Core/*.cs"/></ItemGroup></Project>')
    print(f'core_sha256={fingerprint.hexdigest()} seeds=1..{args.seeds}', flush=True)
    print('Scripted policy and approximate view only; no human or phone inference.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(folder / 'Eval.csproj'), '-c', 'Release', '--', str(args.seeds)], check=False).returncode)
