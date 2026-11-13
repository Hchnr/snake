'use strict';

const BEST_KEY = 'snake_best_v1';
const SIDEBAR_REWARD_KEY = 'snake_sidebar_reward_v1';
const SHARE = { title: '来玩贪吃蛇', desc: '一起挑战最高分' };

function createMiniPlatform(api, sidebarApi) {
  let sidebarAvailable = false;
  let fromSidebar = false;
  let rewardClaimed = false;
  let receivedShow = false;
  const showListeners = [];
  const updateLaunch = options => {
    const launch = options || {};
    fromSidebar = (launch.launch_from || launch.launchFrom) === 'homepage' && launch.location === 'sidebar_card';
  };
  // Register synchronously during game.js startup so a cold-start event is not missed.
  if (typeof api.onShow === 'function') {
    api.onShow(options => {
      receivedShow = true;
      updateLaunch(options);
      showListeners.forEach(listener => listener(options));
    });
  }
  if (sidebarApi) {
    try { if (!receivedShow && api.getLaunchOptionsSync) updateLaunch(api.getLaunchOptionsSync()); } catch (_) { /* Optional API. */ }
    try { rewardClaimed = api.getStorageSync && api.getStorageSync(SIDEBAR_REWARD_KEY) === true; } catch (_) { /* Optional storage. */ }
    if (typeof sidebarApi.checkScene === 'function' && typeof sidebarApi.navigateToScene === 'function') {
      try {
        sidebarApi.checkScene({ scene: 'sidebar', success(result) { sidebarAvailable = !!(result && result.isExist); } });
      } catch (_) { /* Unsupported host. */ }
    }
  }
  const canvas = api.createCanvas();
  let info = {};
  try { info = api.getSystemInfoSync() || {}; } catch (_) { /* Use safe defaults. */ }
  const width = info.windowWidth || info.screenWidth || 375;
  const height = info.windowHeight || info.screenHeight || 812;
  const pixelRatio = Math.max(1, Math.min(3, info.pixelRatio || 1));
  const safeArea = info.safeArea || { left: 0, top: 0, right: width, bottom: height };
  if (typeof api.onShareAppMessage === 'function') {
    try { api.onShareAppMessage(() => ({ ...SHARE })); } catch (_) { /* Optional API. */ }
  }
  if (typeof api.showShareMenu === 'function') {
    try { api.showShareMenu({}); } catch (_) { /* Optional API. */ }
  }
  return {
    canvas, width, height, pixelRatio, safeArea,
    readBest() { return api.getStorageSync ? api.getStorageSync(BEST_KEY) : 0; },
    writeBest(value) { if (api.setStorageSync) api.setStorageSync(BEST_KEY, value); },
    onTouch(start, end, cancel) {
      if (api.onTouchStart) api.onTouchStart(start);
      if (api.onTouchEnd) api.onTouchEnd(end);
      if (api.onTouchCancel) api.onTouchCancel(cancel);
    },
    onHide(callback) { if (api.onHide) api.onHide(callback); },
    onShow(callback) { showListeners.push(callback); },
    onKey() {},
    sidebarState() { return { available: sidebarAvailable, fromSidebar, rewardClaimed: !!rewardClaimed }; },
    navigateSidebar() {
      if (!sidebarAvailable || !sidebarApi || !sidebarApi.navigateToScene) return false;
      try {
        sidebarApi.navigateToScene({ scene: 'sidebar', fail() {
          if (api.showToast) api.showToast({ title: '暂时无法打开侧边栏', icon: 'none' });
        } });
        return true;
      } catch (_) {
        if (api.showToast) api.showToast({ title: '暂时无法打开侧边栏', icon: 'none' });
        return false;
      }
    },
    claimSidebarReward() {
      if (!sidebarAvailable || !fromSidebar || rewardClaimed) return false;
      rewardClaimed = true;
      try { if (api.setStorageSync) api.setStorageSync(SIDEBAR_REWARD_KEY, true); } catch (_) { /* Reward stays unlocked for this session. */ }
      return true;
    },
    requestFrame(callback) {
      if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(callback);
      return setTimeout(callback, 16);
    },
    share() {
      if (typeof api.shareAppMessage !== 'function') {
        if (api.showToast) api.showToast({ title: '当前环境暂不支持分享', icon: 'none' });
        return false;
      }
      try {
        api.shareAppMessage({ ...SHARE, fail() {
          if (api.showToast) api.showToast({ title: '分享暂不可用', icon: 'none' });
        } });
        return true;
      } catch (_) {
        if (api.showToast) api.showToast({ title: '分享暂不可用', icon: 'none' });
        return false;
      }
    }
  };
}

module.exports = { createMiniPlatform, BEST_KEY, SIDEBAR_REWARD_KEY, SHARE };
