// Real Elmish update/commands and rendered controls; never accesses application data.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg } from '../../src/bin/fable-js/src/State.js';
import { DiffTarget } from '../../src/bin/fable-js/src/domain/GitProtocol.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';

const cases = new Msg(0, []).cases();
const staged = new DiffTarget(1, []);
const unstaged = new DiffTarget(0, []);
const untracked = new DiffTarget(3, []);
const partial = { path: 'partial.txt', status: 'M' };
const other = { path: 'other.txt', status: 'M' };
const newFile = { path: '--новый [1].txt', status: '?' };
const commit = { oid: 'head', parents: [], subject: 'Base', refs: [], authorName: 'A', authorEmail: 'a@b', authorDate: '', commitDate: '' };
let groups = { staged: [partial], unstaged: [partial, other], untracked: [newFile] };
let state = init()[0];
const calls = [];
const writes = [];
const delayedDiffs = [];
let delayDiff = false;
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: { invoke(command, args) {
    calls.push({ command, ...args });
    if (command === 'git_open') return Promise.resolve({ repoId: 'session' });
    if (command === 'git_log') return Promise.resolve({ commits: [commit], nextCursor: 'more' });
    if (command === 'git_status') return Promise.resolve({ head: 'head', branch: 'main', entries: [{ path: 'partial.txt', kind: 'tracked', index: 'M', worktree: 'M' }] });
    if (command === 'git_branches') return Promise.resolve({ branches: [] });
    if (command === 'git_diff_files') return Promise.resolve(groups[args.params.target.kind]);
    if (command === 'git_diff') {
        if (delayDiff) return new Promise(resolve => delayedDiffs.push(resolve));
        return Promise.resolve({ path: args.params.path, text: `fresh ${args.params.target.kind}`, binary: false, truncated: false, untracked: false });
    }
    if (command === 'git_stage' || command === 'git_unstage') return new Promise((resolve, reject) => writes.push({ command, ...args, resolve, reject }));
    throw new Error(`Unexpected IPC ${command}`);
} } };
const dispatch = message => {
    const [next, commands] = update(message, state);
    state = next;
    for (const run of commands) run(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const settle = async () => { for (let i = 0; i < 12; i++) await new Promise(resolve => setImmediate(resolve)); };
const tab = () => Array.from(state.repositoryTabs).find(t => t.id === state.activeRepositoryTab);
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch }));
const open = async (endpoint, root = '/fixture') => {
    send('OpenRepositoryTab', { root, name: 'fixture', endpoint });
    await settle();
    return tab().id;
};
const id = await open(undefined);
send('SelectWorkingTree', id);
await settle();
assert.match(render(), /Stage all \(2\)/);
assert.match(render(), /Unstage all \(1\)/);
assert.match(render(), /Unstage keeps working files intact/);
send('CheckWorkingTreeFile', id, unstaged, partial.path, true);
send('CheckWorkingTreeFile', id, unstaged, other.path, true);
assert.match(render(), /Stage selected \(2\)/);

// No writes for unknown paths, empty selections, or immutable commit views.
send('ChangeIndex', id, unstaged, []);
send('ChangeIndex', id, unstaged, ['missing']);
assert.equal(writes.length, 0);
delayDiff = true;
send('SelectWorkingTreeFile', id, unstaged, partial.path);
await settle();
const logsBefore = calls.filter(c => c.command === 'git_log').length;
send('ChangeIndex', id, unstaged, [partial.path, other.path, partial.path]);
await settle();
const write = writes.shift();
assert.equal(write.command, 'git_stage');
assert.deepEqual(write.params.paths, [partial.path, other.path]);
assert.deepEqual(write.params.target, { kind: 'unstaged' });
assert.match(render(), /Updating index/);
assert.match(render(), /disabled="" aria-label="Stage all in unstaged"/);
const duringWrite = calls.length;
send('ChangeIndex', id, unstaged, [other.path]);
send('RefreshRepositoryHistory', id);
send('GitChanged', undefined, '/fixture', false, true);
send('Tick', new Date());
await settle();
assert.equal(calls.length, duringWrite, 'Watchers and duplicate actions wait for the write');
delayedDiffs.shift()({ text: 'obsolete patch' });
await settle();
assert.doesNotMatch(render(), /obsolete patch/);
delayDiff = false;
groups = { staged: [partial, other], unstaged: [], untracked: [newFile] };
write.resolve(true);
await settle();
assert.equal(tab().indexRequestId, undefined);
assert.equal(tab().selection.diff.target.tag, staged.tag);
assert.match(render(), /fresh staged/);
assert.equal(calls.filter(c => c.command === 'git_log').length, logsBefore, 'Index change preserves history pages/cursor');
assert.equal(tab().history.nextCursor, 'more');

