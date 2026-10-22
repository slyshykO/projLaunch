// Compile Fable first. No application database or user's repository is modified.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg } from '../../src/bin/fable-js/src/State.js';
import { DiffTarget } from '../../src/bin/fable-js/src/domain/GitProtocol.js';
import { withWorkingTree, workingTreeOid } from '../../src/bin/fable-js/src/domain/CommitGraph.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';

const requests = [];
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: {
    invoke(command, args) {
        if (command === 'git_branches') return Promise.resolve({ branches: [], head: null, currentBranch: null });
        return new Promise((resolve, reject) => requests.push({ command, params: args.params, resolve, reject }));
    },
} };
const cases = new Msg(0, []).cases();
let state = init()[0];
const dispatch = message => {
    const [updated, commands] = update(message, state);
    state = updated;
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
const tab = () => Array.from(state.repositoryTabs).find(item => item.id === state.activeRepositoryTab);
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch() {} }));
const commit = (oid, parents = []) => ({
    oid, parents, subject: `Commit ${oid}`, refs: [], authorName: 'Author', authorEmail: 'test@example.test',
    authorDate: '2026-10-21T10:00:00Z', commitDate: '2026-10-21T10:00:00Z',
});
const history = [commit('other', ['root']), commit('head', ['root']), commit('root')];
const dirty = { head: 'head', branch: 'main', entries: [
    { path: 'partially-staged.txt', index: 'M', worktree: 'M', kind: 'tracked' },
    { path: '<новый>.txt', index: '?', worktree: '?', kind: 'untracked' },
] };
const staged = new DiffTarget(1, []);
const unstaged = new DiffTarget(0, []);
const untracked = new DiffTarget(3, []);
const groups = [
    [{ path: 'partially-staged.txt', status: 'M' }, { path: 'new-name.txt', originalPath: 'old-name.txt', status: 'R100' }],
    [{ path: 'partially-staged.txt', status: 'M' }, { path: 'conflict.txt', status: 'U' }, { path: 'deleted.txt', status: 'D' }],
    [{ path: '<новый>.txt', status: '?' }],
];
const finishRefresh = async (status = dirty, commits = history) => {
    (await next('git_open')).resolve({ repoId: 'fresh-session' });
    (await next('git_log')).resolve({ commits, nextCursor: 'next-page' });
    const request = await next('git_status');
    assert.equal(request.params.repoId, 'fresh-session');
    request.resolve(status);
    await tick();
};
const finishFiles = async (values = groups) => {
    (await next('git_open')).resolve({ repoId: 'files-session' });
    for (const [index, kind] of ['staged', 'unstaged', 'untracked'].entries()) {
        const request = await next('git_diff_files');
        assert.deepEqual(request.params.target, { kind });
        request.resolve(values[index]);
    }
    await tick();
};
const openDiff = async (kind, path) => {
    (await next('git_open')).resolve({ repoId: 'diff-session' });
    const request = await next('git_diff');
    assert.deepEqual(request.params.target, { kind });
    assert.equal(request.params.path, path);
    return request;
};
const patch = text => ({ text, binary: false, truncated: false, untracked: false });

// The dotted path traverses unrelated tips and terminates at HEAD, then becomes solid.
const [displayed, graph] = withWorkingTree(dirty, history);
assert.equal(displayed[0].oid, workingTreeOid);
assert.deepEqual(displayed[0].parents, ['head']);
assert.equal(graph.rows[0].uncommitted, true);
assert.equal(graph.rows[1].passing.find(edge => edge.color === 0).fromLane, graph.rows[0].parents[0].toLane);
assert.equal(graph.rows[1].dashedBelow, true);
assert.equal(graph.rows[2].oid, 'head');
assert.equal(graph.rows[2].dashedAbove, true);
assert.equal(graph.rows[2].dashedBelow, false);
assert.equal(graph.rows[3].dashedAbove, false);
const [, partial] = withWorkingTree(dirty, history.slice(0, 1));
assert.ok(partial.continuations.some(lane => lane.target === 'head'));
assert.deepEqual(partial.rows, graph.rows.slice(0, 2), 'Loading more keeps existing routes and dashed styling');
assert.equal(withWorkingTree({ entries: [] }, history)[0].length, history.length);
const [unborn, unbornGraph] = withWorkingTree({ entries: dirty.entries }, []);
assert.equal(unborn.length, 1);
assert.equal(unbornGraph.rows[0].parents.length, 0, 'Unborn HEAD has no invented parent');

