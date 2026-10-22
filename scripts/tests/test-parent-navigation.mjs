// Compile Fable first. Real Elmish transitions with isolated Git IPC.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg, workspaceSnapshot } from '../../src/bin/fable-js/src/State.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';
import { ParentLinks } from '../../src/bin/fable-js/src/ui/CommitFiles.js';

const logs = [], targets = [];
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: {
    invoke(command, args) {
        if (command === 'git_branches') return Promise.resolve({ branches: [], head: null, currentBranch: null });
        const params = args?.params;
        if (command === 'git_open') return Promise.resolve({ repoId: params.path });
        if (command === 'git_status') return Promise.resolve({ entries: [] });
        if (command === 'git_diff_files') {
            targets.push(params.target);
            return Promise.resolve([{ path: 'file.txt', status: 'M' }]);
        }
        if (command === 'git_diff') return Promise.resolve({ text: '+new', binary: false, truncated: false });
        assert.equal(command, 'git_log');
        return new Promise((resolve, reject) => logs.push({ params, resolve, reject }));
    },
} };
const cases = new Msg(0, []).cases();
let state = init()[0];
const dispatch = message => {
    const [next, commands] = update(message, state);
    state = next;
    for (const run of commands) run(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const tick = () => new Promise(resolve => setImmediate(resolve));
const tab = id => Array.from(state.repositoryTabs).find(tab => tab.id === (id ?? state.activeRepositoryTab));
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch }));
const commit = (letter, parents = []) => ({
    oid: letter.repeat(40), parents, subject: `Commit ${letter}`, refs: [],
    authorName: 'Author', authorEmail: 'test@example.test',
    authorDate: '2026-10-21T10:00:00Z', commitDate: '2026-10-21T10:00:00Z',
});
const root = commit('a');
const parent = commit('b', [root.oid]);
const merge = commit('c', [parent.oid, root.oid]);
const unrelated = commit('d');
const nextLog = async () => {
    await tick();
    assert.equal(logs.length, 1, 'Only one history read per step');
    return logs.shift();
};
const page = async (commits, nextCursor) => {
    (await nextLog()).resolve({ commits, nextCursor });
    await tick();
};
const open = async (commits = [merge], cursor = 'page-2') => {
    send('OpenRepositoryTab', { root: `C:/repo-${Array.from(state.repositoryTabs).length}`, name: 'repo' });
    await page(commits, cursor);
    send('SelectRepositoryCommit', tab().id, merge.oid);
    await tick();
    return tab().id;
};

// Both merge parent hashes are real buttons and retain the full OID in dispatch.
const first = await open([merge, parent, root]);
let clicked;
const links = ParentLinks(first, merge, message => { clicked = message; });
const buttons = React.Children.toArray(links.props.children).filter(child => child.type === 'button');
assert.equal(buttons.length, 2);
buttons[1].props.onClick();
assert.equal(cases[clicked.tag], 'NavigateToParent');
assert.deepEqual(clicked.fields, [first, root.oid]);
assert.match(render(), /aria-label="Open parent b{40}"/);
assert.match(render(), /aria-label="Diff parent"/);

// A loaded parent opens immediately, closes diff and resets its own diff parent.
send('SelectCommitFile', first, 'file.txt');
await tick();
assert.ok(tab().selection.diff);
send('NavigateToParent', first, parent.oid);
await tick();
assert.equal(tab().selection.commit.oid, parent.oid);
assert.equal(tab().selection.parent, 1);
assert.equal(tab().selection.diff, undefined);
assert.equal(tab().commitNavigation, undefined);
assert.equal(logs.length, 0);
assert.equal(workspaceSnapshot(state).tabs[0].selection.commit.oid, parent.oid);
send('NavigateToParent', first, root.oid);
await tick();
assert.equal(tab().selection.parent, undefined);
assert.equal(targets.at(-1).oid, root.oid);
assert.match(render(), /parent: —/);

// Auto-pagination preserves topology and selects only after finding the parent.
const second = await open();
send('NavigateToParent', second, parent.oid);
assert.equal(tab().selection.commit.oid, merge.oid);
assert.match(render(), /Finding parent/);
const request = await nextLog();
assert.equal(request.params.cursor, 'page-2');
request.resolve({ commits: [unrelated], nextCursor: 'page-3' });
await tick();
await page([parent, root]);
assert.equal(tab().selection.commit.oid, parent.oid);
assert.deepEqual(tab().history.commits.map(c => c.oid), [merge, unrelated, parent, root].map(c => c.oid));
assert.equal(tab().commitNavigation, undefined);
assert.equal(logs.length, 0);

// A new parent click while loading wins, including a second merge parent.
const third = await open();
send('NavigateToParent', third, parent.oid);
const pending = await nextLog();
send('NavigateToParent', third, root.oid);
pending.resolve({ commits: [parent], nextCursor: 'page-3' });
await tick();
assert.equal(tab().selection.commit.oid, merge.oid);
await page([root]);
assert.equal(tab().selection.commit.oid, root.oid);

// Cancel and explicit row selection prevent a late page from changing selection.
for (const cancel of ['CancelCommitNavigation', 'SelectRepositoryCommit']) {
    const id = await open();
    send('NavigateToParent', id, parent.oid);
    const pending = await nextLog();
    send(cancel, id, merge.oid);
    pending.resolve({ commits: [parent], nextCursor: 'page-3' });
    await tick();
    assert.equal(tab().selection.commit.oid, merge.oid);
    assert.equal(tab().commitNavigation, undefined);
    assert.equal(logs.length, 0);
}

// Missing parent leaves the current commit intact; hash retry starts a fresh snapshot.
const missing = await open();
send('NavigateToParent', missing, parent.oid);
await page([unrelated]);
assert.match(tab().commitNavigationError, /not available/);
assert.equal(tab().selection.commit.oid, merge.oid);
send('NavigateToParent', missing, parent.oid);
const retry = await nextLog();
assert.equal(retry.params.cursor, undefined);
retry.resolve({ commits: [merge, parent] });
await tick();
assert.equal(tab().selection.commit.oid, parent.oid);
assert.equal(tab().commitNavigationError, undefined);

// Expired cursors stop the search, preserve selection and allow retry.
const failed = await open();
send('NavigateToParent', failed, parent.oid);
(await nextLog()).reject('Cursor expired');
await tick();
assert.match(tab().commitNavigationError, /Cursor expired/);
assert.equal(tab().selection.commit.oid, merge.oid);
assert.equal(logs.length, 0);
send('NavigateToParent', failed, parent.oid);
await page([merge, parent]);
assert.equal(tab().selection.commit.oid, parent.oid);

// Closed tabs and background results cannot change another tab's selection.
const closing = await open();
send('NavigateToParent', closing, parent.oid);
const closed = await nextLog();
send('CloseRepositoryTab', closing);
const activeBefore = state.activeRepositoryTab;
const selectedBefore = tab().selection.commit.oid;
closed.resolve({ commits: [parent], nextCursor: 'ignored' });
await tick();
assert.equal(state.activeRepositoryTab, activeBefore);
assert.equal(tab().selection.commit.oid, selectedBefore);
assert.equal(logs.length, 0);
send('NavigateToParent', activeBefore, unrelated.oid);
assert.equal(logs.length, 0, 'Only a parent of the current selection can be followed');
console.log('PASS: parent links, loaded and paginated navigation, merge/root, diff, persistence, cancellation, late responses, missing parents and retry.');
