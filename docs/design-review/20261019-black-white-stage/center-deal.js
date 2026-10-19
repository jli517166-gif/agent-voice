// One shared origin, fixed destination boxes. No layout properties are animated.
export function dealOffset(origin, box) {
  return {
    x: origin.x - box.left - box.width / 2,
    y: origin.y - box.top - box.height / 2,
  };
}
export function dealFromCenter(stage, entries, reduced = false) {
  if (reduced) return [];
  const bounds = stage.getBoundingClientRect();
  const origin = {
    x: bounds.left + bounds.width / 2,
    y: bounds.top + bounds.height / 2,
  };
  // Read every destination before starting any animations.
  const seen = new Set();
  const items = entries
    .filter((e) => e.node && !seen.has(e.node) && seen.add(e.node))
    .map((e) => ({ ...e, box: e.node.getBoundingClientRect() }))
    .filter((e) => e.box.width && e.box.height);
  return items.map((entry, i) => {
    const { x, y } = dealOffset(origin, entry.box),
      angle = entry.angle ?? (i % 2 ? 5 : -5);
    entry.node.dataset.dealRole = entry.role || "content";
    const animation = entry.node.animate(
      [
        {
          opacity: 0,
          transformOrigin: "50% 50%",
          transform: `translate3d(${x}px,${y}px,0) rotate(${angle}deg) scale(.62)`,
        },
        { opacity: 1, offset: 0.2 },
        {
          opacity: 1,
          transformOrigin: "50% 50%",
          transform: "translate3d(0,0,0) rotate(0deg) scale(1)",
        },
      ],
      {
        duration: entry.duration || 660,
        delay: entry.delay || 0,
        easing: "cubic-bezier(.22,.8,.25,1)",
        fill: "both",
      },
    );
    return animation;
  });
}
export function pageEntries(screen, art, room, floor, ribbons) {
  const entries = [...screen.querySelectorAll(".title-piece")].map(
    (node, i) => ({ node, role: "title", delay: i * 110, duration: 690 }),
  );
  entries.push({
    node: screen.querySelector(".lead"),
    role: "description",
    delay: 310,
    duration: 600,
  });
  const cards = [
    ...screen.querySelectorAll(
      ".option,.summary-line,.simple-action,.voice-name,.small-result,.after-options,.receipt-heading,.switch-line,.intro-foot,.sound-bars,.learn",
    ),
  ];
  if (!cards.length) {
    const copy = screen.querySelector(".step-grid > div:not(.art-host)");
    if (copy)
      cards.push(
        ...[...copy.children].filter(
          (n) => !n.matches("h1,.lead,.eyebrow") && !n.querySelector("h1"),
        ),
      );
  }
  cards
    .filter(
      (node) => !cards.some((other) => other !== node && other.contains(node)),
    )
    .forEach((node, i) =>
      entries.push({
        node,
        role: "control",
        delay: 530 + Math.min(i, 5) * 75,
        duration: 610,
      }),
    );
  for (const node of [room, art])
    entries.push({
      node,
      role: "illustration",
      delay: 850,
      duration: 650,
      angle: -4,
    });
  for (const node of [floor, ribbons])
    entries.push({ node, role: "light", delay: 910, duration: 550, angle: 0 });
  return entries;
}
export function settle(animations, cancel = false) {
  for (const animation of animations) {
    try {
      animation.finish();
      if (cancel) animation.cancel();
    } catch {}
  }
}
