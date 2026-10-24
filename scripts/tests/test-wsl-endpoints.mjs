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
        const id = args?.endpoint?.distribution ?? 'local';
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
const endpoint = distribution => ({ kind: 'wsl', distribution });
for (const distribution of [undefined, 'Ubuntu', 'Debian']) {
    send('OpenRepositoryTab', { root: '/home/user/repo', name: 'repo', endpoint: distribution ? endpoint(distribution) : undefined });
    await settle();
}
assert.equal(tabs().length, 3, 'Identical paths in different environments need independent tabs');
const ubuntu = tabs().find(tab => tab.endpoint?.distribution === 'Ubuntu');
send('OpenRepositoryTab', { root: ubuntu.root, name: 'repo', endpoint: endpoint('Ubuntu') });
assert.equal(tabs().length, 3, 'Opening the same endpoint/path must reuse its tab');
assert.equal(state.activeRepositoryTab, ubuntu.id);
calls.length = 0;
send('SelectWorkingTree', ubuntu.id);
await settle();
send('SelectWorkingTreeFile', ubuntu.id, new DiffTarget(0, []), 'file.txt');
await settle();
for (const name of ['git_open', 'git_diff_files', 'git_diff']) {
    assert.ok(calls.some(call => call.command === name), `Missing ${name}`);
}
assert.ok(calls.filter(call => call.command.startsWith('git_')).every(call => call.endpoint.distribution === 'Ubuntu'));
saved = JSON.parse(JSON.stringify(workspaceSnapshot(state)));
assert.equal(saved.version, 3);
assert.equal(saved.activeEndpoint.distribution, 'Ubuntu');
state = init()[0];
calls.length = 0;
send('LoadWorkspace');
await settle();
assert.equal(tabs().length, 3);
assert.equal(tabs().find(tab => tab.id === state.activeRepositoryTab).endpoint.distribution, 'Ubuntu');
assert.ok(calls.filter(call => call.command.startsWith('git_')).every(call => call.endpoint.distribution === 'Ubuntu'));
console.log('PASS: endpoint isolation, history/status/branches/files/diff routing, deduplication and workspace restart.');
