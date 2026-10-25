// Compile Fable first. In-memory IPC only; never touches the user's catalog or files.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const requests = [];
globalThis.isTauri = true;
globalThis.window = {
    location: { hash: '', href: 'http://localhost/' },
    __TAURI_INTERNALS__: {
        invoke(command, args) {
            assert.ok(['delete_project', 'update_project_last_opened'].includes(command), command);
            return new Promise((resolve, reject) => requests.push({ command, args, resolve, reject }));
        },
    },
};
const { init, update, Msg, watchedRepositories } = await import('../../src/bin/fable-js/src/State.js');
const { Dialog } = await import('../../src/bin/fable-js/src/ui/ProjectDeletion.js');
const { Project } = await import('../../src/bin/fable-js/src/ui/View.js');
const cases = new Msg(0, []).cases();
const step = (state, name, ...fields) => {
    assert.ok(cases.includes(name), name);
    return update(new Msg(cases.indexOf(name), fields), state);
};
const run = command => new Promise(resolve => { for (const start of command) start(resolve); });
const none = command => assert.equal(Array.from(command).length, 0);
const ids = state => Array.from(state.projects, project => project.id);
const fixture = id => JSON.stringify({ id, name: `Project ${id}`, path: 'C:/shared', ide: 'vscode', description: '', lastOpened: '2026-10-25T10:00:00Z', environment: {}, remote: null });
let state = step(init()[0], 'OnGetProjectsSuccess', [fixture('one'), fixture('two')])[0];
const project = Array.from(state.projects)[0];
const card = renderToStaticMarkup(React.createElement(Project, { prj: project, gitCheck: undefined, readOnly: false, dispatch() {} }));
assert.match(card, /aria-label="Remove project Project one"/);

none(step(state, 'DeleteProject')[1]);
assert.equal(step(state, 'OpenDeleteProjectModal', 'missing')[0], state);
state = step(state, 'OpenDeleteProjectModal', 'one')[0];
assert.equal(state.projectDeletion.id, 'one');
const html = renderToStaticMarkup(React.createElement(Dialog, { project, isDeleting: false, error: undefined, dispatch() {} }));
assert.match(html, /Files and Git repositories stay on disk/);
assert.match(html, /autofocus=""[^>]*>Cancel/);
assert.equal(step(state, 'OpenAddProjectModal')[0], state);
assert.equal(step(state, 'OpenEditProjectModal', 'two')[0], state);
assert.equal(step(state, 'OpenAbout')[0], state);
state = step(state, 'CancelProjectDeletion')[0];
assert.equal(state.projectDeletion, undefined);
assert.deepEqual(ids(state), ['one', 'two']);
assert.equal(requests.length, 0);

// Both cards and the tab share a root. A deleted card must not take it away from the others.
const repo = { root: 'C:/shared', name: 'shared', dirty: false };
for (const id of ['one', 'two']) {
    state = step(state, 'RefreshProjectGit', id)[0];
    const check = Array.from(state.projectGit).find(([key]) => key === id)[1];
    const result = { tag: 0, fields: [{ repositories: [repo], warnings: [], unsupported: false }] };
    state = step(state, 'OnProjectGitStatus', id, check.requestId, result)[0];
}
state = step(state, 'OpenRepositoryTab', repo)[0];
const activeTab = state.activeRepositoryTab;
state = step(state, 'GitChanged', undefined, 'C:/shared', false, true)[0];
const tabs = state.repositoryTabs;
state = step(state, 'OpenDeleteProjectModal', 'one')[0];
let command;
[state, command] = step(state, 'DeleteProject');
const failed = run(command);
assert.equal(state.isProjectDeleting, true);
assert.deepEqual(requests[0].args, { id: 'one' });
none(step(state, 'DeleteProject')[1]);
assert.equal(step(state, 'CancelProjectDeletion')[0], state);
assert.equal(step(state, 'OnOpenProjectSuccess', 'one')[0], state);
none(step(state, 'UpdateProjectLastOpened', 'one', new Date())[1]);
let closing = step(state, 'WindowSave')[0];
none(step(closing, 'ContinueWindowClose')[1]);
assert.equal(cases[(await run(step(closing, 'OnWindowSaveSuccess')[1])).tag], 'ContinueWindowClose');
requests[0].reject('Database is locked');
state = update(await failed, closing)[0];
assert.equal(state.isWindowClosePending, false);
assert.equal(state.isProjectDeleting, false);
assert.match(state.projectDeleteError, /Database is locked/);
assert.deepEqual(ids(state), ['one', 'two']);
assert.equal(state.projectDeletion.id, 'one');
assert.equal(Array.from(watchedRepositories(state)).length, 1);

[state, command] = step(state, 'DeleteProject');
const success = run(command);
requests[1].resolve();
state = update(await success, state)[0];
assert.deepEqual(ids(state), ['two']);
assert.equal(state.projectDeletion, undefined);
assert.equal(state.projectDeleteError, undefined);
assert.ok(!Array.from(state.projectGit).some(([id]) => id === 'one'));
assert.ok(!Array.from(state.pendingProjectGit).includes('one'));
assert.equal(state.repositoryTabs, tabs);
assert.equal(state.activeRepositoryTab, activeTab);
assert.equal(Array.from(watchedRepositories(state)).length, 1);
assert.equal(step(state, 'OnOpenProjectSuccess', 'one')[0], state);
none(step(state, 'UpdateProjectLastOpened', 'one', new Date())[1]);
none(step(state, 'OnUpdateProjectLastOpenedSuccess', 'one')[1]);
state = step(state, 'OnGetProjectsSuccess', [fixture('one'), fixture('two')])[0];
assert.deepEqual(ids(state), ['two'], 'A stale catalog load cannot restore a deleted card');

// Ordinary opens update just the timestamp, never upsert a complete project record.
[state, command] = step(state, 'OnOpenProjectSuccess', 'two');
const opened = await run(command);
[state, command] = update(opened, state);
const touched = run(command);
assert.equal(requests[2].command, 'update_project_last_opened');
assert.equal(requests[2].args.id, 'two');
assert.equal(Object.keys(requests[2].args).length, 2);
assert.ok(Number.isFinite(Date.parse(requests[2].args.lastOpened)));
requests[2].resolve();
state = update(await touched, state)[0];

state = step(state, 'CloseRepositoryTab', activeTab)[0];
state = step(state, 'OpenDeleteProjectModal', 'two')[0];
[state, command] = step(state, 'DeleteProject');
const lastDeletion = run(command);
requests[3].resolve();
state = update(await lastDeletion, state)[0];
assert.deepEqual(ids(state), []);
assert.equal(Array.from(watchedRepositories(state)).length, 0, 'The final consumer releases its subscription');
console.log('PASS: project deletion, confirmation, errors/retry, stale responses, late launches, shared tabs/watchers and close coordination.');
