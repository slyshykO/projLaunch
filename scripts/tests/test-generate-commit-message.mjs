import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg } from '../../src/bin/fable-js/src/State.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';
import { DiffTarget } from '../../src/bin/fable-js/src/domain/GitProtocol.js';

const cases = new Msg(0, []).cases();
let state = init()[0];
const calls = [];
const pending = [];
let delayOpen = false;
const delayedOpens = [];
const file = { path: 'change.txt', status: 'M' };
let staged = true;
const projects = [
    { id: 'codex-project', name: 'Codex project', commitAgent: 'codex', remote: null },
    { id: 'claude-project', name: 'Claude project', commitAgent: 'claude', remote: null },
    { id: 'wsl', name: 'WSL project', commitAgent: 'codex', remote: { Wsl: 'Ubuntu' } },
    { id: 'ssh', name: 'SSH project', commitAgent: 'claude', remote: { Ssh: { host: 'test', username: '' } } },
].map(p => ({ ...p, path: '/fixture', description: '', ide: 'vscode', environment: {}, lastOpened: '2026-11-18T12:00:00Z' }));
const endpoint = id => id === 'wsl' ? { kind: 'wsl', distribution: 'Ubuntu' } : id === 'ssh' ? { kind: 'ssh', host: 'test', username: '' } : undefined;
const repo = id => ({ root: '/fixture', name: 'fixture', endpoint: endpoint(id) });
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: { invoke(command, args) {
    calls.push({ command, ...args });
    if (command === 'git_project_status') return Promise.resolve({ repositories: [repo(args.id)], warnings: [], unsupported: false });
    if (command === 'git_open') return delayOpen ? new Promise(resolve => delayedOpens.push(resolve)) : Promise.resolve({ repoId: 'fresh-session' });
    if (command === 'git_log') return Promise.resolve({ commits: [], nextCursor: undefined });
    if (command === 'git_status') return Promise.resolve({ head: 'a'.repeat(40), branch: 'main', entries: [{ path: file.path, kind: 'tracked', index: staged ? 'M' : '.', worktree: 'M' }] });
    if (command === 'git_branches') return Promise.resolve({ branches: [] });
    if (command === 'git_diff_files') return Promise.resolve(args.params.target.kind === (staged ? 'staged' : 'unstaged') ? [file] : []);
    if (command === 'git_generate_commit_message') return new Promise((resolve, reject) => pending.push({ ...args, resolve, reject }));
    if (command === 'git_cancel') return Promise.resolve(true);
    throw new Error(`Unexpected IPC ${command}`);
} } };
const dispatch = message => {
    const [next, commands] = update(message, state);
    state = next;
    for (const run of commands) run(dispatch);
};
const send = (name, ...fields) => dispatch(new Msg(cases.indexOf(name), fields));
const settle = async () => { for (let i = 0; i < 16; i++) await new Promise(resolve => setImmediate(resolve)); };
const tab = () => Array.from(state.repositoryTabs).find(t => t.id === state.activeRepositoryTab);
const render = () => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab: tab(), dispatch }));
const open = async projectId => {
    send('OpenProjectRepositoryTab', projectId, repo(projectId));
    await settle();
    send('SelectWorkingTree', tab().id);
    await settle();
    return tab().id;
};
send('OnGetProjectsSuccess', projects.map(p => JSON.stringify(p)));
await settle();
let id = await open('codex-project');
assert.match(render(), /Generate with Codex/);
assert.match(render(), /Project settings for commit message/);
assert.match(render(), /Generate from staged changes only/);
send('EditCommitMessage', id, 'Previous draft');
send('GenerateCommitMessage', id);
await settle();
assert.equal(pending[0].params.agent, 'codex');
assert.equal(pending[0].params.repoId, 'fresh-session');
assert.equal(pending[0].endpoint, undefined);
send('GenerateCommitMessage', id);
send('CreateCommit', id);
send('ChangeIndex', id, new DiffTarget(1, []), [file.path]);
await settle();
assert.equal(pending.length, 1, 'Generation excludes duplicate requests and writes');
assert.match(render(), /Cancel generation/);
pending.shift().resolve({ message: 'Generated subject\n\nBody', scope: 'staged' });
await settle();
assert.equal(tab().commitDraft.message, 'Generated subject\n\nBody');
assert.match(render(), /Message generated from staged changes/);
assert.equal(calls.filter(c => c.command === 'git_commit').length, 0, 'Generation only fills the editor');

