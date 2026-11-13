'use strict';

const { SnakeGame, DIRECTIONS } = require('./core');

const DESIGN = { width: 390, height: 844, boardX: 24, boardY: 194, boardSize: 342 };
const HIT = {
  primary: { x: 89, y: 375, w: 212, h: 48 },
  pause: { x: 292, y: 132, w: 74, h: 38 },
  up: { x: 163, y: 591, w: 64, h: 52 },
  left: { x: 93, y: 649, w: 64, h: 52 },
  down: { x: 163, y: 649, w: 64, h: 52 },
  right: { x: 233, y: 649, w: 64, h: 52 },
  sidebar: { x: 24, y: 713, w: 342, h: 30 },
  sidebarClose: { x: 298, y: 281, w: 30, h: 30 },
  share: { x: 248, y: 752, w: 118, h: 38 }
};

function contains(box, point) {
  return point.x >= box.x && point.x <= box.x + box.w && point.y >= box.y && point.y <= box.y + box.h;
}

function rounded(ctx, x, y, w, h, radius, color) {
  const r = Math.min(radius, w / 2, h / 2);
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y);
  ctx.quadraticCurveTo(x + w, y, x + w, y + r);
  ctx.lineTo(x + w, y + h - r);
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h);
  ctx.quadraticCurveTo(x, y + h, x, y + h - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();
  ctx.fill();
}

function text(ctx, value, x, y, size, color, align = 'left', weight = 'normal') {
  ctx.fillStyle = color;
  ctx.font = `${weight} ${size}px sans-serif`;
  ctx.textAlign = align;
  ctx.textBaseline = 'middle';
  ctx.fillText(String(value), x, y);
}

class GameApp {
  constructor(platform, options = {}) {
    this.platform = platform;
    this.canvas = platform.canvas;
    this.ctx = this.canvas.getContext('2d');
    this.canvas.width = Math.round(platform.width * platform.pixelRatio);
    this.canvas.height = Math.round(platform.height * platform.pixelRatio);
    this.ctx.scale(platform.pixelRatio, platform.pixelRatio);
    const area = platform.safeArea || {};
    const left = Math.max(0, Math.min(platform.width, area.left || 0));
    const top = Math.max(0, Math.min(platform.height, area.top || 0));
    const right = Math.max(left, Math.min(platform.width, area.right || platform.width));
    const bottom = Math.max(top, Math.min(platform.height, area.bottom || platform.height));
    this.scale = Math.min((right - left) / DESIGN.width, (bottom - top) / DESIGN.height);
    if (!this.scale) this.scale = Math.min(platform.width / DESIGN.width, platform.height / DESIGN.height);
    this.offsetX = left + (right - left - DESIGN.width * this.scale) / 2;
    this.offsetY = top + (bottom - top - DESIGN.height * this.scale) / 2;
    this.game = new SnakeGame({ store: platform, random: options.random });
    this.now = options.now || Date.now;
    this.lastTick = this.now();
    this.touchStart = null;
    this.sidebarOpen = false;
    platform.onTouch(event => this.handleStart(event), event => this.handleEnd(event), () => { this.touchStart = null; });
    platform.onHide(() => { this.game.pause(); this.touchStart = null; });
    platform.onShow(() => { this.lastTick = this.now(); });
    platform.onKey(key => this.handleKey(key));
  }

  start() {
    const frame = () => {
      this.frame();
      this.platform.requestFrame(frame);
    };
    frame();
    return this;
  }

  frame() {
    const now = this.now();
    if (this.game.status === 'running' && now - this.lastTick >= this.game.interval()) {
      this.game.step();
      this.lastTick = now;
    } else if (this.game.status !== 'running') {
      this.lastTick = now;
    }
    this.render();
  }

  point(event, end) {
    const touches = end ? event.changedTouches : event.touches;
    const touch = touches && touches[0];
    if (!touch) return null;
    const x = touch.clientX === undefined ? touch.x : touch.clientX;
    const y = touch.clientY === undefined ? touch.y : touch.clientY;
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    return { x: (x - this.offsetX) / this.scale, y: (y - this.offsetY) / this.scale };
  }

