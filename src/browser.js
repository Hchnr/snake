'use strict';

const { GameApp } = require('./app');
const { BEST_KEY, SHARE } = require('./platform');

function createBrowserPlatform() {
  const canvas = document.getElementById('game');
  const bounds = canvas.getBoundingClientRect();
  const width = bounds.width;
  const height = bounds.height;
  function toTouch(event) {
    const current = canvas.getBoundingClientRect();
    return { clientX: (event.clientX - current.left) * width / current.width, clientY: (event.clientY - current.top) * height / current.height };
  }
  return {
    canvas, width, height, pixelRatio: Math.min(3, window.devicePixelRatio || 1),
    safeArea: { left: 0, top: 0, right: width, bottom: height },
    readBest() { return window.localStorage.getItem(BEST_KEY); },
    writeBest(value) { window.localStorage.setItem(BEST_KEY, value); },
    onTouch(start, end, cancel) {
      canvas.addEventListener('pointerdown', event => {
        canvas.setPointerCapture(event.pointerId);
        start({ touches: [toTouch(event)] });
      });
      canvas.addEventListener('pointerup', event => end({ changedTouches: [toTouch(event)] }));
      canvas.addEventListener('pointercancel', cancel);
    },
    onHide(callback) { document.addEventListener('visibilitychange', () => { if (document.hidden) callback(); }); },
    onShow(callback) { document.addEventListener('visibilitychange', () => { if (!document.hidden) callback(); }); },
    onKey(callback) {
      window.addEventListener('keydown', event => {
        if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', ' ', 'Enter'].includes(event.key)) event.preventDefault();
        if (!event.repeat) callback(event.key);
      });
    },
    requestFrame(callback) { return window.requestAnimationFrame(callback); },
    share() {
      if (navigator.share) {
        navigator.share({ title: SHARE.title, text: SHARE.desc, url: window.location.href }).catch(() => {});
        return true;
      }
      document.getElementById('preview-hint').textContent = '浏览器预览不支持分享，请在小游戏中体验。';
      return false;
    }
  };
}

new GameApp(createBrowserPlatform()).start();
