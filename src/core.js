'use strict';

const DIRECTIONS = Object.freeze({
  up: { x: 0, y: -1 },
  down: { x: 0, y: 1 },
  left: { x: -1, y: 0 },
  right: { x: 1, y: 0 }
});

class SnakeGame {
  constructor(options = {}) {
    this.cols = options.cols || 18;
    this.rows = options.rows || 18;
    if (this.cols < 4 || this.rows < 2) throw new Error('Board is too small');
    this.random = options.random || Math.random;
    this.store = options.store || {};
    this.botCount = options.botCount === undefined ? 3 : options.botCount;
    this.best = 0;
    try {
      const value = Number(this.store.readBest ? this.store.readBest() : 0);
      this.best = Number.isFinite(value) && value > 0 ? Math.floor(value) : 0;
    } catch (_) { /* Storage may be unavailable on the host. */ }
    this.reset();
  }

  reset() {
    const x = Math.floor(this.cols / 2);
    const y = Math.floor(this.rows / 2);
    this.snake = [{ x, y }, { x: x - 1, y }, { x: x - 2, y }];
    this.direction = 'right';
    this.queuedDirection = null;
    this.score = 0;
    this.status = 'ready';
    this.bots = [];
    this.food = null;
    this.pendingGrowth = 0;
    for (let i = 0; i < this.botCount; i += 1) this.spawnBot();
    this.food = this.placeFood();
  }

  placeFood() {
    const occupied = new Set([...this.snake, ...this.bots.flatMap(bot => bot.snake)].map(part => `${part.x},${part.y}`));
    const empty = [];
    for (let y = 0; y < this.rows; y += 1) {
      for (let x = 0; x < this.cols; x += 1) {
        if (!occupied.has(`${x},${y}`)) empty.push({ x, y });
      }
    }
    if (!empty.length) return null;
    const sample = this.random();
    const index = Math.max(0, Math.min(empty.length - 1, Math.floor(sample * empty.length) || 0));
    return empty[index];
  }

  spawnBot() {
    const occupied = new Set([...this.snake, ...this.bots.flatMap(bot => bot.snake), ...(this.food ? [this.food] : [])].map(p => `${p.x},${p.y}`));
    const candidates = [];
    for (let y = 0; y < this.rows; y += 1) for (let x = 2; x < this.cols; x += 1) {
      const snake = [{ x, y }, { x: x - 1, y }, { x: x - 2, y }];
      if (snake.every(p => !occupied.has(`${p.x},${p.y}`)) && Math.abs(x - this.snake[0].x) + Math.abs(y - this.snake[0].y) >= 5) candidates.push(snake);
    }
    if (!candidates.length) return false;
    const index = Math.max(0, Math.min(candidates.length - 1, Math.floor(this.random() * candidates.length) || 0));
    this.bots.push({ snake: candidates[index], direction: 'right' });
    return true;
  }

  botDirection(bot) {
    const current = DIRECTIONS[bot.direction];
    const occupied = new Set([this.snake, ...this.bots.map(b => b.snake)].flatMap(snake => snake.slice(0, -1)).map(p => `${p.x},${p.y}`));
    const choices = Object.keys(DIRECTIONS).filter(name => {
      const d = DIRECTIONS[name];
      if (d.x + current.x === 0 && d.y + current.y === 0) return false;
      const x = bot.snake[0].x + d.x, y = bot.snake[0].y + d.y;
      return x >= 0 && x < this.cols && y >= 0 && y < this.rows && !occupied.has(`${x},${y}`);
    });
    choices.sort((a, b) => {
      const distance = name => this.food ? Math.abs(bot.snake[0].x + DIRECTIONS[name].x - this.food.x) + Math.abs(bot.snake[0].y + DIRECTIONS[name].y - this.food.y) : 0;
      return distance(a) - distance(b);
    });
    return choices[0] || bot.direction;
  }

  addScore(amount) {
    this.score += amount;
    if (this.score > this.best) {
      this.best = this.score;
      try { if (this.store.writeBest) this.store.writeBest(this.best); } catch (_) { /* Keep playing without storage. */ }
    }
  }

  start() {
    if (this.status === 'ready') this.status = 'running';
  }

  restart() {
    this.reset();
    this.start();
  }

  pause() {
    if (this.status === 'running') this.status = 'paused';
  }

  resume() {
    if (this.status === 'paused') this.status = 'running';
  }

  turn(direction) {
    const requested = DIRECTIONS[direction];
    const current = DIRECTIONS[this.direction];
    if (this.status !== 'running' || !requested || this.queuedDirection || direction === this.direction) return false;
    if (requested.x + current.x === 0 && requested.y + current.y === 0) return false;
    this.queuedDirection = direction;
    return true;
  }

  interval() {
    return Math.max(85, 180 - Math.floor(this.score / 5) * 8);
  }

  step() {
    if (this.status !== 'running') return;
    if (this.queuedDirection) this.direction = this.queuedDirection;
    this.queuedDirection = null;
    const actors = [{ snake: this.snake, direction: this.direction, player: true }, ...this.bots.map(bot => ({ ...bot, source: bot }))];
    for (const actor of actors) {
      if (!actor.player) actor.direction = this.botDirection(actor.source);
      const d = DIRECTIONS[actor.direction];
      actor.head = { x: actor.snake[0].x + d.x, y: actor.snake[0].y + d.y };
      actor.eating = !!this.food && actor.head.x === this.food.x && actor.head.y === this.food.y;
      actor.growing = actor.eating || (actor.player && this.pendingGrowth > 0);
      actor.body = actor.growing ? actor.snake : actor.snake.slice(0, -1);
    }
    const same = (a, b) => a.x === b.x && a.y === b.y;
    for (const actor of actors) {
      actor.dead = actor.head.x < 0 || actor.head.x >= this.cols || actor.head.y < 0 || actor.head.y >= this.rows;
      for (const other of actors) {
        if (other.body.some(part => same(actor.head, part))) {
          actor.dead = true;
          if (other.player && !actor.player) actor.killedByPlayer = true;
        }
        if (other !== actor && (same(actor.head, other.head) || (same(actor.head, other.snake[0]) && same(other.head, actor.snake[0])))) actor.dead = true;
      }
    }
    const player = actors[0];
    if (player.dead) { this.status = 'gameover'; return; }
    let foodEaten = false;
    for (const actor of actors) {
      if (actor.dead) continue;
      actor.snake.unshift(actor.head);
      if (!actor.growing) actor.snake.pop();
      if (actor.player && this.pendingGrowth > 0 && !actor.eating) this.pendingGrowth -= 1;
      if (actor.eating) { foodEaten = true; if (actor.player) this.addScore(1); }
      if (actor.source) actor.source.direction = actor.direction;
    }
    const defeated = actors.slice(1).filter(actor => actor.dead && actor.killedByPlayer).length;
    if (defeated) { this.addScore(defeated * 3); this.pendingGrowth += defeated * 3; }
    this.bots = actors.slice(1).filter(actor => !actor.dead).map(actor => actor.source);
    if (foodEaten) this.food = this.placeFood();
    if (!this.food) { this.status = 'won'; return; }
    while (this.bots.length < this.botCount && this.spawnBot()) { /* Fill available slots. */ }
  }
}

module.exports = { SnakeGame, DIRECTIONS };
