#!/usr/bin/env python3
"""Sample actual body-threat projections and the offscreen warning candidate.

Assumes an immediately centered 70-degree orthographic camera, 540x960 screen,
standard view, and samples every 10 ticks in 60 seconds. Does not model camera
lag, marker extent, UI or sprite occlusion, actual touch, phones, or humans.
Policies: 0 straight without steering; 1 chase fruit; 2 chase tail; 3 chase
fruit with pulsed boost. Damage windows count any body loss in the following
20 ticks, not necessarily damage at the originally marked segment; samples
without a complete window are excluded. Sampling states are not independent.
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
program = r''' using SnakeClub.Core;
record struct Exposure(int tick,int lost,bool allOff,bool headWarning,int length);
class Program {
static void Main(string[] args) {
int count=int.Parse(args[0]);
foreach(bool first in new[]{false,true})for(int policy=0;policy<4;policy++) {
int snapshots=0,warning=0,off=0,visible=0,head=0,games=0,offGames=0,offNoHead=0,offLong=0,offWindow=0,offHurt=0,visibleWindow=0,visibleHurt=0,selectedSamples=0,selectedGames=0,selectedEpisodes=0,selectedSwitches=0,angle45=0,angle90=0;
for(int seed=1;seed<=count;seed++) {
var w=new PveFixedWorld(seed,first,null,true,localAdvantage:true,localOpeningFood:true,localOpeningEncounter:true,localRecoverySupply:true,localSafeRecovery:true,localAlignedStart:true,localCappedStart:true,localStableBlocked:true,localRecoveryPriority:true,localSqueezeEscape:true,localMultiBite:true,localHeadTieAfterBite:true,localOpeningBoost:true,localFreeBoost:true,localOpeningDefense:true,localOpeningFruit:true);
byte heading=0;bool had=false,hadOff=false,hadSelected=false;int previousSegment=-1;double previousAngle=0;var pending=new List<Exposure>();
while(!w.Finished && w.Tick<1200) {
 if(w.Tick%8==0 && policy>0) { var s=w.RenderState();var me=s.snakes[0].segments[0];double best=double.MaxValue,tx=0,ty=0;
 if(policy==1||policy==3)foreach(var f in w.Fruit){double dx=f.position.x/1000.0-me.x,dy=f.position.y/1000.0-me.y,d=dx*dx+dy*dy;if(d<best){best=d;tx=dx;ty=dy;}}
 else foreach(var other in s.snakes.Skip(1)){if(!other.alive||other.segments.Count<=5)continue;var t=other.segments[^1];double dx=t.x-me.x,dy=t.y-me.y,d=dx*dx+dy*dy;if(d<best){best=d;tx=dx;ty=dy;}}
 if(best<double.MaxValue)heading=(byte)(((int)Math.Round(Math.Atan2(ty,tx)*128/Math.PI)+256)&255); }
 w.Step(heading,policy==3 && w.Tick%80<30,policy>0);
 for(int i=pending.Count-1;i>=0;i--){var q=pending[i];if(w.Tick>=q.tick+20){if(q.allOff){offWindow++;if(w.PlayerSegmentsLostToBites>q.lost)offHurt++;}else{visibleWindow++;if(w.PlayerSegmentsLostToBites>q.lost)visibleHurt++;}pending.RemoveAt(i);}}
 if(w.Finished||w.Tick<(first?80:60)||w.Tick%10!=0||!w.Fighters[0].alive)continue;
 var state=w.RenderState();var player=state.snakes[0];var center=player.segments[0];double size=GrowthView.CameraSize(player.segments.Count,player.boosting,0);
 bool any=false, on=false;for(int i=1;i<player.segments.Count&&i<player.strengthThreatSegments.Count;i++)if(player.strengthThreatSegments[i]){
  any=true;var pt=player.segments[i];double dx=pt.x-center.x,dy=pt.y-center.y;double cameraUp=(.58-29)*Math.Cos(70*Math.PI/180)+(dy+10.5)*Math.Sin(70*Math.PI/180);double x=540+dx*960/size,y=960-cameraUp*960/size;if(x>=0&&x<=1080&&y>=0&&y<=1920)on=true;
 }
 Point ScreenPoint(Point p) { double dx=p.x-center.x,dy=p.y-center.y; double up=(.72-29)*Math.Cos(70*Math.PI/180)+(dy+10.5)*Math.Sin(70*Math.PI/180); return new Point((float)(540+dx*960/size),(float)(960-up*960/size)); }
 bool selected=BodyThreatEdgeIndicator.TrySelect(state,"you",ScreenPoint,new WeakBodyViewport(0,0,1080,1920),previousSegment,out bool declaredOff,out var selectedTarget);
 bool hw=!string.IsNullOrEmpty(state.strengthHeadThreat);
 if(selected&&!hw){if(!declaredOff || !player.strengthThreatSegments[selectedTarget.segment])throw new InvalidOperationException("pointer lacks actual marked body");
  Point meScreen=ScreenPoint(center); double angle=Math.Atan2(selectedTarget.screen.y-meScreen.y,selectedTarget.screen.x-meScreen.x);
  if(previousSegment<0)selectedEpisodes++;else {if(previousSegment!=selectedTarget.segment)selectedSwitches++;double delta=Math.Abs(Math.Atan2(Math.Sin(angle-previousAngle),Math.Cos(angle-previousAngle)));if(delta>Math.PI/4)angle45++;if(delta>Math.PI/2)angle90++;}
  previousAngle=angle;previousSegment=selectedTarget.segment;selectedSamples++;hadSelected=true;}
 else previousSegment=-1;
 snapshots++;if(!any)continue;warning++;had=true;if(hw)head++;
 if(on)visible++;else{off++;hadOff=true;if(!hw)offNoHead++;if(player.segments.Count>=32)offLong++;}
 pending.Add(new Exposure(w.Tick,w.PlayerSegmentsLostToBites,!on,hw,player.segments.Count));
}
if(had)games++;if(hadOff)offGames++;if(hadSelected)selectedGames++;
}
Console.WriteLine($"first={first} policy={policy} n={count} samples={snapshots} warning={warning} visible={visible} onlyOffscreen={off} offNoHead={offNoHead} offLong={offLong} head={head} warningGames={games} offGames={offGames} offWindow={offWindow} offHurt={offHurt} visibleWindow={visibleWindow} visibleHurt={visibleHurt} selectedSamples={selectedSamples} selectedGames={selectedGames} selectedEpisodes={selectedEpisodes} selectedSwitches={selectedSwitches} angle45={angle45} angle90={angle90}");
}
}}

'''
with tempfile.TemporaryDirectory(prefix='snake-body-threat-eval-') as temp:
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