  handleStart(event) { this.touchStart = this.point(event, false); }

  handleEnd(event) {
    const start = this.touchStart;
    this.touchStart = null;
    const end = this.point(event, true);
    if (!start || !end) return;
    const dx = end.x - start.x;
    const dy = end.y - start.y;
    if (Math.max(Math.abs(dx), Math.abs(dy)) > 24) {
      const direction = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up');
      this.game.turn(direction);
      return;
    }
    this.tap(end);
  }

  tap(point) {
    if (this.sidebarOpen) {
      if (contains(HIT.sidebarClose, point)) { this.sidebarOpen = false; return; }
      if (contains(HIT.primary, point)) {
        const sidebar = this.platform.sidebarState();
        if (sidebar.rewardClaimed) this.sidebarOpen = false;
        else if (sidebar.fromSidebar) this.platform.claimSidebarReward();
        else this.platform.navigateSidebar();
      }
      return;
    }
    if (this.platform.sidebarState && this.platform.sidebarState().available && contains(HIT.sidebar, point)) {
      this.game.pause();
      this.sidebarOpen = true;
      return;
    }
    if (contains(HIT.share, point)) { this.platform.share(); return; }
    if (contains(HIT.primary, point) && this.game.status !== 'running') {
      if (this.game.status === 'ready') this.game.start();
      else if (this.game.status === 'paused') this.game.resume();
      else this.game.restart();
      this.lastTick = this.now();
      return;
    }
    if (contains(HIT.pause, point)) {
      if (this.game.status === 'running') this.game.pause();
      else if (this.game.status === 'paused') { this.game.resume(); this.lastTick = this.now(); }
      return;
    }
    for (const direction of Object.keys(DIRECTIONS)) {
      if (contains(HIT[direction], point)) { this.game.turn(direction); return; }
    }
  }

  handleKey(key) {
    const directions = { ArrowUp: 'up', w: 'up', W: 'up', ArrowDown: 'down', s: 'down', S: 'down', ArrowLeft: 'left', a: 'left', A: 'left', ArrowRight: 'right', d: 'right', D: 'right' };
    if (directions[key]) this.game.turn(directions[key]);
    else if (key === ' ' || key === 'Enter') {
      if (this.game.status === 'running') this.game.pause();
      else if (this.game.status === 'paused') this.game.resume();
      else if (this.game.status === 'ready') this.game.start();
      else this.game.restart();
      this.lastTick = this.now();
    }
  }

  render() {
    const ctx = this.ctx;
    ctx.fillStyle = '#0b1715';
    ctx.fillRect(0, 0, this.platform.width, this.platform.height);
    ctx.save();
    ctx.translate(this.offsetX, this.offsetY);
    ctx.scale(this.scale, this.scale);
    this.drawHeader(ctx);
    this.drawBoard(ctx);
    this.drawControls(ctx);
    if (this.game.status !== 'running') this.drawOverlay(ctx);
    if (this.sidebarOpen) this.drawSidebar(ctx);
    ctx.restore();
  }

  drawHeader(ctx) {
    rounded(ctx, 24, 36, 31, 31, 10, '#c8f36b');
    text(ctx, 'S', 39.5, 51.5, 17, '#182b20', 'center', 'bold');
    text(ctx, 'SNAKE CLUB', 65, 52, 12, '#c8f36b', 'left', 'bold');
    text(ctx, '轻松玩 · 慢慢长', 24, 96, 12, '#8ba69c');
    text(ctx, '贪吃蛇', 24, 124, 26, '#f5f8ea', 'left', 'bold');
    text(ctx, '得分', 24, 168, 11, '#8ba69c');
    text(ctx, this.game.score, 57, 168, 17, '#f5f8ea', 'left', 'bold');
    text(ctx, '最佳', 119, 168, 11, '#8ba69c');
    text(ctx, this.game.best, 152, 168, 17, '#c8f36b', 'left', 'bold');
    rounded(ctx, HIT.pause.x, HIT.pause.y, HIT.pause.w, HIT.pause.h, 14, '#263b32');
    text(ctx, this.game.status === 'paused' ? '继续' : '暂停', 329, 151, 13, '#e4eddf', 'center', 'bold');
  }

