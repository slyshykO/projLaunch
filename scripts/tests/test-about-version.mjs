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
const { init } = await import('../../src/bin/fable-js/State.js');
const { About } = await import('../../src/bin/fable-js/View.js');
const state = init()[0];
state.appVersion = 'stale-manifest-version';
const html = renderToStaticMarkup(React.createElement(About, state));
assert.match(html, />About</);
assert.ok(html.includes(`>${expected}</td>`), `About must display the generated Git version ${expected}`);
assert.ok(!html.includes('stale-manifest-version'), 'About must not substitute the committed manifest version');
console.log(`PASS: About displays generated Git version ${expected}.`);
