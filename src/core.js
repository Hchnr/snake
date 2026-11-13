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
    this.food = this.placeFood();
  }

  placeFood() {
    const occupied = new Set(this.snake.map(part => `${part.x},${part.y}`));
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
    const delta = DIRECTIONS[this.direction];
    const head = { x: this.snake[0].x + delta.x, y: this.snake[0].y + delta.y };
    const eating = this.food && head.x === this.food.x && head.y === this.food.y;
    const collisionBody = eating ? this.snake : this.snake.slice(0, -1);
    const hitWall = head.x < 0 || head.x >= this.cols || head.y < 0 || head.y >= this.rows;
    if (hitWall || collisionBody.some(part => part.x === head.x && part.y === head.y)) {
      this.status = 'gameover';
      return;
    }
    this.snake.unshift(head);
    if (!eating) {
      this.snake.pop();
      return;
    }
    this.score += 1;
    if (this.score > this.best) {
      this.best = this.score;
      try { if (this.store.writeBest) this.store.writeBest(this.best); } catch (_) { /* Keep playing without storage. */ }
    }
    this.food = this.placeFood();
    if (!this.food) this.status = 'won';
  }
}

module.exports = { SnakeGame, DIRECTIONS };
