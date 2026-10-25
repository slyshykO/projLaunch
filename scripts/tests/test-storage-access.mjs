// Compile Fable first; isolated IPC, no user database or native windows.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { init, update, Msg } from '../../src/bin/fable-js/src/State.js';
import { View } from '../../src/bin/fable-js/src/ui/View.js';

const calls = [];
let access = { canWrite: false, ownerPid: 12345 };
let accessError;
globalThis.isTauri = true;
globalThis.window = Object.assign(new EventTarget(), { __TAURI_INTERNALS__: {
    metadata: { currentWindow: { label: 'main' } },
    invoke(command, args) {
        if (command === 'git_branches') return Promise.resolve({ branches: [], head: null, currentBranch: null });
        calls.push(command);
        if (command === 'get_storage_access') return accessError ? Promise.reject(accessError) : Promise.resolve(access);
        if (command === 'load_workspace') return Promise.resolve({ version: 1, tabs: [], activeRoot: undefined });
        if (command === 'git_project_status') return Promise.resolve({ repositories: [], warnings: [] });
        if (command === 'git_open') return Promise.resolve({ repoId: 'fresh-session' });
        if (command === 'git_log') return Promise.resolve({ commits: [] });
        if (command === 'git_status') return Promise.resolve({ entries: [] });
        if (['save_workspace', 'rewrite_project_file', 'delete_project', 'update_project_last_opened'].includes(command)) {
            assert.equal(access.canWrite, true, 'Read-only window must never attempt database writes');
        }
        return Promise.resolve();
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
const project = { id: 'one', name: 'Example', path: 'C:/example', ide: 'vscode', description: '', lastOpened: '2026-10-18T12:34:56Z', remote: null, environment: {} };

send('LoadWorkspace');
await tick();
assert.deepEqual(calls.slice(0, 2), ['get_storage_access', 'load_workspace']);
assert.equal(state.workspaceReady, true);
assert.equal(state.storageAccess.canWrite, false);
send('OnGetProjectsSuccess', [JSON.stringify(project)]);
await tick();
const savedProject = Array.from(state.projects)[0];
send('OpenAddProjectModal');
assert.equal(state.projectEditor, undefined);
send('OpenEditProjectModal', 'one');
assert.equal(state.projectEditor, undefined);
send('SaveProject', savedProject);
send('OpenDeleteProjectModal', 'one');
assert.equal(state.projectDeletion, undefined);
send('DeleteProject');
send('UpdateProjectLastOpened', 'one', new Date());
send('OpenProject', 'one');
await tick();
assert.ok(calls.includes('open_project'), 'IDE launching stays available');
assert.equal(Array.from(state.projects)[0].lastOpened.getTime(), savedProject.lastOpened.getTime());
assert.ok(!calls.includes('rewrite_project_file'), 'Opening does not update lastOpened in a reader');
assert.ok(!calls.includes('update_project_last_opened'));
assert.ok(!calls.includes('delete_project'));
const html = renderToStaticMarkup(React.createElement(View, { state, dispatch }));
assert.match(html, /Read-only database/);
assert.match(html, /PID 12345/);
assert.match(html, /disabled="" aria-label="Edit project Example"/);
assert.match(html, /disabled="" aria-label="Remove project Example"/);

// Tabs/layout stay usable in memory, and closing never waits for impossible saves.
send('OpenRepositoryTab', { root: 'C:/example', name: 'Example' });
await tick();
send('ResizeRepositoryPanels', state.activeRepositoryTab, true, 55);
assert.equal(Array.from(state.repositoryTabs)[0].filesWidth, 55);
send('SaveWorkspace');
assert.ok(!calls.includes('save_workspace'));
send('WindowSave');
await tick();
assert.ok(calls.includes('plugin:window|close'));
assert.equal(state.workspaceError, undefined);

// A new launch can become writer, but a status-read error never overwrites the session.
state = init()[0];
calls.length = 0;
accessError = 'Database unavailable';
send('LoadWorkspace');
await tick();
assert.equal(state.workspaceReady, false);
assert.match(state.workspaceError, /Database unavailable/);
assert.ok(!calls.includes('load_workspace'));
send('SaveWorkspace');
assert.ok(!calls.includes('save_workspace'));
accessError = undefined;
access = { canWrite: true, ownerPid: 54321 };
send('LoadWorkspace');
await tick();
assert.equal(state.storageAccess.canWrite, true);
send('OpenAddProjectModal');
assert.ok(state.projectEditor);
send('OpenRepositoryTab', { root: 'C:/example', name: 'Example' });
await tick();
assert.ok(calls.includes('save_workspace'));
console.log('PASS: read-only startup, disabled edits, IDE launch without writes, transient tabs/layout, clean close, error/retry and writer restart.');
