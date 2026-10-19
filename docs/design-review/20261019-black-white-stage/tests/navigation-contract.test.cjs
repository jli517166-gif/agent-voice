const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const crypto = require("node:crypto");
const html = fs.readFileSync(
  path.join(__dirname, "../base-template.html"),
  "utf8",
);
const show = html.slice(
  html.indexOf("function show("),
  html.indexOf("\nfunction onAction"),
);
function harness() {
  let maxScreens = 1,
    disposed = 0;
  const make = () => ({
    dataset: {},
    style: {},
    classList: { add() {}, remove() {} },
    setAttribute(k, v) {
      this[k] = v;
    },
    addEventListener() {},
    querySelectorAll() {
      return [];
    },
    animate() {
      return { cancel() {} };
    },
    remove() {
      root.children = root.children.filter((n) => n !== this);
    },
  });
  const root = {
    children: [make()],
    append(n) {
      this.children.push(n);
      maxScreens = Math.max(maxScreens, this.children.length);
    },
    replaceChildren(...nodes) {
      this.children = nodes;
      maxScreens = Math.max(maxScreens, nodes.length);
    },
  };
  const controls = new Map();
  const q = (s) =>
    s === "#screens"
      ? root
      : s === "#screens .screen"
        ? root.children[0]
        : controls.has(s)
          ? controls.get(s)
          : (controls.set(s, make()), controls.get(s));
  const context = {
    step: 0,
    mode: "setup",
    reduced: false,
    activeAnimations: [],
    steps: Array.from({ length: 10 }, (_, i) => ({ name: "step " + i })),
    document: { createElement: make },
    history: { replaceState() {} },
    $: q,
    content: () => "<h1>test</h1>",
    onAction() {},
    sound: false,
    tone() {},
    clearTransient() {
      disposed++;
      context.activeAnimations.forEach((a) => a.cancel());
      context.activeAnimations = [];
    },
  };
  vm.createContext(context);
  vm.runInContext(show, context);
  return {
    context,
    root,
    get maxScreens() {
      return maxScreens;
    },
    get disposed() {
      return disposed;
    },
  };
}
test("page switch never mounts old and new configuration screens together", () => {
  const h = harness();
  h.context.show(1);
  assert.equal(
    h.maxScreens,
    1,
    "old content may not be visible under a new scene",
  );
  assert.equal(h.root.children.length, 1);
});
test("rapid forward/back navigation preserves one screen and disposes each previous scene", () => {
  const h = harness();
  for (const n of [1, 2, 3, 2, 1, 8, 9, 0]) h.context.show(n);
  assert.equal(h.maxScreens, 1);
  assert.equal(h.root.children.length, 1);
  assert.equal(h.disposed, 8);
  assert.equal(h.context.step, 0);
});
test("built entry points and dependency imports resolve to one current resource version", () => {
  const root = path.join(__dirname, "..");
  const built = fs.readFileSync(path.join(root, "index.html"), "utf8");
  const { imports } = JSON.parse(
    built.match(/<script type="importmap">([\s\S]*?)<\/script>/)[1],
  );
  for (const [key, url] of Object.entries(imports)) {
    const file = key.slice(2),
      hash = crypto
        .createHash("sha256")
        .update(fs.readFileSync(path.join(root, file), "utf8").replace(/\r\n/g, "\n"))
        .digest("hex")
        .slice(0, 12);
    assert.equal(
      url,
      "./" + file + "?v=" + hash,
      `${file} must not reference a stale module`,
    );
  }
  const entries = [
    ...built.matchAll(/<script type="module" src="([^"]+)"/g),
  ].map((m) => m[1]);
  for (const url of entries) {
    const name = url.split("?")[0];
    assert.equal(
      "./" + url,
      imports["./" + name],
      `${name} must not execute as two module identities`,
    );
  }
  for (const match of built.matchAll(/href="([^"?]+\.css)\?v=([^"]+)"/g)) {
    const hash = crypto
      .createHash("sha256")
      .update(fs.readFileSync(path.join(root, match[1]), "utf8").replace(/\r\n/g, "\n"))
      .digest("hex")
      .slice(0, 12);
    assert.equal(
      match[2],
      hash,
      `${match[1]} must use its current content version`,
    );
  }
});
