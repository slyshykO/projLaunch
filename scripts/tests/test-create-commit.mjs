// Actual Elmish commands and rendered UI with isolated IPC; no user repositories.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg } from '../../src/bin/fable-js/src/State.js';
import { DiffTarget } from '../../src/bin/fable-js/src/domain/GitProtocol.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';

const cases = new Msg(0, []).cases();
const staged = new DiffTarget(1, []);
const unstaged = new DiffTarget(0, []);
const partial = { path: 'partial.txt', status: 'M' };
const base = { oid: 'a'.repeat(40), parents: [], subject: 'Base', refs: [], authorName: 'A', authorEmail: 'a@b', authorDate: '', commitDate: '' };
const created = { ...base, oid: 'b'.repeat(40), subject: 'New commit', parents: [base.oid] };
let groups = { staged: [partial], unstaged: [partial], untracked: [] };
let history = [base];
let status = { head: base.oid, branch: 'main', entries: [{ path: partial.path, kind: 'tracked', index: 'M', worktree: 'M' }] };
let state = init()[0];
const calls = [];
const writes = [];
const delayedDiffs = [];
let delayDiff = false;
let statusFailure = false;
const responses = [];
let runtime = 'session-1';
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: { invoke(command, args) {
    calls.push({ command, ...args });
    if (command === 'git_open') return Promise.resolve({ repoId: runtime });
    if (command === 'git_log') return Promise.resolve({ commits: history, nextCursor: 'more' });
    if (command === 'git_status') return statusFailure ? Promise.reject(new Error('Status unavailable')) : Promise.resolve(status);
    if (command === 'git_branches') return Promise.resolve({ branches: [], head: status.head });
    if (command === 'git_diff_files') return Promise.resolve(groups[args.params.target.kind] ?? []);
    if (command === 'git_diff') {
        if (delayDiff) return new Promise(resolve => delayedDiffs.push(resolve));
        return Promise.resolve({ path: args.params.path, text: `fresh ${args.params.target.kind}`, binary: false, truncated: false });
    }
    if (['git_commit', 'git_stage', 'git_unstage'].includes(command)) {
        return new Promise((resolve, reject) => writes.push({ command, ...args, resolve, reject }));
    }
    throw new Error(`Unexpected IPC ${command}`);
} } };
const dispatch = message => {
    if (cases[message.tag] === 'OnCommitCreated') responses.push(message);
    const [next, commands] = update(message, state);
    state = next;
    for (const run of commands) run(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const settle = async () => { for (let i = 0; i < 16; i++) await new Promise(resolve => setImmediate(resolve)); };
const tab = () => Array.from(state.repositoryTabs).find(t => t.id === state.activeRepositoryTab);
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch }));
const open = async endpoint => {
    send('OpenRepositoryTab', { root: '/fixture', name: 'fixture', endpoint });
    await settle();
    send('SelectWorkingTree', tab().id);
    await settle();
    return tab().id;
};
let id = await open(undefined);
assert.match(render(), /Includes all Staged changes \(1 files\), regardless of checkboxes/);
assert.match(render(), /Commit to main/);
for (const message of ['', ' \n\t', 'nul\0message']) {
    send('EditCommitMessage', id, message);
    send('CreateCommit', id);
}
assert.equal(writes.length, 0);
const message = 'Коммит <script>\n\nDetailed body';
send('EditCommitMessage', id, message);
assert.match(render(), /Коммит &lt;script&gt;/);
send('ShowProjects');
send('SelectRepositoryTab', id);
assert.equal(tab().commitDraft.message, message);

// A late patch cannot overwrite the post-commit diff. All writes and refreshes wait.
delayDiff = true;
send('SelectWorkingTreeFile', id, staged, partial.path);
await settle();
runtime = 'session-2';
send('CreateCommit', id);
await settle();
const pending = writes.shift();
assert.equal(pending.command, 'git_commit');
assert.equal(pending.params.message, message);
assert.equal(pending.params.repoId, 'session-2', 'Reopen instead of reusing a stale runtime repo ID');
assert.equal(pending.params.expectedHead, base.oid);
assert.equal(pending.params.expectedBranch, 'main');
assert.ok(!('paths' in pending.params), 'Commit uses the entire index');
assert.match(render(), /Creating commit/);
assert.match(render(), /disabled="" aria-busy="true">Committing/);
const count = calls.length;
send('CreateCommit', id);
send('EditCommitMessage', id, 'edited while pending');
send('ChangeIndex', id, staged, [partial.path]);
send('RefreshRepositoryHistory', id);
send('GitChanged', undefined, '/fixture', true, true);
send('Tick', new Date());
await settle();
assert.equal(calls.length, count);
assert.equal(tab().commitDraft.message, message);
state.isWindowClosePending = true;
assert.equal(Array.from(update(new Msg(cases.indexOf('ContinueWindowClose'), []), state)[1]).length, 0,
    'Normal window close waits for a pending Git write');
