// Run after generating src/bin/version.fs and compiling src with Fable.
// Render the actual About component; do not read the user's app data or call Tauri.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

globalThis.isTauri = true;
globalThis.window = {
    location: { hash: '#/about', href: 'http://localhost/#/about' },
    __TAURI_INTERNALS__: {
        invoke: () => { throw new Error('About rendering must not invoke Tauri'); },
    },
};

const source = readFileSync(new URL('../../src/bin/version.fs', import.meta.url), 'utf8');
const expected = source.match(/let version\s*\(\)\s*=\s*"([^"]+)"/)?.[1];
assert.ok(expected, 'Generated version must be a runtime function, not an inlined Literal');
const { init } = await import('../../src/bin/fable-js/src/State.js');
const { View } = await import('../../src/bin/fable-js/src/ui/View.js');
const state = init()[0];
state.appVersion = 'stale-manifest-version';
state.isAboutOpen = true;
const html = renderToStaticMarkup(React.createElement(View, { state, dispatch() {} }));
assert.match(html, />About</);
assert.ok(html.includes(`>${expected}</td>`), `About must display the generated Git version ${expected}`);
assert.ok(!html.includes('stale-manifest-version'), 'About must not substitute the committed manifest version');
console.log(`PASS: About displays generated Git version ${expected}.`);

assert.match(html, /href="https:\/\/github.com\/slyshykO\/projLaunch"/);
assert.match(html, /target="_blank"/);
state.appDataDir = '/example/data';
state.appConfigDir = '/example/config';
state.isAboutOpen = false;
state.isDebugAboutOpen = true;
const debugHtml = renderToStaticMarkup(React.createElement(View, { state, dispatch() {} }));
assert.match(debugHtml, />DebugAbout</);
for (const label of ['Data dir', 'Config dir', 'Window size', 'Window position', 'Current time']) {
    assert.ok(!html.includes(label), `${label} must not appear in About`);
    assert.ok(debugHtml.includes(label), `${label} must appear in DebugAbout`);
}
assert.ok(debugHtml.includes('/example/data'));
assert.ok(debugHtml.includes('/example/config'));
assert.ok(!debugHtml.includes('Git repository'));
console.log('PASS: About repository link and separate DebugAbout diagnostics.');
