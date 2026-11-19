#!/usr/bin/env python3
"""Check strength-match camera resets in a disposable, non-graphical Unity project."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get('UNITY_EDITOR', '/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity'))
if not editor.is_file():
    sys.exit('Unity Editor not found; set UNITY_EDITOR.')

fixture = r'''using System;
using System.IO;
using System.Reflection;
using SnakeClub;
using SnakeClub.Core;
using UnityEngine;

public static class StrengthCameraRestartRegression
{
    public static void Run()
    {
        var root = new GameObject("Strength camera restart regression");
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var renderer = root.AddComponent<ArenaRenderer>();
            typeof(ArenaRenderer).GetMethod("Initialize", flags).Invoke(renderer, null);
            var view = (Camera)typeof(ArenaRenderer).GetField("view", flags).GetValue(renderer);
            renderer.StrengthBodyVisual = true;
            PveFixedWorld World(int seed, int x, int y, int length)
            {
                var world = new PveFixedWorld(seed, false, null, true, localAdvantage: true,
                    localOpeningFood: true, localOpeningEncounter: true, localRecoverySupply: true);
                var actors = (PveFixedFighter[])typeof(PveFixedWorld).GetField("fighters", flags).GetValue(world);
                actors[0] = new PveFixedFighter("you", new PveFixedMotion(x, y, 0, length));
                return world;
            }
            void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
            void AtPlayer(PveFixedWorld world)
            {
                var player = world.Fighters[0].motion;
                var expected = new Vector3(player.X / 1000f, 29, player.Y / 1000f - 10.5f);
                Require(Vector3.Distance(view.transform.position, expected) < .001f, "camera did not start at the new player");
            }
            var first = World(1, -80000, -20000, 48);
            var firstState = first.RenderState();
            renderer.ViewPreference = 1; renderer.Draw(firstState, "you"); AtPlayer(first);
            bool initialSizeCorrect = Mathf.Abs(view.orthographicSize - GrowthView.CameraSize(48, false, 1)) < .001f;
            first.Fighters[0].alive = false;
            Require(ReferenceEquals(firstState, first.RenderState()), "fixture requires a stable projection within a match");
            renderer.Draw(firstState, "you");
            var second = World(2, 80000, 20000, 16);
            var secondState = second.RenderState();
            renderer.ViewPreference = -1; renderer.Draw(secondState, "you"); AtPlayer(second);
            Require(initialSizeCorrect, "initial size did not match current growth and preference");
            Require(Mathf.Abs(view.orthographicSize - GrowthView.CameraSize(16, false, -1)) < .001f, "retry inherited prior growth zoom");
            renderer.MotionPaused = true;
            var paused = view.transform.position + new Vector3(6, 0, 0);
            view.transform.position = paused;
            renderer.Draw(second.RenderState(), "you");
            Require(Vector3.Distance(view.transform.position, paused) < .001f, "same-match pause unexpectedly reset the camera");
            renderer.StrengthBodyVisual = false;
            renderer.Draw(first.RenderState(), "you");
            Require(Vector3.Distance(view.transform.position, paused) < .001f, "legacy state change unexpectedly reset the paused camera");
            renderer.StrengthBodyVisual = true;
            renderer.Draw(second.RenderState(), "you"); AtPlayer(second);
            File.WriteAllText(Environment.GetEnvironmentVariable("SNAKE_CAMERA_TEST_STAMP"), "passed");
            Debug.Log("PASS Unity strength camera restarts at the new player");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}'''

with tempfile.TemporaryDirectory(prefix='snake-strength-camera-') as directory:
    temp = Path(directory)
    project = temp / 'project'
    project.mkdir()
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(root / 'unity' / name, project / name,
                        ignore=shutil.ignore_patterns('ByteGame', 'ByteGame.meta', 'ReferenceSkins', 'ReferenceSkins.meta', '.DS_Store'))
    (project / 'Assets/Editor/StrengthCameraRestartRegression.cs').write_text(fixture, encoding='utf-8')
    log = temp / 'unity.log'
    stamp = temp / 'passed'
    env = dict(os.environ, SNAKE_CAMERA_TEST_STAMP=str(stamp))
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-projectPath', str(project),
               '-executeMethod', 'StrengthCameraRestartRegression.Run', '-logFile', str(log)]
    print('Checking strength camera restarts without a visible editor...', flush=True)
    result = subprocess.run(command, env=env, timeout=300, check=False)
    output = log.read_text(errors='replace') if log.exists() else ''
    if result.returncode or not stamp.exists():
        print('\n'.join(output.splitlines()[-100:]), file=sys.stderr)
        sys.exit(result.returncode or 1)
    print('PASS Unity strength camera restarts at the new player')
