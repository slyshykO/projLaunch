// No Tauri runtime, real console interception, database or personal files.
import assert from 'node:assert/strict';
import { createLogger } from '../../src/src/interop/_Logging.js';

function fixture(send, available = () => true) {
    const calls = [];
    const consoleTarget = Object.fromEntries(['log', 'info', 'warn', 'error', 'debug', 'trace']
        .map(method => [method, (...args) => calls.push([method, args])]));
    const eventTarget = new EventTarget();
    return { consoleTarget, eventTarget, calls, logger: createLogger({ send, available, consoleTarget, eventTarget }) };
}

// Ordinary logs must be sent automatically while the application is running.
// Neither explicit flush() nor disposal may be required to observe delivery.
let delivered;
const liveDelivery = new Promise(resolve => { delivered = resolve; });
const live = fixture(async entries => {
    if (entries.some(entry => entry.message === 'live message')) delivered();
});
const stopLive = live.logger.start();
live.consoleTarget.info('live message');
let liveTimeout;
try {
    await Promise.race([liveDelivery, new Promise((_, reject) => {
        liveTimeout = setTimeout(() => reject(new Error('Logs were not sent during normal runtime')), 1000);
    })]);
} finally {
    clearTimeout(liveTimeout);
}
stopLive();

for (const level of ['error', 'warn']) {
    const immediateBatches = [];
    const immediate = fixture(async entries => { immediateBatches.push(entries); });
    const stopImmediate = immediate.logger.start();
    immediate.consoleTarget[level]('urgent');
    assert.ok(immediateBatches.flat().some(entry => entry.message === 'urgent'),
        `${level} must start sending immediately, without a timer or shutdown`);
    stopImmediate();
    await immediate.logger.flush();
}

const batches = [];
const f = fixture(async entries => { batches.push(entries); });
const originalLog = f.consoleTarget.log;
const stop = f.logger.start();
f.logger.start(); // No duplicate listeners or console wrapping.
const circular = { text: 'Привет', big: 3n };
circular.self = circular;
f.consoleTarget.log('object', circular);
f.consoleTarget.error(new Error('console failure'));
f.consoleTarget.debug('debug detail');
const error = new Event('error');
Object.assign(error, { error: new Error('uncaught failure') });
f.eventTarget.dispatchEvent(error);
const rejection = new Event('unhandledrejection');
Object.assign(rejection, { reason: 'rejected operation' });
f.eventTarget.dispatchEvent(rejection);
f.consoleTarget.info('😀'.repeat(10000));
await f.logger.flush();
let entries = batches.flat();
assert.equal(entries.filter(entry => entry.message === 'Frontend ready').length, 1);
assert.ok(entries.some(entry => entry.message.includes('[Circular]') && entry.message.includes('Привет')));
assert.ok(entries.some(entry => entry.level === 'error' && entry.message.includes('console failure')));
assert.ok(entries.some(entry => entry.level === 'debug' && entry.message === 'debug detail'));
assert.ok(entries.some(entry => entry.message.includes('uncaught failure')));
assert.ok(entries.some(entry => entry.message.includes('rejected operation')));
assert.ok(entries.some(entry => entry.message.endsWith('[truncated]')));
assert.ok(entries.every(entry => Buffer.byteLength(entry.message) <= 16 * 1024));
assert.ok(f.calls.some(([method]) => method === 'log'), 'Original developer console still receives messages');
stop();
await f.logger.flush();
assert.equal(f.consoleTarget.log, originalLog);
const beforeStop = batches.flat().length;
f.eventTarget.dispatchEvent(rejection);
f.consoleTarget.log('after disposal');
await f.logger.flush();
assert.equal(batches.flat().length, beforeStop);

