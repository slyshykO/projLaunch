// Fable state + isolated IPC. Every restart creates a new Elmish model.
import assert from 'node:assert/strict';
import { init, update, Msg, workspaceSnapshot } from '../../src/bin/fable-js/src/State.js';

const requests = [];
const writes = [];
const native = [];
let disk;
let writing = false;
globalThis.isTauri = true;
globalThis.window = Object.assign(new EventTarget(), { __TAURI_INTERNALS__: {
    metadata: { currentWindow: { label: 'main' } },
    invoke(command, args) {
        if (command === 'git_branches') return Promise.resolve({ branches: [], head: null, currentBranch: null });
        if (command === 'get_storage_access') return Promise.resolve({ canWrite: true });
        if (command === 'save_workspace') {
            assert.equal(writing, false, 'Workspace writes must be serialized');
            writing = true;
            return new Promise((resolve, reject) => writes.push({
                value: JSON.parse(JSON.stringify(args.workspace)),
                resolve() { disk = this.value; writing = false; resolve(); },
                reject(error) { writing = false; reject(error); },
            }));
        }
        if (command.startsWith('plugin:') || command === 'frontend_log') {
            native.push(command);
            return Promise.resolve();
        }
        return new Promise((resolve, reject) => requests.push({ command, params: args?.params, resolve, reject }));
    },
} });
const cases = new Msg(0, []).cases();
let state = init()[0];
const dispatch = message => {
    const [next, commands] = update(message, state);
    state = next;
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
const flush = async () => {
    await tick();
    while (writes.length) { writes.shift().resolve(); await tick(); }
};
const tabs = () => Array.from(state.repositoryTabs);
const tab = root => tabs().find(tab => tab.root === root);
const oid = 'a'.repeat(40);
const selectedCommit = {
    oid, parents: [], subject: 'Older selected commit', refs: [],
    authorName: 'Author', authorEmail: 'test@example.test', authorDate: '2026-10-21T10:00:00Z', commitDate: '2026-10-21T10:00:00Z',
};
const saved = {
    version: 1, activeRoot: 'C:/one', tabs: [
        { root: 'C:/one', name: 'one', filesWidth: 31, filesHeight: 56, selection: { commit: selectedCommit, parent: null, file: { target: 'commit', path: 'old.txt' } } },
        { root: 'C:/two', name: 'two', filesWidth: 65, filesHeight: 28, selection: { commit: null, parent: null, file: { target: 'unstaged', path: 'live.txt' } } },
    ],
};
const finishHistory = async () => {
    (await next('git_open')).resolve({ repoId: 'new-runtime-id' });
    const history = await next('git_log');
    assert.equal(history.params.cursor, undefined);
    history.resolve({ commits: [] });
    (await next('git_status')).resolve({ entries: [] });
    await tick();
};
const finishCommittedFiles = async (files = [{ path: 'old.txt', status: 'M' }]) => {
    (await next('git_open')).resolve({ repoId: 'new-file-id' });
    const request = await next('git_diff_files');
    assert.equal(request.params.target.oid, oid, 'Restore a selected commit even outside the first history page');
    request.resolve(files);
    await tick();
};
const finishPatch = async (kind, path) => {
    (await next('git_open')).resolve({ repoId: 'new-diff-id' });
    const request = await next('git_diff');
    assert.equal(request.params.target.kind, kind);
    assert.equal(request.params.path, path);
    request.resolve({ path, text: '+fresh data', binary: false, truncated: false, untracked: false });
    await tick();
};

send('LoadWorkspace');
(await next('load_workspace')).resolve(saved);
await tick();
assert.deepEqual(tabs().map(tab => tab.root), ['C:/one', 'C:/two']);
assert.equal(state.activeRepositoryTab, tab('C:/one').id);
assert.equal(tab('C:/one').filesWidth, 31);
assert.equal(tab('C:/one').filesHeight, 56);
assert.equal(tab('C:/two').history.hasLoaded, false, 'Background restored tabs load lazily');
assert.equal(tab('C:/two').history.repoId, undefined);
await finishHistory();
await finishCommittedFiles();
await finishPatch('commit', 'old.txt');
assert.equal(tab('C:/one').selection.diff.patch.text, '+fresh data');
assert.equal(tab('C:/one').restoreFile, undefined);
await flush();

// A working-tree selection restores its target and refetches current files/patches.
send('SelectRepositoryTab', tab('C:/two').id);
await finishHistory();
(await next('git_open')).resolve({ repoId: 'live-files' });
for (const kind of ['staged', 'unstaged', 'untracked']) {
    const request = await next('git_diff_files');
    assert.equal(request.params.target.kind, kind);
    request.resolve(kind === 'unstaged' ? [{ path: 'live.txt', status: 'M' }] : []);
}
await finishPatch('unstaged', 'live.txt');
await flush();
const stable = JSON.parse(JSON.stringify(workspaceSnapshot(state)));
assert.equal(stable.activeRoot, 'C:/two');
assert.ok(!JSON.stringify(stable).includes('new-runtime-id'));
assert.ok(!JSON.stringify(stable).includes('fresh data'));
assert.ok(!JSON.stringify(stable).includes('requestId'));

// Coalesce rapid changes behind one in-flight write, then flush the newest snapshot.
send('ResizeRepositoryPanels', tab('C:/one').id, true, 45);
await tick();
assert.equal(writes.length, 1);
send('ResizeRepositoryPanels', tab('C:/one').id, true, 51);
send('ResizeRepositoryPanels', tab('C:/one').id, false, 90);
send('ShowProjects');
assert.equal(writes.length, 1);
await flush();
assert.equal(disk.tabs[0].filesWidth, 51);
assert.equal(disk.tabs[0].filesHeight, 70);
assert.equal(disk.activeRoot, undefined);

// A new process has fresh runtime identities; the Projects tab is restored too.
const oldIds = tabs().map(tab => tab.id);
state = init()[0];
send('LoadWorkspace');
(await next('load_workspace')).resolve(disk);
await tick();
assert.equal(state.activeRepositoryTab, undefined);
assert.ok(tabs().every(tab => !oldIds.includes(tab.id)));
assert.equal(requests.length, 0, 'Projects activation does not start background Git reads');
assert.equal(tab('C:/one').filesWidth, 51);
send('CloseRepositoryTab', tab('C:/two').id);
await flush();
assert.deepEqual(disk.tabs.map(tab => tab.root), ['C:/one']);

// Save failure is visible and retry preserves the latest layout.
send('ResizeRepositoryPanels', tab('C:/one').id, true, 37);
await tick();
writes.shift().reject('Database is locked');
await tick();
assert.match(state.workspaceError, /Database is locked/);
assert.equal(disk.tabs[0].filesWidth, 51);
send('SaveWorkspace');
await flush();
assert.equal(disk.tabs[0].filesWidth, 37);
assert.equal(state.workspaceError, undefined);

// Closing waits for the active writer and any newer state before native close.
send('ResizeRepositoryPanels', tab('C:/one').id, true, 42);
send('WindowSave');
await tick();
assert.ok(!native.includes('plugin:window|close'));
writes.shift().reject('Disk is full');
await tick();
assert.equal(state.isWindowClosePending, false, 'A failed flush keeps the window open');
assert.ok(!native.includes('plugin:window|close'));
send('WindowSave');
send('ShowProjects');
await flush();
await tick();
assert.ok(native.includes('plugin:window|close'));
assert.equal(disk.tabs[0].filesWidth, 42);

// Failed load cannot silently replace the stored workspace with an empty model.
state = init()[0];
send('LoadWorkspace');
(await next('load_workspace')).reject('Unsupported workspace state version 2');
await tick();
assert.equal(state.workspaceReady, false);
assert.match(state.workspaceError, /Unsupported workspace/);
send('SaveWorkspace');
assert.equal(writes.length, 0);
send('LoadWorkspace');
(await next('load_workspace')).resolve(disk);
await tick();
assert.equal(state.workspaceReady, true);

// Missing files do not reopen a stale diff; the commit selection remains usable.
send('SelectRepositoryTab', tab('C:/one').id);
await finishHistory();
await finishCommittedFiles([]);
await flush();
assert.equal(tab('C:/one').selection.commit.oid, oid);
assert.equal(tab('C:/one').selection.diff, undefined);
assert.equal(tab('C:/one').restoreFile, undefined);
assert.equal(requests.length, 0);

// A slow startup read merges saved tabs without replacing choices made meanwhile.
state = init()[0];
send('LoadWorkspace');
const loading = await next('load_workspace');
send('OpenRepositoryTab', { root: 'C:/new', name: 'new' });
await finishHistory();
loading.resolve({ ...saved, activeRoot: 'C:/two' });
await tick();
assert.equal(tab('C:/new').id, state.activeRepositoryTab);
assert.deepEqual(tabs().map(tab => tab.root), ['C:/new', 'C:/one', 'C:/two']);
await finishHistory();
await flush();
assert.equal(disk.activeRoot, 'C:/new');
assert.equal(requests.length, 0);
console.log('PASS: workspace restore, lazy tabs, selection/diff, panel sizes, serialized saves, restart, failures, startup races and close drain.');
