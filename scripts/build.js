'use strict';

const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const output = process.env.SNAKE_OUTPUT_DIR ? path.resolve(process.env.SNAKE_OUTPUT_DIR) : path.join(root, 'dist');
const runtimeModules = ['core', 'platform', 'app', 'main'];

function writeJson(file, value) {
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

function buildMini(platform, globalName, envName) {
  const directory = path.join(output, platform);
  fs.mkdirSync(path.join(directory, 'src'), { recursive: true });
  for (const name of runtimeModules) fs.copyFileSync(path.join(root, 'src', `${name}.js`), path.join(directory, 'src', `${name}.js`));
  const sidebar = platform === 'douyin'
    ? `const sidebar = {\n  checkScene: typeof tt.checkScene === 'function' ? options => tt.checkScene(options) : null,\n  navigateToScene: typeof tt.navigateToScene === 'function' ? options => tt.navigateToScene(options) : null\n};\n`
    : '';
  const argument = platform === 'douyin' ? ', sidebar' : '';
  fs.writeFileSync(path.join(directory, 'game.js'), `'use strict';\n${sidebar}require('./src/main').startMiniGame(${globalName}${argument});\n`);
  writeJson(path.join(directory, 'game.json'), { deviceOrientation: 'portrait', showStatusBar: false });
  const configPath = path.join(directory, 'project.config.json');
  let existing = {};
  try { existing = JSON.parse(fs.readFileSync(configPath, 'utf8')); } catch (_) { /* First build. */ }
  const config = {
    ...existing,
    description: '贪吃蛇小游戏',
    projectname: 'snake-club',
    compileType: 'game',
    setting: { ...existing.setting, es6: true }
  };
  const appid = process.env[envName] || existing.appid || (platform === 'wechat' ? 'touristappid' : '');
  if (appid) config.appid = appid;
  else delete config.appid;
  writeJson(configPath, config);
  console.log(`Built ${platform}: ${directory}`);
}

function buildBrowser() {
  const directory = path.join(output, 'browser');
  fs.mkdirSync(directory, { recursive: true });
  fs.copyFileSync(path.join(root, 'browser', 'index.html'), path.join(directory, 'index.html'));
  const modules = ['core', 'platform', 'app', 'browser'];
  const factories = modules.map(name => {
    const source = fs.readFileSync(path.join(root, 'src', `${name}.js`), 'utf8');
    return `${JSON.stringify(`./${name}`)}: function(require, module, exports) {\n${source}\n}`;
  }).join(',\n');
  const bundle = `(function() {\n'use strict';\nconst factories = {\n${factories}\n};\nconst cache = {};\nfunction require(name) {\n  if (!factories[name]) throw new Error('Unknown module: ' + name);\n  if (!cache[name]) { const module = { exports: {} }; cache[name] = module; factories[name](require, module, module.exports); }\n  return cache[name].exports;\n}\nrequire('./browser');\n})();\n`;
  fs.writeFileSync(path.join(directory, 'bundle.js'), bundle);
  console.log(`Built browser: ${directory}`);
}

buildMini('douyin', 'tt', 'SNAKE_DOUYIN_APPID');
buildMini('wechat', 'wx', 'SNAKE_WECHAT_APPID');
buildBrowser();