// Project setting selection, fallback scope and endpoint identity.
send('SelectCommitProject', id, 'claude-project');
staged = false;
send('RefreshRepositoryHistory', id);
await settle();
assert.match(render(), /Generate with Claude/);
assert.match(render(), /Generate from all working changes/);
send('GenerateCommitMessage', id);
await settle();
assert.equal(pending[0].params.agent, 'claude');
pending.shift().resolve({ message: 'All changes', scope: 'all' });
await settle();
assert.equal(tab().commitDraft.message, 'All changes');
assert.match(render(), /Review the files you stage/);
send('CreateCommit', id);
assert.equal(calls.filter(c => c.command === 'git_commit').length, 0, 'No automatic staging');

// Typing, watcher invalidations and settings changes make an old result inapplicable.
for (const reason of ['typing', 'watcher', 'settings']) {
    send('GenerateCommitMessage', id);
    await settle();
    const request = pending.shift();
    if (reason === 'typing') send('EditCommitMessage', id, 'Typed while generating');
    if (reason === 'watcher') send('GitChanged', undefined, '/fixture', true, true);
    if (reason === 'settings') {
        projects[1].commitAgent = 'codex';
        send('OnGetProjectsSuccess', projects.map(p => JSON.stringify(p)));
    }
    await settle();
    const original = tab().commitDraft.message;
    request.resolve({ message: 'Stale generated message', scope: 'all' });
    await settle();
    assert.equal(tab().commitDraft.message, original);
    assert.match(render(), /changed during generation/);
}
send('GenerateCommitMessage', id);
await settle();
pending.shift().reject(new Error('Agent not installed on endpoint'));
await settle();
assert.match(render(), /Agent not installed on endpoint/);
assert.equal(tab().commitDraft.message, 'Typed while generating');
assert.equal(pending.length, 0);
send('GenerateCommitMessage', id);
await settle();
const cancelled = pending.shift();
send('CancelCommitGeneration', id);
await settle();
assert.equal(calls.at(-1).command, 'git_cancel');
assert.equal(calls.at(-1).params.operationId, cancelled.params.operationId);
cancelled.resolve({ message: 'Cancelled', scope: 'all' });
await settle();
assert.notEqual(tab().commitDraft.message, 'Cancelled');
send('GenerateCommitMessage', id);
await settle();
const closed = pending.shift();
send('CloseRepositoryTab', id);
id = await open('claude-project');
closed.resolve({ message: 'Closed tab result', scope: 'all' });
await settle();
assert.notEqual(tab().commitDraft.message, 'Closed tab result');
delayOpen = true;
send('GenerateCommitMessage', id);
await settle();
assert.equal(delayedOpens.length, 1);
send('CancelCommitGeneration', id);
delayedOpens.shift()({ repoId: 'late-session' });
await settle();
assert.equal(pending.length, 0, 'Cancelling during repo.open never starts an agent');
delayOpen = false;
for (const projectId of ['wsl', 'ssh']) {
    const remoteId = await open(projectId);
    send('GenerateCommitMessage', remoteId);
    await settle();
    const remote = pending.shift();
    assert.deepEqual(remote.endpoint, endpoint(projectId));
    assert.equal(remote.params.agent, projects.find(p => p.id === projectId).commitAgent);
    remote.resolve({ message: 'Remote message', scope: 'all' });
    await settle();
    assert.equal(tab().commitDraft.message, 'Remote message');
}
console.log('AI commit messages: project settings, endpoints, editable results, cancellation and stale-response guards passed.');
