'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

// Compile the actual GameConfiguration source under the same symbols used by
// Unity. A local verifier must remain unavailable in every TTSDK player build,
// including targets other than WebGL. No Unity UI or window is launched.
test('local v13 verifier is disabled in all TTSDK players', { timeout: 120000 }, () => {
  const root = path.resolve(__dirname, '../..');
  const source = fs.readFileSync(path.join(root, 'unity/Assets/Snake/GameApiClient.cs'), 'utf8');
  const begin = source.indexOf('    [Serializable] public sealed class GameConfiguration');
  const end = source.indexOf('    [Serializable] public sealed class LeaderboardRow', begin);
  assert.ok(begin >= 0 && end > begin, 'GameConfiguration source boundaries changed');
  const gameConfig = source.slice(begin, end);
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-platform-gate-'));
  try {
    const scenarios = [
      { name: 'local', defines: '', expected: true },
      { name: 'douyin-webgl', defines: 'SNAKE_DOUYIN_TTSDK;UNITY_WEBGL', expected: false },
      { name: 'douyin-other-player', defines: 'SNAKE_DOUYIN_TTSDK', expected: false },
      { name: 'editor', defines: 'SNAKE_DOUYIN_TTSDK;UNITY_EDITOR', expected: true },
    ];
    for (const scenario of scenarios) {
      const dir = path.join(temp, scenario.name);
      fs.mkdirSync(dir);
      fs.writeFileSync(path.join(dir, 'Gate.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><DefineConstants>${scenario.defines}</DefineConstants><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>`);
      fs.writeFileSync(path.join(dir, 'Program.cs'), `using System;\nnamespace SnakeClub {\n${gameConfig}\n}\nclass Program { static void Main() { var c = new SnakeClub.GameConfiguration { experimentalLocalStrengthPveServer = true, allowDevLogin = true, serverUrl = "", apiUrl = "http://127.0.0.1:2568" }; if (c.LocalStrengthPveServerPreview != ${scenario.expected ? 'true' : 'false'}) throw new Exception("Incorrect local verifier gate for ${scenario.name}"); Console.WriteLine("${scenario.name}:ok"); } }\n`);
      const output = execFileSync('dotnet', ['run', '--project', path.join(dir, 'Gate.csproj'), '--configuration', 'Release', '--verbosity', 'quiet'],
        { encoding: 'utf8', timeout: 30000, env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
      assert.match(output, new RegExp(`${scenario.name}:ok`));
    }
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
});
