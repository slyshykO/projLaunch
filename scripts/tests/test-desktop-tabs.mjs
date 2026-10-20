// Run after compiling src with Fable. No database or native windows are used.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

globalThis.isTauri = true;
globalThis.window = {
    __TAURI_INTERNALS__: {
        invoke: () => { throw new Error('Tab navigation must not invoke native commands'); },
    },
};
const { init, update, Msg } = await import('../../src/bin/fable-js/src/State.js');
const { View } = await import('../../src/bin/fable-js/src/ui/View.js');
const { TabBar } = await import('../../src/bin/fable-js/src/ui/DesktopTabs.js');
const cases = new Msg(0, []).cases();
const step = (state, name, ...fields) => {
    const tag = cases.indexOf(name);
    assert.notEqual(tag, -1);
    return update(new Msg(tag, fields), state);
};
const repo = (name, root) => ({ name, root, dirty: false, branch: 'main', error: undefined });
let state = init()[0];
assert.equal(state.activeRepositoryTab, undefined);
assert.equal(Array.from(state.repositoryTabs).length, 0);

state = step(state, 'OpenRepositoryTab', repo('shared', 'C:/one'))[0];
const first = state.activeRepositoryTab;
state = step(state, 'OpenRepositoryTab', repo('shared', 'C:/two'))[0];
const second = state.activeRepositoryTab;
assert.notEqual(first, second, 'Identical names must not merge different worktrees');
state = step(state, 'OpenRepositoryTab', repo('renamed', 'C:/one'))[0];
assert.equal(state.activeRepositoryTab, first);
assert.equal(Array.from(state.repositoryTabs).length, 2, 'Shared repository opens once across projects');
state = step(state, 'SelectRepositoryTab', 'missing')[0];
assert.equal(state.activeRepositoryTab, first);

state = step(state, 'ShowProjects')[0];
const tabsBeforeAbout = state.repositoryTabs;
state = step(state, 'OpenAbout')[0];
assert.ok(state.isAboutOpen);
assert.equal(state.repositoryTabs, tabsBeforeAbout);
assert.equal(step(state, 'OpenAddProjectModal')[0], state, 'Do not stack native dialogs');
const aboutHtml = renderToStaticMarkup(React.createElement(View, { state, dispatch() {} }));
assert.match(aboutHtml, /<dialog[^>]+id="about-dialog"/);
assert.match(aboutHtml, /aria-labelledby="about-title"/);
state = step(state, 'CloseAbout')[0];
assert.equal(state.isAboutOpen, false);
state = step(state, 'OpenAddProjectModal')[0];
assert.equal(step(state, 'OpenAbout')[0], state, 'About must not cover an unsaved project editor');
state = step(state, 'CloseProjectEditor')[0];

const html = renderToStaticMarkup(React.createElement(View, { state, dispatch() {} }));
assert.match(html, /role="tablist"/);
assert.equal((html.match(/role="tabpanel"/g) ?? []).length, 3);
assert.match(html, /id="panel-projects"[^>]+role="tabpanel"/);
assert.match(html, /id="panel-[^"]+"[^>]+hidden=""/);
assert.doesNotMatch(html, /href="#about"|href="#projects"|class="navbar/);
assert.doesNotMatch(html, /Close repository Projects/);
const bar = renderToStaticMarkup(React.createElement(TabBar, {
    tabs: state.repositoryTabs, active: undefined, dispatch() {},
}));
assert.equal((bar.match(/role="tab"/g) ?? []).length, 3);
assert.equal((bar.match(/aria-selected="true"/g) ?? []).length, 1);

state = step(state, 'SelectRepositoryTab', second)[0];
state = step(state, 'CloseRepositoryTab', first)[0];
assert.equal(state.activeRepositoryTab, second, 'Closing a background tab preserves selection');
const [, closeCommand] = step(state, 'CloseActiveTab');
for (const run of closeCommand) run(message => { state = update(message, state)[0]; });
assert.equal(state.activeRepositoryTab, undefined);
assert.equal(Array.from(state.repositoryTabs).length, 0);
assert.equal(Array.from(step(state, 'CloseActiveTab')[1]).length, 0, 'Projects cannot be closed');

state = step(state, 'OpenRepositoryTab', repo('one', 'C:/one'))[0];
const left = state.activeRepositoryTab;
state = step(state, 'OpenRepositoryTab', repo('two', 'C:/two'))[0];
state = step(state, 'CloseRepositoryTab', state.activeRepositoryTab)[0];
assert.equal(state.activeRepositoryTab, left, 'Closing selected tab activates its left neighbour');
console.log('PASS: repository tab identity, selection/closing, persistent project panel and About dialog isolation.');