const overflowBatches = [];
const flood = fixture(async entries => { overflowBatches.push(entries); });
const stopFlood = flood.logger.start();
for (let i = 0; i < 1000; i++) flood.consoleTarget.info(`message ${i}`);
await flood.logger.flush();
assert.ok(overflowBatches.every(batch => batch.length <= 32));
assert.ok(overflowBatches.flat().length <= 201);
assert.ok(overflowBatches.flat().some(entry => entry.level === 'warn' && /messages dropped/.test(entry.message)));
stopFlood();

let fail = true;
let attempts = 0;
const recovered = [];
const broken = fixture(async entries => {
    attempts++;
    if (fail) throw new Error('IPC unavailable');
    recovered.push(...entries);
});
const stopBroken = broken.logger.start();
await broken.logger.flush();
broken.consoleTarget.error('another error');
await broken.logger.flush();
assert.equal(attempts, 2, 'Transport failures do not recursively log or retry');
assert.equal(broken.calls.filter(([method]) => method === 'warn').length, 1);
fail = false;
broken.consoleTarget.info('recovered');
await broken.logger.flush();
assert.ok(recovered.some(entry => entry.message === 'recovered'));
stopBroken();

const browser = fixture(() => assert.fail('Browser-only mode must not invoke IPC'), () => false);
const stopBrowser = browser.logger.start();
browser.consoleTarget.warn('browser only');
await browser.logger.flush();
assert.ok(browser.calls.some(([method]) => method === 'warn'));
stopBrowser();

let finishSend;
const hanging = fixture(() => new Promise(resolve => { finishSend = resolve; }));
const stopHanging = hanging.logger.start();
const startedAt = Date.now();
await hanging.logger.flush();
assert.ok(Date.now() - startedAt < 2500, 'Unavailable transport must not block closing indefinitely');
finishSend();
stopHanging();
await hanging.logger.flush();
console.log('PASS: frontend levels, errors, serialization, batching, overflow, recovery, disposal and bounded shutdown.');

// Verify the compiled F# boundary and its close flow, with an isolated IPC stub.
const { start: startFrontend } = await import('../../src/bin/fable-js/src/interop/FrontendLog.js');
const { Core_invoke } = await import('../../src/bin/fable-js/src/tauri/AppTauri.js');
const { init, update, Msg } = await import('../../src/bin/fable-js/src/State.js');
const ipc = [];
globalThis.isTauri = true;
globalThis.window = Object.assign(new EventTarget(), {
    __TAURI_INTERNALS__: {
        metadata: { currentWindow: { label: 'main' } },
        async invoke(command, args) {
            ipc.push({ command, args });
            if (command === 'failing_command') throw 'fixture failure';
        },
    },
});
const realConsole = globalThis.console;
globalThis.console = Object.fromEntries(['log', 'info', 'warn', 'error', 'debug', 'trace'].map(method => [method, () => {}]));
const subscription = startFrontend();
try {
    await assert.rejects(Core_invoke('failing_command', { token: 'do-not-log-this-token' }));
    console.info('last message before close');
    const state = init()[0];
    const cases = new Msg(0, []).cases();
    const [, commands] = update(new Msg(cases.indexOf('OnWindowSaveSuccess'), []), state);
    await new Promise(resolve => { for (const run of commands) run(resolve); });
    const closeIndex = ipc.findIndex(call => call.command === 'plugin:window|close');
    assert.ok(closeIndex >= 0, 'Compiled close flow must close the native window');
    const sent = ipc.slice(0, closeIndex).filter(call => call.command === 'frontend_log').flatMap(call => call.args.entries);
    assert.ok(sent.some(entry => entry.message === 'last message before close'));
    assert.ok(sent.some(entry => entry.level === 'error' && entry.message.includes('failing_command')));
    assert.ok(!JSON.stringify(sent).includes('do-not-log-this-token'), 'IPC parameters must not be copied into logs');
} finally {
    subscription.Dispose();
    globalThis.console = realConsole;
}
console.log('PASS: compiled F# logging IPC, error reporting without parameters and log drain before native close.');
