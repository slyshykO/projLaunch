// Compile Fable first. Real state transitions, controlled reads, no user database.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg, workspaceSnapshot } from '../../src/bin/fable-js/src/State.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';

const commit = (letter, refs = []) => ({ oid: letter.repeat(40), parents: [], subject: `Commit ${letter}`, refs,
    authorName: 'Author', authorEmail: 'test@example.test', authorDate: '', commitDate: '' });
const current = commit('a', ['refs/heads/main']);
const old = commit('b');
const remoteTip = commit('c');
const branch = (name, oid, remote = false, isCurrent = false) => ({ name, oid, refName: `refs/${remote ? 'remotes' : 'heads'}/${name}`,
    isRemote: remote, isCurrent, upstream: isCurrent ? 'origin/main' : null, tracking: isCurrent ? 'ahead 1' : null });
let list = { branches: [branch('main', current.oid, false, true), branch('feature/старе<&>', old.oid), branch('origin/main', remoteTip.oid, true)],
    head: current.oid, currentBranch: 'main', remotes: [{ name: 'origin', url: 'git@example.invalid:team/repo.git' }] };
let branchError;
const logs = [], calls = [];
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: { invoke(command, args) {
    calls.push(command);
    const params = args?.params;
    if (command === 'git_open') return Promise.resolve({ repoId: params.path });
    if (command === 'git_status') return Promise.resolve({ head: current.oid, branch: 'main', entries: [] });
    if (command === 'git_branches') return branchError ? Promise.reject(branchError) : Promise.resolve(list);
    if (command === 'git_log') return new Promise((resolve, reject) => logs.push({ params, resolve, reject }));
    if (command === 'git_diff_files') return Promise.resolve([{ path: 'file.txt', status: 'M' }]);
    if (command === 'git_diff') return Promise.resolve({ text: '+line', binary: false, truncated: false });
    throw new Error(`Unexpected IPC ${command}`);
} } };
const cases = new Msg(0, []).cases();
let state = init()[0];
const dispatch = msg => {
    const [next, commands] = update(msg, state);
    state = next;
    for (const run of commands) run(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const tick = () => new Promise(resolve => setImmediate(resolve));
const tab = () => Array.from(state.repositoryTabs).find(tab => tab.id === state.activeRepositoryTab);
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch }));
const nextLog = async () => { await tick(); assert.equal(logs.length, 1); return logs.shift(); };
const page = async (commits, nextCursor) => { (await nextLog()).resolve({ commits, nextCursor }); await tick(); };

send('OpenRepositoryTab', { root: 'C:/branches-one', name: 'One' });
assert.match(render(), /Loading branches/);
await page([current], 'older');
assert.equal(tab().branches.branches.length, 3, 'All branches are present before older history loads');
let html = render();
assert.match(html, /aria-label="Repository branches"/);
assert.match(html, /aria-label="Local"/);
assert.match(html, /aria-label="Remotes"/);
assert.ok(html.includes('title="origin\ngit@example.invalid:team/repo.git"'));
assert.match(html, /aria-current="true"/);
assert.match(html, /старе&lt;&amp;&gt;/);
assert.ok(html.indexOf('repository-branches') < html.indexOf('repository-content'), 'Branches occupy the leftmost panel');
const id = tab().id;
send('NavigateToBranch', id, 'refs/heads/main');
await tick();
assert.equal(tab().selection.commit.oid, current.oid);
send('SelectCommitFile', id, 'file.txt');
await tick();
assert.ok(tab().selection.diff);
const reveal = tab().revealRevision;
send('NavigateToBranch', id, 'refs/heads/main');
assert.equal(tab().selection.diff, undefined, 'Following the same selected tip reopens the graph');
assert.ok(tab().revealRevision > reveal);

// An unloaded tip uses a fresh snapshot, then paginates to the exact full OID.
send('NavigateToBranch', id, 'refs/heads/feature/старе<&>');
assert.equal(tab().selection.commit.oid, current.oid);
assert.match(render(), /Finding branch feature/);
const fresh = await nextLog();
assert.equal(fresh.params.cursor, undefined);
fresh.resolve({ commits: [current], nextCursor: 'fresh-older' });
await tick();
const older = await nextLog();
assert.equal(older.params.cursor, 'fresh-older');
older.resolve({ commits: [old] });
await tick();
assert.equal(tab().selection.commit.oid, old.oid);
assert.equal(workspaceSnapshot(state).tabs[0].selection.commit.oid, old.oid);

// Cancelling a remote-branch jump keeps the selection despite a late response.
send('NavigateToBranch', id, 'refs/remotes/origin/main');
const cancelled = await nextLog();
send('CancelCommitNavigation', id);
cancelled.resolve({ commits: [current, remoteTip] });
await tick();
assert.equal(tab().selection.commit.oid, old.oid);
send('NavigateToBranch', id, 'refs/remotes/origin/main');
await tick();
assert.equal(tab().selection.commit.oid, remoteTip.oid);

// Branch failures are independent of history, retain old rows and can recover.
branchError = 'Branches unavailable';
send('RefreshRepositoryHistory', id);
await page([current]);
assert.equal(tab().history.error, undefined);
assert.equal(tab().branches.branches.length, 3);
assert.match(render(), /Previously loaded branches/);
branchError = undefined;
list = { branches: [branch('main', current.oid)], head: current.oid, currentBranch: null };
send('RefreshRepositoryHistory', id);
await page([current]);
assert.equal(tab().branchesError, undefined);
assert.match(render(), /Detached HEAD/);
assert.doesNotMatch(render(), /aria-current="true"/);
assert.equal(tab().branches.branches.length, 1, 'Deleted branches disappear after refresh');
list = { branches: [], head: null, currentBranch: 'new-branch' };
send('RefreshRepositoryHistory', id);
await page([]);
assert.match(render(), /new-branch · No commits yet/);

// Closed tabs never receive stale branch results or switch the active selection.
send('OpenRepositoryTab', { root: 'C:/branches-two', name: 'Two' });
const closing = tab().id;
const pending = await nextLog();
send('CloseRepositoryTab', closing);
pending.resolve({ commits: [current] });
await tick();
assert.equal(state.activeRepositoryTab, id);
assert.equal(Array.from(state.repositoryTabs).length, 1);
send('NavigateToBranch', id, 'refs/heads/not-in-list');
assert.equal(logs.length, 0);
assert.ok(calls.every(command => ['git_open', 'git_status', 'git_branches', 'git_log', 'git_diff_files', 'git_diff'].includes(command)));
console.log('PASS: complete branch list, current/remote/Unicode, tree markup, navigation/pagination, cancel, errors/retry, detached/unborn and closed-tab isolation.');
