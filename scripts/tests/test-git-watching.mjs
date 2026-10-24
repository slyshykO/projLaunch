import assert from 'node:assert/strict';
import { init, update, Msg, watchedRepositories } from '../../src/bin/fable-js/src/State.js';

const requests = [], calls = [];
let filesAvailable = true;
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: { invoke(command, args) {
    calls.push({ command, args });
    if (command === 'git_status') return Promise.resolve({ entries: [] });
    if (command === 'git_branches') return Promise.resolve({ branches: [] });
    if (command === 'git_diff_files') return Promise.resolve(filesAvailable ? [{ path: 'file.txt', status: 'M' }] : []);
    if (command === 'git_diff') return Promise.resolve({ path: 'file.txt', text: `patch-${calls.length}`, binary: false, truncated: false });
    if (command === 'frontend_log') return Promise.resolve();
    return new Promise((resolve, reject) => requests.push({ command, args, resolve, reject }));
} } };
const cases = new Msg(0, []).cases();
let state = init()[0];
const dispatch = message => {
    const [next, commands] = update(message, state);
    state = next;
    for (const command of commands) command(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const tick = () => new Promise(resolve => setImmediate(resolve));
const next = async command => {
    await tick();
    const request = requests.shift();
    assert.equal(request?.command, command);
    return request;
};
const tab = () => Array.from(state.repositoryTabs)[0];
const oid = number => number.toString(16).padStart(40, '0');
const page = start => Array.from({ length: 100 }, (_, i) => ({ oid: oid(start + i), parents: [], refs: [], subject: `Commit ${start + i}`, authorName: 'A', authorDate: '', commitDate: '' }));
const position = { top: 5000, left: 73, anchor: oid(155), offset: 4 };
const restored = () => {
    if (tab().scrollRestoreId) send('HistoryScrollRestored', tab().id, tab().scrollRestoreId, position);
};
send('OpenRepositoryTab', { root: 'C:/repo', name: 'repo' });
(await next('git_open')).resolve({ repoId: 'session' });
(await next('git_log')).resolve({ commits: page(1), nextCursor: 'page-2' });
await tick();
send('LoadMoreCommits', tab().id);
(await next('git_log')).resolve({ commits: page(101), nextCursor: 'page-3' });
await tick();
send('HistoryScrolled', tab().id, position);

// Repeated events while a refresh is busy are retained for one subsequent pass.
send('GitChanged', undefined, 'C:/repo', false, true);
const statusOpen = await next('git_open');
send('GitChanged', undefined, 'C:/repo', false, true);
send('GitChanged', undefined, 'C:/repo', false, true);
assert.equal(requests.length, 0);
statusOpen.resolve({ repoId: 'session' });
await tick();
assert.equal(requests.length, 0, 'Worktree changes do not read history pages');
assert.equal(tab().history.commits.length, 200);
assert.equal(tab().history.nextCursor, 'page-3');
assert.deepEqual(tab().historyView.scroll, position);
restored(); send('ApplyGitChanges');
(await next('git_open')).resolve({ repoId: 'session' });
await tick(); restored();

// A refs change replaces the full loaded depth atomically and keeps scroll.
send('GitChanged', undefined, 'C:/repo', true, true);
(await next('git_open')).resolve({ repoId: 'session' });
(await next('git_log')).resolve({ commits: page(501), nextCursor: 'new-2' });
const second = await next('git_log');
assert.equal(tab().history.commits[0].oid, oid(1), 'Old rows stay visible until all replacement pages arrive');
assert.equal(second.args.params.cursor, 'new-2');
second.resolve({ commits: page(601), nextCursor: 'new-3' });
await tick();
assert.equal(tab().history.commits.length, 200);
assert.equal(tab().history.commits[0].oid, oid(501));
assert.deepEqual(tab().historyView.scroll, position);
restored();

// Background tabs defer refresh; identical paths in other endpoints are isolated.
send('ShowProjects');
send('GitChanged', undefined, 'C:/repo', true, true);
await tick(); assert.equal(requests.length, 0);
send('GitChanged', { kind: 'wsl', distribution: 'Ubuntu' }, 'C:/repo', true, true);
send('SelectRepositoryTab', tab().id); send('ApplyGitChanges');
(await next('git_open')).resolve({ repoId: 'session' });
(await next('git_log')).resolve({ commits: page(1), nextCursor: 'fail-next' });
(await next('git_log')).reject(new Error('connection lost'));
await tick();
assert.equal(tab().history.commits[0].oid, oid(501), 'Failed replacement preserves old rows');
restored();

// Successful re-subscription gets fresh handles and refreshes the selected mutable diff.
send('GitChanged', undefined, 'C:/repo', true, true);
(await next('git_open')).resolve({ repoId: 'new-session' });
(await next('git_log')).resolve({ commits: page(1) });
await tick(); restored();
send('SelectWorkingTree', tab().id);
(await next('git_open')).resolve({ repoId: 'new-session' });
await tick();
const unstaged = tab().selection.groups[1][0];
send('SelectWorkingTreeFile', tab().id, unstaged, 'file.txt');
(await next('git_open')).resolve({ repoId: 'new-session' });
await tick();
const oldPatch = tab().selection.diff.patch.text;
send('GitChanged', undefined, 'C:/repo', false, true);
assert.equal(tab().selection.diff.patch.text, oldPatch, 'Automatic updates keep the diff mounted');
(await next('git_open')).resolve({ repoId: 'new-session' });
(await next('git_open')).resolve({ repoId: 'new-session' }); // file groups
(await next('git_open')).resolve({ repoId: 'new-session' }); // restored diff
await tick();
assert.equal(tab().selection.diff.file.path, 'file.txt');
assert.notEqual(tab().selection.diff.patch.text, oldPatch);
// History is hidden behind the diff, so its pending scroll token must not
// prevent subsequent edits from refreshing that diff.
assert.ok(tab().scrollRestoreId);
send('GitChanged', undefined, 'C:/repo', false, true);
(await next('git_open')).resolve({ repoId: 'new-session' });
(await next('git_open')).resolve({ repoId: 'new-session' });
(await next('git_open')).resolve({ repoId: 'new-session' });
await tick();
assert.equal(Array.from(watchedRepositories(state)).length, 1);
filesAvailable = false;
send('GitChanged', undefined, 'C:/repo', false, true);
(await next('git_open')).resolve({ repoId: 'new-session' });
(await next('git_open')).resolve({ repoId: 'new-session' });
await tick();
assert.equal(tab().selection.diff, undefined, 'A removed/cleaned file closes its obsolete diff');
send('CloseRepositoryTab', tab().id);
assert.equal(Array.from(watchedRepositories(state)).length, 0);
send('GitChanged', undefined, 'C:/repo', true, true);
await tick(); assert.equal(requests.length, 0);
console.log('PASS: Watch refreshes preserve depth/scroll, coalesce busy reads, defer background tabs and reload mutable diffs.');
