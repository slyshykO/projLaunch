import assert from 'node:assert/strict';
import { attachHistoryScroll } from '../../src/src/interop/_HistoryScroll.js';

let resize;
let disconnected = false;
globalThis.ResizeObserver = class {
    constructor(callback) { resize = callback; }
    observe() {}
    disconnect() { disconnected = true; }
};
class HistoryElement extends EventTarget {
    scrollTop = 0;
    scrollLeft = 0;
    clientHeight = 100;
    clientWidth = 200;
    scrollWidth = 500;
    hidden = false;
    names = Array.from({ length: 10 }, (_, index) => String(index + 1).padStart(40, '0'));
    get scrollHeight() { return 20 + this.names.length * 30; }
    closest() { return this.hidden ? {} : null; }
    getBoundingClientRect() { return { top: 100 }; }
    querySelector() { return { getBoundingClientRect: () => ({ height: 20 }) }; }
    querySelectorAll() {
        return this.names.map((name, index) => ({
            dataset: { historyOid: name },
            getBoundingClientRect: () => ({ top: 120 + index * 30 - this.scrollTop, bottom: 150 + index * 30 - this.scrollTop }),
        }));
    }
}
const element = new HistoryElement();
const changes = [];
const restored = [];
let cancelled = 0;
const controller = attachHistoryScroll(element, value => changes.push(value), (token, value) => restored.push({ token, value }), () => cancelled++);
const saved = { top: 65, left: 42, anchor: element.names[2], offset: 5 };
element.hidden = true;
controller.update(saved, 'restart-one', false);
resize();
element.dispatchEvent(new Event('scroll'));
assert.equal(changes.length, 0, 'A hidden tab must not overwrite its saved position with zero');
assert.equal(restored.length, 0);
controller.update(saved, 'restart-one', true);
assert.equal(restored.length, 0, 'Loaded history behind a diff waits until visible');

// New commits above the saved anchor shift its pixel position after restart.
element.names.unshift('f'.repeat(40));
element.hidden = false;
resize();
assert.equal(element.scrollTop, 95);
assert.equal(element.scrollLeft, 42);
assert.deepEqual(restored[0], { token: 'restart-one', value: { ...saved, top: 95 } });
element.dispatchEvent(new Event('scroll'));
resize();
assert.equal(restored.length, 1);
assert.equal(changes.length, 0, 'Programmatic restoration is acknowledged exactly once');

controller.update(restored[0].value, undefined, true);
element.scrollTop = 130;
element.dispatchEvent(new Event('scroll'));
assert.equal(changes[0].anchor, element.names[4]);
assert.equal(changes[0].offset, 10);
assert.equal(changes[0].left, 42);

// A missing anchor uses the saved offset and clamps to the shorter list.
controller.update({ top: 5000, left: 5000, anchor: 'e'.repeat(40), offset: 10 }, 'short-history', true);
assert.equal(element.scrollTop, 250);
assert.equal(element.scrollLeft, 300);
assert.equal(restored.at(-1).value.top, 250);

// User interaction while loading takes priority over any later restore.
controller.update(saved, 'interrupted', false);
element.dispatchEvent(new Event('wheel'));
assert.equal(cancelled, 1);
controller.update(saved, undefined, true);
resize();
assert.equal(restored.length, 2);
element.scrollTop = 20;
element.dispatchEvent(new Event('scroll'));
assert.equal(changes.at(-1).top, 20);

controller.dispose();
assert.equal(disconnected, true);
element.scrollTop = 30;
element.dispatchEvent(new Event('scroll'));
assert.equal(changes.at(-1).top, 20, 'Unmount removes scroll listeners');
console.log('PASS: scroll anchors, shifted rows, hidden/diff panels, horizontal offsets, missing-anchor clamp, user interruption and disposal.');