  drawBoard(ctx) {
    const { boardX: x, boardY: y, boardSize: size } = DESIGN;
    const cell = size / this.game.cols;
    rounded(ctx, x - 5, y - 5, size + 10, size + 10, 16, '#30473b');
    ctx.fillStyle = '#1a2d24';
    ctx.fillRect(x, y, size, size);
    for (let row = 0; row < this.game.rows; row += 1) {
      for (let col = 0; col < this.game.cols; col += 1) {
        if ((row + col) % 2 === 0) {
          ctx.fillStyle = '#20372a';
          ctx.fillRect(x + col * cell, y + row * cell, cell, cell);
        }
      }
    }
    if (this.game.food) {
      const fx = x + (this.game.food.x + 0.5) * cell;
      const fy = y + (this.game.food.y + 0.5) * cell;
      ctx.beginPath(); ctx.fillStyle = '#ffba73'; ctx.arc(fx, fy, cell * 0.34, 0, Math.PI * 2); ctx.fill();
      ctx.beginPath(); ctx.fillStyle = '#f4e2ab'; ctx.arc(fx - cell * 0.10, fy - cell * 0.10, cell * 0.08, 0, Math.PI * 2); ctx.fill();
    }
    this.game.snake.slice().reverse().forEach((part, reverseIndex) => {
      const isHead = reverseIndex === this.game.snake.length - 1;
      const unlocked = this.platform.sidebarState && this.platform.sidebarState().rewardClaimed;
      const color = unlocked ? (isHead ? '#a2f2da' : '#5ccfae') : (isHead ? '#d9fa83' : '#a8df65');
      rounded(ctx, x + part.x * cell + 1, y + part.y * cell + 1, cell - 2, cell - 2, cell * 0.31, color);
      if (isHead) {
        const d = DIRECTIONS[this.game.direction];
        const cx = x + (part.x + 0.5) * cell + d.x * cell * 0.14;
        const cy = y + (part.y + 0.5) * cell + d.y * cell * 0.14;
        ctx.fillStyle = '#213628';
        for (const side of [-1, 1]) {
          ctx.beginPath();
          ctx.arc(cx + (-d.y) * side * cell * 0.18, cy + d.x * side * cell * 0.18, cell * 0.055, 0, Math.PI * 2);
          ctx.fill();
        }
      }
    });
  }

  drawControls(ctx) {
    text(ctx, '滑动屏幕或点击方向键', 24, 563, 12, '#a6bcb0');
    text(ctx, '方向控制', 366, 563, 11, '#6e8b7b', 'right');
    const arrows = { up: '▲', left: '◀', down: '▼', right: '▶' };
    for (const direction of Object.keys(arrows)) {
      const box = HIT[direction];
      rounded(ctx, box.x, box.y + 3, box.w, box.h, 15, '#14271e');
      rounded(ctx, box.x, box.y, box.w, box.h - 3, 15, '#30483b');
      text(ctx, arrows[direction], box.x + box.w / 2, box.y + 24, 17, '#e0eddb', 'center');
    }
    if (this.platform.sidebarState && this.platform.sidebarState().available) {
      const claimed = this.platform.sidebarState().rewardClaimed;
      rounded(ctx, HIT.sidebar.x, HIT.sidebar.y, HIT.sidebar.w, HIT.sidebar.h, 11, '#385340');
      text(ctx, claimed ? '✦  侧边栏福利 · 薄荷配色已解锁' : '✦  侧边栏福利 · 解锁薄荷配色', 195, 728, 12, '#d9f4bd', 'center', 'bold');
    }
    text(ctx, '吃掉果实，刷新纪录', 24, 771, 11, '#809b8d');
    rounded(ctx, HIT.share.x, HIT.share.y, HIT.share.w, HIT.share.h, 13, '#263b32');
    text(ctx, '↗  分享游戏', HIT.share.x + HIT.share.w / 2, HIT.share.y + 19, 12, '#d4e5ce', 'center', 'bold');
  }

