#!/usr/bin/env python3
"""Compare v6 and v7 opening turn policies in deterministic offline samples.

The policies are scripted, not player behavior; different states can change
subsequent random choices. Proximity is geometry only, not a combat verdict.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--seeds', type=int, default=100, help='consecutive seeds starting at 1')
args = parser.parse_args()
if not 1 <= args.seeds <= 10000:
    parser.error('--seeds must be between 1 and 10000')
core = escape(str(Path(__file__).resolve().parent.parent / 'unity/Assets/Snake/Core/*.cs'))
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="{core}"/></ItemGroup></Project>'''
program = r'''using SnakeClub.Core;
int samples = int.Parse(args[0]);
// Actual steering persists once it begins. In the small-first case the user
// turns slightly at tick 1 and reverses at tick 11, consuming the assist.
var policies = new (string name, int firstTick, byte heading, bool smallFirst)[] {
    ("right_now", 1, 0, false), ("up_now", 1, 64, false),
    ("diagonal_45_now", 1, 32, false), ("diagonal_135_now", 1, 96, false), ("left_now", 1, 128, false),
    ("left_tick_21", 21, 128, false), ("left_tick_40", 40, 128, false), ("left_tick_41", 41, 128, false),
    ("small_then_left", 11, 128, true)
};
foreach (bool first in new[] { false, true })
foreach (var policy in policies)
foreach (bool capped in new[] { false, true })
{
    int assist = 0, backward1 = 0, sideways1 = 0, backward10 = 0, folded = 0, exposed = 0, shieldObserved = 0, dead = 0, bites = 0;
    long eaten = 0, lost = 0;
    for (int seed = 1; seed <= samples; seed++)
    {
        var w = new PveFixedWorld(seed, first, null, true, localAdvantage: true, localOpeningFood: true,
            localOpeningEncounter: true, localRecoverySupply: true, localSafeRecovery: true,
            localAlignedStart: true, localCappedStart: capped);
        var me = w.Fighters[0]; int firstX = 0, firstY = 0; bool fold = false, near = false;
        while (!w.Finished && w.Tick < 15 * PveFixedWorld.TickRate)
        {
            int nextTick = w.Tick + 1;
            if (nextTick == policy.firstTick) { firstX = me.motion.X; firstY = me.motion.Y; }
            bool steered = policy.smallFirst || nextTick >= policy.firstTick;
            byte heading = nextTick >= policy.firstTick ? policy.heading : policy.smallFirst ? (byte)4 : (byte)0;
            w.Step(heading, false, steered);
            long forward = (long)(me.motion.X - firstX) * PveFixedMotion.DirectionX(policy.heading) +
                (long)(me.motion.Y - firstY) * PveFixedMotion.DirectionY(policy.heading);
            if (w.Tick == policy.firstTick && forward < 0) backward1++;
            // A zero dot product is perpendicular, not forward progress.
            if (w.Tick == policy.firstTick && forward == 0) sideways1++;
            if (w.Tick == policy.firstTick + 9 && forward < 0) backward10++;
            if (w.Tick >= policy.firstTick && w.Tick < policy.firstTick + 40 && me.alive)
            {
                // Ignore the three immediately trailing segments: this is a
                // geometric fold diagnostic, not a self-collision rule.
                var seg = me.motion.Segments;
                for (int i = 4; i < seg.Count; i++)
                {
                    long x = (long)me.motion.X - seg[i].x, y = (long)me.motion.Y - seg[i].y;
                    if (x*x + y*y <= 1000L*1000L) { fold = true; break; }
                }
            }
            int expiry = (first ? 4 : 3) * PveFixedWorld.TickRate;
            if (w.Tick == expiry && me.alive) shieldObserved++;
            if (w.Tick >= expiry && w.Tick < expiry + 40 && me.alive)
            {
                foreach (var other in w.Fighters)
                {
                    if (other == me || !other.alive) continue;
                    // Ignore the other head; this says only that some body
                    // segment is within 2 units, irrespective of eligibility.
                    for (int i = 1; i < other.motion.Segments.Count; i++)
                    {
                        var p = other.motion.Segments[i];
                        long x = (long)me.motion.X - p.x, y = (long)me.motion.Y - p.y;
                        if (x*x + y*y <= 2000L*2000L) { near = true; break; }
                    }
                    if (near) break;
                }
            }
        }
        if (w.OpeningTurnAssistUsed) assist++;
        if (fold) folded++; if (near) exposed++;
        if (!me.alive) dead++; if (w.PlayerSegmentsEaten > 0) bites++;
        eaten += w.PlayerSegmentsEaten; lost += w.PlayerSegmentsLostToBites;
    }
    Console.WriteLine($"firstRun={(first ? 1 : 0)} policy={policy.name} variant={(capped ? "v7" : "v6")} seeds={samples} " +
        $"assist={assist} back1={backward1} sideways1={sideways1} back10={backward10} self_near_head_after_steer_2s={folded} " +
        $"shield_window_observed={shieldObserved} opponent_body_near_after_shield={exposed} " +
        $"bite_rounds={bites} eaten={eaten} lost={lost} dead={dead}");
}'''
with tempfile.TemporaryDirectory(prefix='snake-opening-cap-eval-') as directory:
    path = Path(directory)
    (path / 'Evaluate.csproj').write_text(project, encoding='utf-8')
    (path / 'Program.cs').write_text(program, encoding='utf-8')
    print('Scripted policies only, no boost, 15s horizon or match end; random streams may diverge.', flush=True)
    print('Self/other body proximity is geometry, not self-collision, combat eligibility or player risk.', flush=True)
    raise SystemExit(subprocess.run(['dotnet', 'run', '--project', str(path / 'Evaluate.csproj'), '-c', 'Release', '--verbosity', 'quiet', '--', str(args.seeds)], check=False).returncode)