state.isWindowClosePending = false;
delayedDiffs.shift()({ text: 'obsolete patch' });
await settle();
assert.doesNotMatch(render(), /obsolete patch/);
delayDiff = false;
groups = { staged: [], unstaged: [partial], untracked: [] };
history = [created, base];
status = { ...status, head: created.oid };
pending.resolve({ oid: created.oid });
await settle();
assert.equal(tab().commitDraft.message, '');
assert.equal(tab().gitWriteRequestId, undefined);
assert.equal(tab().history.commits[0].oid, created.oid);
assert.equal(tab().branches.head, created.oid);
assert.equal(tab().selection.diff.target.tag, unstaged.tag);
assert.match(render(), /fresh unstaged/);
assert.match(render(), /Created commit bbbbb/);
assert.match(render(), /Stage changes to create a commit/);
assert.ok(calls.slice(count).some(c => c.command === 'git_log' && c.params.cursor == null));
send('EditCommitMessage', id, 'Empty index');
send('CreateCommit', id);
await settle();
assert.equal(writes.length, 0);

// Mutual exclusion with staging, including refreshed data before commit becomes available.
send('ChangeIndex', id, unstaged, [partial.path]);
await settle();
send('CreateCommit', id);
assert.equal(writes.length, 1);
groups = { staged: [partial], unstaged: [], untracked: [] };
writes.shift().resolve(true);
await settle();
send('EditCommitMessage', id, 'Keep on failure');
send('CreateCommit', id);
await settle();
const failed = writes.shift();
const logsBeforeFailure = calls.filter(c => c.command === 'git_log').length;
failed.reject(new Error('commit-msg hook rejected'));
await settle();
assert.equal(tab().commitDraft.message, 'Keep on failure');
assert.match(render(), /commit-msg hook rejected/);
assert.match(render(), /Review refreshed history and changes before retrying/);
assert.ok(calls.filter(c => c.command === 'git_log').length > logsBeforeFailure);
assert.equal(writes.length, 0, 'Never retry a possibly completed mutation automatically');

// Draft/outcome survives tab closure, including completion while the tab is absent.
send('CreateCommit', id);
await settle();
const closed = writes.shift();
const activeRequest = tab().gitWriteRequestId;
dispatch(responses[0]);
assert.equal(tab().gitWriteRequestId, activeRequest, 'Late completion cannot release a newer write');
send('CloseRepositoryTab', id);
id = await open(undefined);
assert.ok(tab().gitWriteRequestId);
assert.match(render(), /Creating commit/);
send('CreateCommit', id);
await settle();
assert.equal(writes.length, 0);
send('CloseRepositoryTab', id);
closed.reject(new Error('Connection lost'));
await settle();
id = await open(undefined);
assert.equal(tab().commitDraft.message, 'Keep on failure');
assert.match(render(), /Connection lost/);

statusFailure = true;
send('RefreshRepositoryHistory', id);
await settle();
send('CreateCommit', id);
assert.equal(writes.length, 0);
statusFailure = false;

// Conflicts disable committing; initial and detached destinations are explicit.
for (const next of [
    { ...status, entries: [{ ...status.entries[0], kind: 'conflict' }] },
    { ...status, head: undefined },
    { ...status, branch: undefined },
]) {
    status = next;
    send('RefreshRepositoryHistory', id);
    await settle();
    if (status.entries[0].kind === 'conflict') {
        send('CreateCommit', id);
        assert.equal(writes.length, 0);
        assert.match(render(), /Resolve all conflicts/);
    } else if (status.head == null) {
        assert.match(render(), /First commit on main/);
    } else {
        assert.match(render(), /Detached HEAD/);
    }
}

// Endpoint identity isolates drafts and routes the same command to WSL and SSH.
status = { head: base.oid, branch: 'main', entries: [] };
for (const endpoint of [{ kind: 'wsl', distribution: 'Ubuntu' }, { kind: 'ssh', host: 'example', username: 'tester' }]) {
    const remoteId = await open(endpoint);
    assert.equal(tab().commitDraft.message, '');
    send('EditCommitMessage', remoteId, 'Remote');
    send('CreateCommit', remoteId);
    await settle();
    const remote = writes.shift();
    assert.equal(remote.command, 'git_commit');
    assert.deepEqual(remote.endpoint, endpoint);
    remote.resolve({ oid: created.oid });
    await settle();
    send('SelectRepositoryCommit', remoteId, base.oid);
    await settle();
    send('EditCommitMessage', remoteId, 'Not working tree');
    send('CreateCommit', remoteId);
    await settle();
    assert.equal(writes.length, 0);
    assert.doesNotMatch(render(), /<textarea/);
}
console.log('Create commit: validation, routing, write/read races, refresh, errors, draft and tab lifecycle passed.');