  drawOverlay(ctx) {
    const status = this.game.status;
    ctx.fillStyle = 'rgba(9, 20, 15, 0.76)';
    ctx.fillRect(DESIGN.boardX, DESIGN.boardY, DESIGN.boardSize, DESIGN.boardSize);
    rounded(ctx, 54, 274, 282, 170, 22, '#f0f6e4');
    const title = { ready: '准备出发？', paused: '先休息一下', gameover: '游戏结束', won: '太棒了！' }[status];
    const hint = { ready: '吃果实，别撞到自己哦', paused: '准备好了就继续吧', gameover: `本次得分 ${this.game.score} · 最佳 ${this.game.best}`, won: `棋盘已填满 · 得分 ${this.game.score}` }[status];
    const button = { ready: '开始游戏', paused: '继续游戏', gameover: '再玩一次', won: '再玩一次' }[status];
    text(ctx, title, 195, 309, 22, '#1b3022', 'center', 'bold');
    text(ctx, hint, 195, 340, 12, '#728373', 'center');
    rounded(ctx, HIT.primary.x, HIT.primary.y, HIT.primary.w, HIT.primary.h, 15, '#bde96d');
    text(ctx, button, 195, 399, 15, '#1d3322', 'center', 'bold');
    if (status === 'ready') {
      text(ctx, '健康游戏忠告', 195, 459, 11, '#b4c9b8', 'center', 'bold');
      text(ctx, '抵制不良游戏，拒绝盗版游戏。', 195, 475, 11, '#a7bdad', 'center');
      text(ctx, '注意自我保护，谨防受骗上当。', 195, 489, 11, '#a7bdad', 'center');
      text(ctx, '适度游戏益脑，沉迷游戏伤身。', 195, 503, 11, '#a7bdad', 'center');
      text(ctx, '合理安排时间，享受健康生活。', 195, 517, 11, '#a7bdad', 'center');
    }
  }

  drawSidebar(ctx) {
    const sidebar = this.platform.sidebarState();
    ctx.fillStyle = 'rgba(9, 20, 15, 0.88)';
    ctx.fillRect(DESIGN.boardX, DESIGN.boardY, DESIGN.boardSize, DESIGN.boardSize);
    rounded(ctx, 54, 274, 282, 170, 22, '#f0f6e4');
    text(ctx, '×', 313, 296, 23, '#5a705c', 'center');
    text(ctx, '侧边栏福利', 195, 309, 22, '#1b3022', 'center', 'bold');
    if (sidebar.rewardClaimed) {
      text(ctx, '薄荷配色已解锁', 195, 341, 12, '#728373', 'center');
    } else if (sidebar.fromSidebar) {
      text(ctx, '欢迎回来，领取专属薄荷配色', 195, 341, 12, '#728373', 'center');
    } else {
      text(ctx, '前往首页侧边栏，点击贪吃蛇', 195, 336, 12, '#728373', 'center');
      text(ctx, '返回游戏即可领取薄荷配色', 195, 353, 12, '#728373', 'center');
    }
    rounded(ctx, HIT.primary.x, HIT.primary.y, HIT.primary.w, HIT.primary.h, 15, '#bde96d');
    text(ctx, sidebar.rewardClaimed ? '完成' : sidebar.fromSidebar ? '领取薄荷配色' : '去首页侧边栏', 195, 399, 15, '#1d3322', 'center', 'bold');
  }
}

module.exports = { GameApp, DESIGN, HIT };
