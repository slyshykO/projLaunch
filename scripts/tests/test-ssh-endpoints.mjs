// Compiled Elmish + isolated IPC: environment identity survives every Git request and restart.
import assert from 'node:assert/strict';
import { init, update, Msg, workspaceSnapshot } from '../../src/bin/fable-js/src/State.js';
import { DiffTarget } from '../../src/bin/fable-js/src/domain/GitProtocol.js';

const calls = [];
let saved;
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: {
    invoke(command, args) {
        if (command === 'get_storage_access') return Promise.resolve({ canWrite: true });
        if (command === 'load_workspace') return Promise.resolve(saved);
        if (command === 'save_workspace') return Promise.resolve();
        calls.push({ command, ...args });
        const id = JSON.stringify([args?.endpoint?.kind ?? 'local', args?.endpoint?.host, args?.endpoint?.username ?? '', args?.endpoint?.distribution]);
        if (command === 'git_open') return Promise.resolve({ repoId: id });
        if (args?.params?.repoId) assert.equal(args.params.repoId, id, 'Request crossed endpoints');
        if (command === 'git_log') return Promise.resolve({ commits: [] });
        if (command === 'git_status') return Promise.resolve({ entries: [{ path: 'file.txt', index: '.', worktree: 'M', kind: 'tracked' }] });
        if (command === 'git_branches') return Promise.resolve({ branches: [] });
        if (command === 'git_diff_files') return Promise.resolve([{ path: 'file.txt', status: 'M' }]);
        if (command === 'git_diff') return Promise.resolve({ text: '+changed', binary: false, truncated: false });
        return Promise.resolve();
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
const settle = async () => { for (let i = 0; i < 15; ++i) await new Promise(resolve => setImmediate(resolve)); };
const tabs = () => Array.from(state.repositoryTabs);
const endpoint = (host, username = '') => ({ kind: 'ssh', host, username });
for (const value of [undefined, { kind: 'wsl', distribution: 'Ubuntu' }, endpoint('192.0.2.1', 'alice'), endpoint('192.0.2.1', 'bob'), endpoint('work-alias')]) {
    send('OpenRepositoryTab', { root: '/home/user/repo', name: 'repo', endpoint: value });
    await settle();
}
assert.equal(tabs().length, 5, 'Host, user and transport must distinguish identical paths');
const selected = tabs().find(tab => tab.endpoint?.host === 'work-alias');
send('OpenRepositoryTab', { root: selected.root, name: 'repo', endpoint: { kind: 'ssh', host: 'work-alias' } });
assert.equal(tabs().length, 5, 'Absent and empty usernames both use the SSH config user');
assert.equal(state.activeRepositoryTab, selected.id);
calls.length = 0;
send('SelectWorkingTree', selected.id);
await settle();
send('SelectWorkingTreeFile', selected.id, new DiffTarget(0, []), 'file.txt');
await settle();
for (const name of ['git_open', 'git_diff_files', 'git_diff']) {
    assert.ok(calls.some(call => call.command === name), `Missing ${name}`);
}
assert.ok(calls.filter(call => call.command.startsWith('git_')).every(call => call.endpoint.host === 'work-alias' && !call.endpoint.username));
saved = JSON.parse(JSON.stringify(workspaceSnapshot(state)));
assert.equal(saved.version, 2);
assert.equal(saved.activeEndpoint.host, 'work-alias');
state = init()[0];
calls.length = 0;
send('LoadWorkspace');
await settle();
assert.equal(tabs().length, 5);
assert.equal(tabs().find(tab => tab.id === state.activeRepositoryTab).endpoint.host, 'work-alias');
assert.ok(calls.filter(call => call.command.startsWith('git_')).every(call => call.endpoint.host === 'work-alias' && !call.endpoint.username));
console.log('PASS: SSH host/user/alias isolation, history/status/branches/files/diff routing, deduplication and workspace restart.');
