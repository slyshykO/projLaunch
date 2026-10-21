// Compile Fable first. Exercise real state transitions and rendered components.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const requests = [];
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: {
    invoke(command, args) {
        if (command === "git_status") return Promise.resolve({ entries: [] });
        return new Promise((resolve, reject) => requests.push({ command, params: args.params, resolve, reject }));
    },
} };
const { init, update, Msg } = await import('../../src/bin/fable-js/src/State.js');
const { RepositoryPanel } = await import('../../src/bin/fable-js/src/ui/DesktopTabs.js');
const cases = new Msg(0, []).cases();
let state = init()[0];
const step = (name, ...fields) => {
    const result = update(new Msg(cases.indexOf(name), fields), state);
    state = result[0];
    return result[1];
};
const execute = commands => new Promise(resolve => {
    assert.ok(Array.from(commands).length);
    for (const run of commands) run(resolve);
});
const next = async command => {
    await new Promise(resolve => setImmediate(resolve));
    const request = requests.shift();
    assert.equal(request?.command, command);
    return request;
};
const apply = async promise => { state = update(await promise, state)[0]; };
const tab = id => Array.from(state.repositoryTabs).find(tab => tab.id === (id ?? state.activeRepositoryTab));
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch() {} }));
const commit = (letter, parents) => ({
    oid: letter.repeat(40), parents, subject: `Commit ${letter}`, refs: [],
    authorName: 'Author', authorEmail: 'author@example.test',
    commitDate: '2026-10-20T10:00:00Z', authorDate: '2026-10-20T10:00:00Z',
});
const root = commit('a', []);
const normal = commit('b', [root.oid]);
const merge = commit('c', [normal.oid, root.oid]);
const openTab = async path => {
    const refresh = await execute(step('OpenRepositoryTab', { root: path, name: path }));
    const result = update(refresh, state);
    state = result[0];
    const pending = execute(result[1]);
    (await next('git_open')).resolve({ repoId: path });
    (await next('git_log')).resolve({ commits: [normal, root, merge] });
    await apply(pending);
    return state.activeRepositoryTab;
};
const start = async commands => {
    const message = await execute(commands);
    const result = update(message, state);
    state = result[0];
    return { completion: execute(result[1]) };
};
const openedFiles = async (oid, parent) => {
    const opened = await next('git_open');
    opened.resolve({ repoId: 'fresh-session' });
    const request = await next('git_diff_files');
    assert.equal(request.params.repoId, 'fresh-session');
    assert.deepEqual(request.params.target, { kind: 'commit', oid, parent });
    return request;
};

const firstId = await openTab('C:/one');
assert.match(render(), /Select a commit/);
assert.equal(Array.from(step('SelectRepositoryCommit', firstId, 'unknown')).length, 0);
const first = await start(step('SelectRepositoryCommit', firstId, normal.oid));
assert.match(render(), /aria-pressed="true"/);
assert.match(render(), /Loading changed files/);
assert.equal(Array.from(step('RefreshCommitFiles', firstId)).length, 0);
const firstRequest = await openedFiles(normal.oid, 1);

// A later selection wins even when the earlier response arrives last.
const second = await start(step('SelectRepositoryCommit', firstId, root.oid));
const secondRequest = await openedFiles(root.oid, undefined);
secondRequest.resolve([
    { path: 'новое имя.txt', originalPath: 'old name.txt', status: 'R100' },
    { path: '<script>.txt', status: 'A' },
    { path: 'gone.txt', status: 'D' },
    { path: 'binary.png', status: 'M' },
]);
await apply(second.completion);
firstRequest.resolve([{ path: 'obsolete.txt', status: 'M' }]);
await apply(first.completion);
assert.equal(tab().selection.commit.oid, root.oid);
assert.equal(tab().selection.files.length, 4);
assert.match(render(), /old name.txt/);
assert.match(render(), /title="Renamed \(R100\)" aria-label="Renamed">R<\/span>/);
assert.match(render(), /aria-label="Added">\+<\/span>/);
assert.match(render(), /aria-label="Deleted">-<\/span>/);
assert.match(render(), /aria-label="Modified">M<\/span>/);
assert.match(render(), /&lt;script&gt;.txt/);
assert.doesNotMatch(render(), /obsolete.txt/);
assert.match(render(), /parent: —/);

const parentOne = await start(step('SelectRepositoryCommit', firstId, merge.oid));
assert.match(render(), /parent: bbbbbb,aaaaaa/);
assert.doesNotMatch(render(), /Choose a parent|Compared with/);
assert.match(render(), /aria-label="Diff parent"/);
assert.equal(tab().selection.parent, 1);
const parentOneRequest = await openedFiles(merge.oid, 1);
parentOneRequest.reject('Server session lost');
await apply(parentOne.completion);
assert.match(render(), /Could not load changed files/);
assert.match(render(), /Retry/);
assert.doesNotMatch(render(), /wrong-parent.txt|No changed files/);
const retry = execute(step('RefreshCommitFiles', firstId));
(await openedFiles(merge.oid, 1)).resolve([]);
await apply(retry);
assert.match(render(), /No changed files/);
const otherParent = await start(step('SelectCommitParent', firstId, 2));
(await openedFiles(merge.oid, 2)).resolve([]);
await apply(otherParent.completion);
assert.equal(tab().selection.parent, 2);

// Tab switch and history refresh preserve the selected immutable commit.
const secondId = await openTab('C:/two');
assert.equal(tab().selection, undefined);
step('SelectRepositoryTab', firstId);
assert.equal(tab().selection.parent, 2);
const refreshing = execute(step('RefreshRepositoryHistory', firstId));
(await next('git_open')).resolve({ repoId: 'new-history-session' });
(await next('git_log')).resolve({ commits: [normal] });
await apply(refreshing);
assert.equal(tab().selection.commit.oid, merge.oid, 'Keep selection even outside the refreshed first page');

const closing = await start(step('SelectRepositoryCommit', firstId, normal.oid));
const closingRequest = await openedFiles(normal.oid, 1);
step('CloseRepositoryTab', firstId);
step('SelectRepositoryTab', secondId);
closingRequest.resolve([{ path: 'closed-tab.txt', status: 'A' }]);
await apply(closing.completion);
assert.equal(tab().selection, undefined);
assert.equal(requests.length, 0);
console.log('PASS: selection, root/merge targets, file statuses, escaping, retry, refresh and stale replies.');
