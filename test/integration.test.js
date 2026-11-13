'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { GameApp } = require('../src/app');
const { createMiniPlatform, BEST_KEY, SIDEBAR_REWARD_KEY } = require('../src/platform');

function mockApi() {
  const events = {};
  const data = new Map();
  const calls = { draws: 0, shares: [], menu: 0 };
  const context = new Proxy({}, {
    get(object, key) { return key in object ? object[key] : (...args) => { if (key === 'fillRect') calls.draws += 1; }; },
    set(object, key, value) { object[key] = value; return true; }
  });
  const canvas = { getContext: () => context };
  const api = {
    createCanvas: () => canvas,
    getSystemInfoSync: () => ({ windowWidth: 390, windowHeight: 844, pixelRatio: 2, safeArea: { left: 0, top: 0, right: 390, bottom: 844 } }),
    getStorageSync: key => data.get(key),
    setStorageSync: (key, value) => data.set(key, value),
    onTouchStart: cb => { events.start = cb; }, onTouchEnd: cb => { events.end = cb; }, onTouchCancel: cb => { events.cancel = cb; },
    onHide: cb => { events.hide = cb; }, onShow: cb => { events.show = cb; },
    onShareAppMessage: cb => { events.share = cb; },
    showShareMenu: () => { calls.menu += 1; },
    shareAppMessage: options => { calls.shares.push(options); }
  };
  function touch(x1, y1, x2 = x1, y2 = y1) {
    events.start({ touches: [{ clientX: x1, clientY: y1 }] });
    events.end({ changedTouches: [{ clientX: x2, clientY: y2 }] });
  }
  return { api, events, data, calls, canvas, touch };
}

test('mini-game input, loop, storage, sharing and background pause work together', () => {
  const host = mockApi();
  const platform = createMiniPlatform(host.api);
  let now = 1000;
  const app = new GameApp(platform, { now: () => now, random: () => 0 });
  app.frame();
  assert.ok(host.calls.draws > 0);
  assert.equal(host.canvas.width, 780);
  assert.equal(host.events.share().title, '来玩贪吃蛇');
  assert.equal(host.calls.menu, 1);
  host.touch(195, 399);
  assert.equal(app.game.status, 'running');
  host.touch(180, 300, 180, 250);
  now += 200;
  const initialHead = { ...app.game.snake[0] };
  app.frame();
  assert.deepEqual(app.game.snake[0], { x: initialHead.x, y: initialHead.y - 1 });
  host.touch(300, 765);
  assert.equal(host.calls.shares.length, 1);
  host.events.hide();
  assert.equal(app.game.status, 'paused');
  now += 100000;
  host.events.show();
  host.touch(195, 399);
  app.frame();
  assert.equal(app.game.status, 'running');
  assert.deepEqual(app.game.snake[0], { x: initialHead.x, y: initialHead.y - 1 });
  platform.writeBest(12);
  assert.equal(host.data.get(BEST_KEY), 12);
});

test('missing optional APIs and a throwing storage remain playable', () => {
  const host = mockApi();
  delete host.api.onShareAppMessage;
  delete host.api.showShareMenu;
  delete host.api.shareAppMessage;
  host.api.getStorageSync = () => { throw Error('no storage'); };
  const platform = createMiniPlatform(host.api);
  const app = new GameApp(platform);
  assert.equal(app.game.best, 0);
  assert.equal(platform.share(), false);
  assert.doesNotThrow(() => app.frame());
});

test('Douyin sidebar reward requires a current sidebar launch and persists once claimed', () => {
  const host = mockApi();
  const navigations = [];
  const sidebar = {
    checkScene(options) { assert.equal(options.scene, 'sidebar'); options.success({ isExist: true }); },
    navigateToScene(options) { navigations.push(options.scene); }
  };
  host.api.getLaunchOptionsSync = () => ({});
  const platform = createMiniPlatform(host.api, sidebar);
  const app = new GameApp(platform);
  assert.equal(platform.sidebarState().available, true);
  assert.equal(platform.claimSidebarReward(), false);
  host.touch(195, 728);
  assert.equal(app.sidebarOpen, true);
  host.touch(195, 399);
  assert.deepEqual(navigations, ['sidebar']);
  assert.equal(platform.sidebarState().rewardClaimed, false);
  host.events.hide();
  host.events.show({ launch_from: 'homepage', location: 'sidebar_card' });
  host.touch(195, 399);
  assert.equal(platform.sidebarState().rewardClaimed, true);
  assert.equal(host.data.get(SIDEBAR_REWARD_KEY), true);
  assert.equal(platform.claimSidebarReward(), false);
  host.events.show({ launch_from: 'other', location: 'other' });
  assert.equal(platform.sidebarState().fromSidebar, false);
  const reopened = createMiniPlatform(host.api, sidebar);
  assert.equal(reopened.sidebarState().rewardClaimed, true);
});

