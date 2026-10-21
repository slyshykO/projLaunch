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
const dispatch = message => {
    const [nextState, commands] = update(message, state);
    state = nextState;
    for (const run of commands) run(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const tick = () => new Promise(resolve => setImmediate(resolve));
const next = async command => {
    await tick();
    const request = requests.shift();
    assert.equal(request?.command, command);
    return request;
};
const tab = () => Array.from(state.repositoryTabs).find(tab => tab.id === state.activeRepositoryTab);
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch() {} }));
const commit = (oid, parents) => ({
    oid, parents, subject: oid, refs: [], authorName: 'Author', authorEmail: 'test@example.test',
    commitDate: '2026-10-20T10:00:00Z', authorDate: '2026-10-20T10:00:00Z',
});
const root = commit('a'.repeat(40), []);
const merge = commit('b'.repeat(40), [root.oid, 'c'.repeat(40)]);
const files = [
    { path: 'новое имя.txt', originalPath: 'old name.txt', status: 'R100' },
    { path: 'deleted.txt', status: 'D' },
    { path: 'binary.png', status: 'M' },
];
const patch = (path, text, extra = {}) => ({ path, text, binary: false, truncated: false, untracked: false, ...extra });
send('OpenRepositoryTab', { root: 'C:/repo', name: 'repo' });
(await next('git_open')).resolve({ repoId: 'history' });
(await next('git_log')).resolve({ commits: [root, merge] });
await tick();
const id = tab().id;
const loadFiles = async () => {
    (await next('git_open')).resolve({ repoId: 'files' });
    (await next('git_diff_files')).resolve(files);
    await tick();
};
const openDiff = async (path, oid = root.oid, parent = undefined) => {
    const opened = await next('git_open');
    opened.resolve({ repoId: 'new-diff-session' });
    const diff = await next('git_diff');
    assert.deepEqual(diff.params.target, { kind: 'commit', oid, parent });
    assert.equal(diff.params.path, path, 'Send the destination path unchanged, including for renames');
    assert.equal(diff.params.repoId, 'new-diff-session');
    assert.notEqual(diff.params.operationId, opened.params.operationId);
    return diff;
};
send('SelectCommitFile', id, files[0].path);
assert.equal(requests.length, 0, 'Cannot request a file before a commit/file list is loaded');
send('SelectRepositoryCommit', id, root.oid);
await loadFiles();
send('SelectCommitFile', id, 'unknown.txt');
assert.equal(requests.length, 0);
send('SelectCommitFile', id, files[0].path);
assert.match(render(), /Loading diff/);
assert.match(render(), /hidden=""/, 'Keep history mounted and hidden while displaying diff');
assert.match(render(), /title="Close diff">✕<\/button>/);
send('SelectCommitFile', id, files[0].path);
send('RetryCommitDiff', id);
const first = await openDiff(files[0].path);
assert.equal(requests.length, 0, 'No duplicate requests for a pending file');
send('SelectCommitFile', id, files[1].path);
const second = await openDiff(files[1].path);
second.resolve(patch(files[1].path, '--- a/deleted.txt\n+++ /dev/null\n@@ -1 +0,0 @@\n-<script>alert(1)</script>\n'));
await tick();
first.resolve(patch(files[0].path, 'obsolete'));
await tick();
assert.equal(tab().selection.diff.file.path, files[1].path);
assert.match(render(), /&lt;script&gt;/);
assert.doesNotMatch(render(), /<script>|obsolete/);
assert.match(render(), /bg-error\/15/);
send('CloseCommitDiff', id);
assert.equal(tab().selection.diff, undefined);
assert.doesNotMatch(render(), /hidden=""|Close diff/);
assert.equal(tab().selection.commit.oid, root.oid);

// Late completion after close must never reopen the diff, including after reopening the same path.
send('SelectCommitFile', id, files[0].path);
const closed = await openDiff(files[0].path);
send('CloseCommitDiff', id);
send('SelectCommitFile', id, files[0].path);
const reopened = await openDiff(files[0].path);
reopened.reject('Session lost');
await tick();
closed.resolve(patch(files[0].path, 'stale same path'));
await tick();
assert.match(render(), /Could not load diff/);
assert.doesNotMatch(render(), /stale same path/);
send('RetryCommitDiff', id);
(await openDiff(files[0].path)).resolve(patch(files[0].path, '', { truncated: true }));
await tick();
assert.match(render(), /No textual changes/);
assert.match(render(), /truncated by the server/);
assert.match(render(), /old name.txt/);
send('SelectCommitFile', id, files[2].path);
(await openDiff(files[2].path)).resolve(patch(files[2].path, undefined, { binary: true }));
await tick();
assert.match(render(), /Binary file/);

// History refresh leaves immutable patch and selection intact.
send('RefreshRepositoryHistory', id);
(await next('git_open')).resolve({ repoId: 'history2' });
(await next('git_log')).resolve({ commits: [root, merge] });
await tick();
assert.equal(tab().selection.diff.patch.binary, true);
send('SelectRepositoryCommit', id, merge.oid);
assert.equal(tab().selection.diff, undefined);
await loadFiles();
send('SelectCommitFile', id, files[0].path);
const oldCommit = await openDiff(files[0].path, merge.oid, 1);
send('SelectRepositoryCommit', id, root.oid);
assert.equal(tab().selection.diff, undefined);
await loadFiles();
oldCommit.resolve(patch(files[0].path, 'wrong commit'));
await tick();
assert.equal(tab().selection.diff, undefined);
send('SelectRepositoryCommit', id, merge.oid);
await loadFiles();
send('SelectCommitFile', id, files[0].path);
(await openDiff(files[0].path, merge.oid, 1)).resolve(patch(files[0].path, '+right parent\n'));
await tick();
assert.match(render(), /parent: aaaaaa,cccccc/);
assert.doesNotMatch(render(), /compared with|Compare with parent/);
assert.match(render(), /right parent/);
send('SelectCommitFile', id, files[1].path);
const previousParent = await openDiff(files[1].path, merge.oid, 1);
send('SelectCommitParent', id, 2);
assert.equal(tab().selection.diff, undefined);
await loadFiles();
previousParent.resolve(patch(files[1].path, 'wrong parent'));
await tick();
assert.equal(tab().selection.diff, undefined);
send('SelectCommitFile', id, files[0].path);
(await openDiff(files[0].path, merge.oid, 2)).resolve(patch(files[0].path, '+right parent\n'));
await tick();
assert.match(render(), /diff: cccccc/);

send('ShowProjects');
assert.equal(Array.from(state.repositoryTabs)[0].selection.diff.patch.text, '+right parent\n');
send('SelectRepositoryTab', id);
assert.match(render(), /right parent/);
send('SelectCommitFile', id, files[1].path);
const closingTab = await openDiff(files[1].path, merge.oid, 2);
send('CloseRepositoryTab', id);
closingTab.resolve(patch(files[1].path, 'closed tab'));
await tick();
assert.equal(Array.from(state.repositoryTabs).length, 0);
assert.equal(requests.length, 0);
console.log('PASS: diff targets, file/commit races, close/reopen, retry, binary/empty/truncated states, escaping and tab lifetime.');
