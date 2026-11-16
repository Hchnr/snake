#!/bin/sh
set -eu
root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
editor=${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity}
case "${1:-}" in
  webgl) method=SnakeClub.Editor.SnakeBuilder.BuildWebGL; target=WebGL ;;
  mac) method=SnakeClub.Editor.SnakeBuilder.BuildMac; target=StandaloneOSX ;;
  *) printf '%s\n' 'Usage: sh scripts/unity-build.sh webgl|mac' >&2; exit 2 ;;
esac
if [ ! -x "$editor" ]; then
  printf '%s\n' "Unity Editor not found at $editor. Install the project's Editor and WebGL module or set UNITY_EDITOR." >&2
  exit 1
fi
SNAKE_BUILD_STAMP=$(mktemp "${TMPDIR:-/tmp}/snake-unity-build.XXXXXX")
export SNAKE_BUILD_STAMP
trap 'rm -f "$SNAKE_BUILD_STAMP"' EXIT HUP INT TERM
"$editor" -batchmode -quit -projectPath "$root_dir/unity" -buildTarget "$target" -executeMethod "$method" -logFile -
if [ ! -s "$SNAKE_BUILD_STAMP" ]; then
  printf '%s\n' 'Unity exited without completing a player build. Check the Editor output above.' >&2
  exit 1
fi
