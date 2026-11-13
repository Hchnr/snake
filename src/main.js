'use strict';

const { GameApp } = require('./app');
const { createMiniPlatform } = require('./platform');

function startMiniGame(api, sidebarApi) {
  return new GameApp(createMiniPlatform(api, sidebarApi)).start();
}

module.exports = { startMiniGame };
