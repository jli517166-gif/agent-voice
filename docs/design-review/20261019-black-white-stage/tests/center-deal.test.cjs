const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = fs
  .readFileSync(path.join(__dirname, "../center-deal.js"), "utf8")
  .replaceAll("export function ", "function ");
const context = vm.createContext({});
vm.runInContext(source, context);
test("unequal destination boxes share one deal origin, including offset stages", () => {
  const origin = { x: 760, y: 420 };
  for (const box of [
    { left: 180, top: 110, width: 450, height: 70 },
    { left: 1010, top: 300, width: 230, height: 290 },
    { left: 500, top: 550, width: 600, height: 80 },
  ]) {
    const offset = context.dealOffset(origin, box);
    assert.equal(box.left + box.width / 2 + offset.x, origin.x);
    assert.equal(box.top + box.height / 2 + offset.y, origin.y);
  }
});
test("dealing batches all geometry reads before animation writes and reduced motion stays still", () => {
  const calls = [];
  const stage = {
    getBoundingClientRect() {
      calls.push("stage");
      return { left: 100, top: 20, width: 800, height: 600 };
    },
  };
  const entry = (x) => ({
    node: {
      dataset: {},
      getBoundingClientRect() {
        calls.push("read" + x);
        return { left: x, top: 100, width: 200, height: 100 };
      },
      animate(frames, options) {
        calls.push("animate" + x);
        assert.match(frames[0].transform, /translate3d/);
        assert.equal(options.fill, "both");
        return { frames, options };
      },
    },
    delay: x,
  });
  const entries = [entry(10), entry(30)];
  const result = context.dealFromCenter(stage, entries);
  assert.deepEqual(calls, [
    "stage",
    "read10",
    "read30",
    "animate10",
    "animate30",
  ]);
  assert.equal(result.length, 2);
  assert.equal(result[0].options.delay, 10);
  calls.length = 0;
  assert.equal(context.dealFromCenter(stage, entries, true).length, 0);
  assert.deepEqual(calls, []);
});