send('OpenRepositoryTab', { root: 'C:/fixture', name: 'fixture' });
await finishRefresh();
const id = tab().id;
assert.match(render(), /Uncommitted changes/);
assert.match(render(), /stroke-dasharray="4,4"/);
assert.equal(tab().history.commits.length, 3, 'Synthetic node stays out of history state');
send('SelectWorkingTree', id);
await finishFiles();
assert.equal(tab().selection.commit, undefined);
assert.equal(tab().selection.files.length, 5, 'Count distinct paths across index and working tree');
assert.match(render(), /Staged · HEAD → index/);
assert.match(render(), /Unstaged · index → working tree/);
assert.match(render(), /Untracked · new files/);
assert.match(render(), /&lt;новый&gt;.txt/);
assert.match(render(), /Conflict/);
assert.match(render(), /old-name.txt/);

// Same path in index and worktree is two independently selectable patches.
send('SelectWorkingTreeFile', id, staged, 'partially-staged.txt');
const stale = await openDiff('staged', 'partially-staged.txt');
send('SelectWorkingTreeFile', id, unstaged, 'partially-staged.txt');
(await openDiff('unstaged', 'partially-staged.txt')).resolve(patch('+worktree-only'));
await tick();
stale.resolve(patch('+obsolete-index'));
await tick();
assert.match(render(), /worktree-only/);
assert.doesNotMatch(render(), /obsolete-index/);
send('SelectWorkingTreeFile', id, untracked, '<новый>.txt');
(await openDiff('untracked', '<новый>.txt')).resolve({ ...patch('<script>new file</script>'), untracked: true });
await tick();
assert.match(render(), /New file preview/);
assert.match(render(), /&lt;script&gt;new file&lt;\/script&gt;/);

send('SelectWorkingTreeFile', id, staged, 'new-name.txt');
(await openDiff('staged', 'new-name.txt')).reject('Session lost');
await tick();
assert.match(render(), /Retry/);
send('RetryCommitDiff', id);
(await openDiff('staged', 'new-name.txt')).resolve({ ...patch(''), binary: true });
await tick();
assert.match(render(), /Binary file/);

// Refresh invalidates an in-flight mutable patch and reloads the selected file groups.
send('SelectWorkingTreeFile', id, unstaged, 'partially-staged.txt');
const outdated = await openDiff('unstaged', 'partially-staged.txt');
send('RefreshRepositoryHistory', id);
assert.equal(tab().selection.diff, undefined);
await finishRefresh({ head: 'head', entries: [] });
await finishFiles([[], [], []]);
outdated.resolve(patch('+outdated after refresh'));
await tick();
assert.match(render(), /Working tree clean/);
assert.match(render(), /No changed files/);
assert.doesNotMatch(render(), /outdated after refresh/);

// Status failure must not masquerade as a clean worktree or erase commit history.
send('SelectRepositoryCommit', id, 'head');
(await next('git_open')).resolve({ repoId: 'commit-files' });
(await next('git_diff_files')).resolve([]);
await tick();
send('RefreshRepositoryHistory', id);
(await next('git_open')).resolve({ repoId: 'new' });
(await next('git_log')).resolve({ commits: history });
(await next('git_status')).reject('Status unavailable');
await tick();
assert.match(render(), /Could not load uncommitted changes/);
assert.doesNotMatch(render(), /Working tree clean/);
assert.equal(tab().selection.commit.oid, 'head');
assert.equal(tab().history.commits.length, 3);

// Empty repos still show and open untracked files; late replies cannot reopen closed tabs.
send('OpenRepositoryTab', { root: 'C:/empty', name: 'empty' });
await finishRefresh({ entries: dirty.entries }, []);
assert.match(render(), /Uncommitted changes/);
assert.doesNotMatch(render(), /No commits yet/);
send('SelectWorkingTree', tab().id);
await finishFiles([[], [], groups[2]]);
const emptyId = tab().id;
send('SelectWorkingTreeFile', emptyId, untracked, '<новый>.txt');
const closing = await openDiff('untracked', '<новый>.txt');
send('CloseRepositoryTab', emptyId);
closing.resolve(patch('closed-tab-result'));
await tick();
assert.equal(tab().id, id);
assert.equal(tab().selection.commit.oid, 'head');
assert.equal(requests.length, 0);
console.log('PASS: working-tree HEAD graph, unborn/clean/error states, grouped targets, partial staging, previews, refresh, stale replies and tab isolation.');