test('unsupported sidebar hosts do not show an entry or grant rewards', () => {
  const host = mockApi();
  const platform = createMiniPlatform(host.api, {
    checkScene(options) { options.success({ isExist: false }); },
    navigateToScene() { throw Error('must not navigate'); }
  });
  assert.equal(platform.sidebarState().available, false);
  assert.equal(platform.navigateSidebar(), false);
  assert.equal(platform.claimSidebarReward(), false);
});

test('build creates standalone Douyin and WeChat projects that start with their host APIs', t => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'snake-build-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const root = path.resolve(__dirname, '..');
  const env = { ...process.env, SNAKE_OUTPUT_DIR: directory, SNAKE_DOUYIN_APPID: 'tt-test-id', SNAKE_WECHAT_APPID: 'wx-test-id' };
  const result = spawnSync(process.execPath, ['scripts/build.js'], { cwd: root, env, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const previousFrame = global.requestAnimationFrame;
  t.after(() => { if (previousFrame === undefined) delete global.requestAnimationFrame; else global.requestAnimationFrame = previousFrame; delete global.tt; delete global.wx; });
  global.requestAnimationFrame = () => 1;
  for (const [target, name, appid] of [['douyin', 'tt', 'tt-test-id'], ['wechat', 'wx', 'wx-test-id']]) {
    const folder = path.join(directory, target);
    const config = JSON.parse(fs.readFileSync(path.join(folder, 'project.config.json'), 'utf8'));
    assert.equal(config.appid, appid);
    assert.equal(config.compileType, 'game');
    assert.equal(JSON.parse(fs.readFileSync(path.join(folder, 'game.json'), 'utf8')).deviceOrientation, 'portrait');
    if (target === 'douyin') assert.match(fs.readFileSync(path.join(folder, 'game.js'), 'utf8'), /tt\.navigateToScene/);
    const host = mockApi();
    if (target === 'douyin') {
      host.api.checkScene = options => options.success({ isExist: true });
      host.api.navigateToScene = options => { host.calls.sidebarScene = options.scene; };
    }
    global[name] = host.api;
    assert.doesNotThrow(() => require(path.join(folder, 'game.js')));
    assert.ok(host.calls.draws > 0, `${target} drew the first frame`);
    assert.equal(typeof host.events.start, 'function');
    assert.equal(typeof host.events.hide, 'function');
    host.touch(195, 399);
    host.touch(300, 765);
    assert.equal(host.calls.shares.length, 1);
    if (target === 'douyin') {
      host.touch(195, 728);
      host.touch(195, 399);
      assert.equal(host.calls.sidebarScene, 'sidebar');
      host.events.show({ launch_from: 'homepage', location: 'sidebar_card' });
      host.touch(195, 399);
      assert.equal(host.data.get(SIDEBAR_REWARD_KEY), true);
    }
  }
  assert.ok(fs.readFileSync(path.join(directory, 'browser', 'index.html'), 'utf8').includes('bundle.js'));
  assert.doesNotThrow(() => new (require('node:vm').Script)(fs.readFileSync(path.join(directory, 'browser', 'bundle.js'), 'utf8')));
  const rebuildEnv = { ...env };
  delete rebuildEnv.SNAKE_DOUYIN_APPID;
  delete rebuildEnv.SNAKE_WECHAT_APPID;
  const rebuild = spawnSync(process.execPath, ['scripts/build.js'], { cwd: root, env: rebuildEnv, encoding: 'utf8' });
  assert.equal(rebuild.status, 0, rebuild.stderr);
  assert.equal(JSON.parse(fs.readFileSync(path.join(directory, 'douyin', 'project.config.json'), 'utf8')).appid, 'tt-test-id');
});