send('ChangeIndex', id, staged, [partial.path]);
await settle();
assert.equal(writes[0].command, 'git_unstage');
groups = { staged: [other], unstaged: [partial], untracked: [newFile] };
writes.shift().resolve(true);
await settle();
assert.equal(tab().selection.diff.target.tag, unstaged.tag);
assert.match(render(), /fresh unstaged/);

// Preserve selection on failure, refresh even on errors, no automatic mutation retry.
send('CheckWorkingTreeFile', id, unstaged, partial.path, true);
send('ChangeIndex', id, unstaged, [partial.path]);
await settle();
writes.shift().reject(new Error('index.lock is held'));
await settle();
assert.match(render(), /index.lock is held/);
assert.match(render(), /Stage selected \(1\)/);
assert.equal(writes.length, 0);
assert.equal(tab().indexRequestId, undefined);

// Newly added files return to Untracked; diff follows the path across groups.
send('SelectWorkingTreeFile', id, untracked, newFile.path);
await settle();
send('ChangeIndex', id, untracked, [newFile.path]);
await settle();
groups = { staged: [other, { ...newFile, status: 'A' }], unstaged: [partial], untracked: [] };
writes.shift().resolve(true);
await settle();
assert.equal(tab().selection.diff.target.tag, staged.tag);
send('ChangeIndex', id, staged, [newFile.path]);
await settle();
groups = { staged: [other], unstaged: [partial], untracked: [newFile] };
writes.shift().resolve(true);
await settle();
assert.equal(tab().selection.diff.target.tag, untracked.tag);

// Closing and reopening never frees the pending operation for this worktree.
send('ChangeIndex', id, unstaged, [partial.path]);
await settle();
const pending = writes.shift();
send('CloseRepositoryTab', id);
const reopened = await open(undefined);
assert.notEqual(reopened, id);
assert.ok(tab().indexRequestId);
const readCount = calls.length;
send('RefreshRepositoryHistory', reopened);
await settle();
assert.equal(calls.length, readCount);
pending.resolve(true);
await settle();
assert.equal(tab().indexRequestId, undefined);
assert.ok(tab().history.hasLoaded);
send('SelectRepositoryCommit', reopened, 'head');
await settle();
send('ChangeIndex', reopened, staged, [other.path]);
await settle();
assert.equal(writes.length, 0);
assert.doesNotMatch(render(), /Stage all|Unstage all/);

// Local, WSL and SSH use the same typed bridge, keeping endpoint + root identity.
for (const endpoint of [{ kind: 'wsl', distribution: 'Ubuntu' }, { kind: 'ssh', host: 'example', username: 'tester' }]) {
    const remoteId = await open(endpoint);
    send('SelectWorkingTree', remoteId);
    await settle();
    send('ChangeIndex', remoteId, unstaged, [partial.path]);
    await settle();
    const remote = writes.shift();
    assert.deepEqual(remote.endpoint, endpoint);
    assert.equal(remote.params.repoId, 'session');
    send('CloseRepositoryTab', remoteId);
    remote.resolve(true);
    await settle();
    assert.ok(!Array.from(state.repositoryTabs).some(t => t.id === remoteId));
}
assert.equal(writes.length, 0);
console.log('Index changes: selection, group controls, RPC routing, races, refresh, errors and tab lifecycle passed.');
