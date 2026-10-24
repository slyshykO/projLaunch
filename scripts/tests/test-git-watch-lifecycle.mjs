import assert from 'node:assert/strict';
import { watchRepository } from '../../src/src/interop/_GitWatch.js';

const tick = () => new Promise(resolve => setImmediate(resolve));
const originalTimeout = globalThis.setTimeout;
const originalClear = globalThis.clearTimeout;
const timers = new Map();
globalThis.setTimeout = (callback, delay) => { const token = {}; timers.set(token, { callback, delay }); return token; };
globalThis.clearTimeout = token => timers.delete(token);
try {
    let handler;
    let unlistened = 0;
    let id = 0;
    const starts = [], stops = [], changes = [], statuses = [];
    const observer = watchRepository(
        async callback => { handler = callback; return () => { unlistened++; }; },
        token => new Promise((resolve, reject) => starts.push({ token, resolve, reject })),
        async token => { stops.push(token); },
        event => event.endpoint === 'local',
        (...change) => changes.push(change),
        status => statuses.push(status),
        () => `watch-${++id}`,
    );
    await tick();
    handler({ endpoint: 'ssh', subscriptionId: starts[0].token, history: true });
    handler({ endpoint: 'local', subscriptionId: 'stale', history: true });
    assert.equal(changes.length, 0);
    handler({ endpoint: 'local', subscriptionId: starts[0].token, history: false, workingTree: true });
    assert.deepEqual(changes, [[false, true]], 'Notifications arriving before subscribe response are retained');
    starts[0].resolve(true);
    await tick();
    assert.deepEqual(changes.at(-1), [true, true], 'Registration closes the initial read gap');
    handler({ endpoint: 'local', error: 'Disconnected' });
    await tick();
    assert.ok(statuses.at(-1).includes('Disconnected'));
    assert.deepEqual(stops, ['watch-1']);
    assert.equal(unlistened, 1);
    const [token, timer] = [...timers][0];
    assert.equal(timer.delay, 1000);
    timers.delete(token); timer.callback();
    await tick();
    assert.equal(starts.length, 2);
    // Disposal while registration is in flight must clean up its late response.
    observer.dispose();
    starts[1].resolve(true);
    await observer.done;
    assert.deepEqual(stops, ['watch-1', 'watch-2']);
    assert.equal(unlistened, 2);
    assert.equal(timers.size, 0);
    const before = changes.length;
    handler({ endpoint: 'local', subscriptionId: 'watch-2', history: true });
    assert.equal(changes.length, before);
} finally {
    globalThis.setTimeout = originalTimeout;
    globalThis.clearTimeout = originalClear;
}
console.log('PASS: Git subscriptions isolate endpoints/tokens, recover, and dispose pending registrations.');
