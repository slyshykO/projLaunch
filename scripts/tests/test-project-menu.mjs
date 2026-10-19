// Run after: dotnet fable src -o ./src/bin/fable-js -e .js --configuration Debug
// Exercise the compiled subscription with in-memory keyboard and Tauri events.
import assert from 'node:assert/strict';

const { onAddProject } = await import('../../src/bin/fable-js/ProjectMenu.js');
const { Msg, update } = await import('../../src/bin/fable-js/State.js');
const flush = () => new Promise(resolve => setImmediate(resolve));

// Node's EventTarget removal needs an options object for capture listeners.
// Normalize the browser's boolean overload before delegating to Node.
class BrowserEventTarget extends EventTarget {
    removeEventListener(type, callback, options) {
        super.removeEventListener(type, callback,
            typeof options === 'boolean' ? { capture: options } : options);
    }
}

for (const platform of ['Win32', 'Linux x86_64', 'MacIntel']) {
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { platform } });
    const callbacks = new Map();
    const requests = [];
    let finishListening;
    globalThis.window = Object.assign(new BrowserEventTarget(), {
        __TAURI_INTERNALS__: {
            metadata: { currentWindow: { label: 'main' } },
            transformCallback(callback) { callbacks.set(1, callback); return 1; },
            invoke(command, args) {
                requests.push({ command, args });
                return command === 'plugin:event|listen'
                    ? new Promise(resolve => { finishListening = () => resolve(7); })
                    : Promise.resolve();
            },
        },
        __TAURI_EVENT_PLUGIN_INTERNALS__: { unregisterListener() {} },
    });
    let count = 0;
    let state = { isProjectSaving: false, projectEditor: undefined };
    const subscribe = () => onAddProject()(message => {
        count++;
        [state] = update(message, state);
    });
    const press = (overrides = {}) => {
        const event = new Event('keydown', { cancelable: true });
        Object.assign(event, {
            code: 'KeyN', key: 'n', ctrlKey: platform !== 'MacIntel',
            metaKey: platform === 'MacIntel', altKey: false, shiftKey: false,
            repeat: false, isComposing: false, ...overrides,
        });
        window.dispatchEvent(event);
        return event;
    };
    const subscription = subscribe();
    await flush();
    finishListening();
    await flush();
    assert.equal(requests[0].args.event, 'menu-add-project');
    assert.deepEqual(requests[0].args.target, { kind: 'Window', label: 'main' });

    assert.ok(press().defaultPrevented);
    assert.equal(count, 1);
    assert.ok(state.projectEditor.id);
    const draft = state.projectEditor;
    draft.name = 'Unsaved draft';
    assert.ok(press({ key: 'т' }).defaultPrevented, 'Physical N must work in Cyrillic layouts');
    assert.equal(count, 2);
    assert.equal(state.projectEditor, draft);
    callbacks.get(1)({ event: 'menu-add-project', id: 7, payload: null });
    assert.equal(count, 3);
    assert.equal(state.projectEditor, draft, 'Native and DOM events must not reset an open editor');
    assert.equal(state.projectEditor.name, 'Unsaved draft');

    assert.ok(press({ repeat: true }).defaultPrevented);
    for (const event of [
        { ctrlKey: false, metaKey: false }, { ctrlKey: true, metaKey: true },
        { ctrlKey: platform === 'MacIntel', metaKey: platform !== 'MacIntel' },
        { altKey: true }, { shiftKey: true }, { isComposing: true }, { code: 'KeyM' },
    ]) assert.equal(press(event).defaultPrevented, false);
    assert.equal(count, 3, 'Other keys, modifiers, IME and key repeat must not open projects');

    [state] = update(Msg.CloseProjectEditor, state);
    press();
    assert.notEqual(state.projectEditor.id, draft.id);
    subscription.Dispose();
    const afterDispose = count;
    assert.equal(press().defaultPrevented, false);
    callbacks.get(1)({ payload: null });
    assert.equal(count, afterDispose);
    assert.equal(requests.at(-1).command, 'plugin:event|unlisten');

    // Disposal before async registration finishes must also release both listeners.
    const pending = subscribe();
    pending.Dispose();
    await flush();
    finishListening();
    await flush();
    assert.equal(requests.at(-1).command, 'plugin:event|unlisten');
    assert.equal(press().defaultPrevented, false);
    assert.equal(count, afterDispose);
}
console.log('PASS: Ctrl/Cmd+N, Cyrillic layout, menu events, draft preservation, repeat/modifier guards and listener cleanup.');
