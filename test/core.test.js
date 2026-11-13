'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { SnakeGame } = require('../src/core');

function makeGame(options = {}) {
  const game = new SnakeGame({ random: () => 0, ...options });
  game.start();
  return game;
}

test('starts ready, moves forward, and prevents reversal or multiple turns per tick', () => {
  const game = new SnakeGame({ random: () => 0 });
  assert.equal(game.status, 'ready');
  game.start();
  const head = { ...game.snake[0] };
  assert.equal(game.turn('left'), false);
  assert.equal(game.turn('up'), true);
  assert.equal(game.turn('left'), false);
  game.step();
  assert.deepEqual(game.snake[0], { x: head.x, y: head.y - 1 });
  assert.equal(game.direction, 'up');
  assert.equal(game.turn('left'), true);
});

test('eating grows the snake, scores and saves a new best immediately', () => {
  const saved = [];
  const game = makeGame({ store: { readBest: () => 0, writeBest: value => saved.push(value) } });
  const length = game.snake.length;
  game.food = { x: game.snake[0].x + 1, y: game.snake[0].y };
  game.step();
  assert.equal(game.snake.length, length + 1);
  assert.equal(game.score, 1);
  assert.equal(game.best, 1);
  assert.deepEqual(saved, [1]);
  assert.ok(!game.snake.some(part => part.x === game.food.x && part.y === game.food.y));
});

test('the tail cell is safe when it moves away, but body collisions end the game', () => {
  const game = makeGame();
  game.snake = [{ x: 1, y: 1 }, { x: 1, y: 2 }, { x: 0, y: 2 }, { x: 0, y: 1 }];
  game.direction = 'left';
  game.food = { x: 4, y: 4 };
  game.step();
  assert.equal(game.status, 'running');
  assert.deepEqual(game.snake[0], { x: 0, y: 1 });
  game.snake = [{ x: 1, y: 1 }, { x: 1, y: 2 }, { x: 2, y: 2 }, { x: 2, y: 1 }, { x: 2, y: 0 }];
  game.direction = 'up';
  game.turn('right');
  game.step();
  assert.equal(game.status, 'gameover');
});

test('hitting the wall ends the game and no further step moves it', () => {
  const game = makeGame();
  game.snake[0] = { x: game.cols - 1, y: 5 };
  game.direction = 'right';
  game.step();
  assert.equal(game.status, 'gameover');
  const head = { ...game.snake[0] };
  game.step();
  assert.deepEqual(game.snake[0], head);
});

test('filling every cell wins instead of trying to place more food', () => {
  const game = makeGame({ cols: 4, rows: 2 });
  game.snake = [{ x: 2, y: 1 }, { x: 1, y: 1 }, { x: 0, y: 1 }, { x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }, { x: 3, y: 0 }];
  game.food = { x: 3, y: 1 };
  game.direction = 'right';
  game.step();
  assert.equal(game.status, 'won');
  assert.equal(game.food, null);
  assert.equal(game.snake.length, 8);
});

test('pause blocks movement; restart clears score but keeps best', () => {
  const game = makeGame({ store: { readBest: () => 7 } });
  const head = { ...game.snake[0] };
  game.pause();
  game.step();
  assert.deepEqual(game.snake[0], head);
  assert.equal(game.turn('up'), false);
  game.resume();
  game.step();
  assert.notDeepEqual(game.snake[0], head);
  game.score = 3;
  game.restart();
  assert.equal(game.status, 'running');
  assert.equal(game.score, 0);
  assert.equal(game.best, 7);
});

test('invalid or unavailable storage does not stop the game', () => {
  const game = makeGame({ store: { readBest() { throw Error('denied'); }, writeBest() { throw Error('full'); } } });
  game.food = { x: game.snake[0].x + 1, y: game.snake[0].y };
  assert.doesNotThrow(() => game.step());
  assert.equal(game.best, 1);
  assert.equal(new SnakeGame({ store: { readBest: () => 'not-a-number' } }).best, 0);
});
