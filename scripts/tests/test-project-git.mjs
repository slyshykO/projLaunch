// Run after compiling src with Fable. All IPC calls use in-memory fixtures.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const requests = [];
globalThis.isTauri = true;
globalThis.window = {
    location: { hash: '', href: 'http://localhost/' },
    __TAURI_INTERNALS__: {
        invoke: (command, args) => {
            assert.equal(command, 'git_project_status');
            assert.equal(args.id, 'fixture');
            return new Promise((resolve, reject) => requests.push({ resolve, reject }));
        },
    },
};
const { init, update, Msg } = await import('../../src/bin/fable-js/State.js');
const { ProjectGitIndicators } = await import('../../src/bin/fable-js/View.js');
const cases = new Msg(0, []).cases();
const step = (state, name, ...fields) => {
    const tag = cases.indexOf(name);
    assert.notEqual(tag, -1);
    return update(new Msg(tag, fields), state);
};
const execute = command => new Promise(resolve => {
    for (const start of command) start(resolve);
});
const checkFor = state => Array.from(state.projectGit).find(([id]) => id === 'fixture')?.[1];
const fixture = path => JSON.stringify({
    id: 'fixture', name: 'Fixture', path, lastOpened: '2026-10-19T12:00:00.000Z',
    description: '', ide: 'vscode', environment: {}, remote: null,
});
const summary = dirty => ({ repositories: [{ root: 'C:/repo', name: 'repo', branch: 'main', dirty, error: undefined }], warnings: [], unsupported: false });

let [state, command] = step(init()[0], 'OnGetProjectsSuccess', [fixture('C:/old')]);
assert.equal(cases[(await execute(command)).tag], 'RefreshGitStatuses');
[state, command] = step(state, 'RefreshProjectGit', 'fixture');
const firstRequest = checkFor(state).requestId;
assert.equal(checkFor(state).isLoading, true);
const oldCompletion = execute(command);
assert.equal(Array.from(step(state, 'RefreshProjectGit', 'fixture')[1]).length, 0, 'No overlapping refresh of the same path');

state = step(state, 'OnGetProjectsSuccess', [fixture('C:/new')])[0];
requests[0].resolve(summary(true));
const oldMessage = await oldCompletion;
assert.equal(update(oldMessage, state)[0], state, 'A changed path rejects an old response even before the new request starts');
[state, command] = step(state, 'RefreshProjectGit', 'fixture');
assert.notEqual(checkFor(state).requestId, firstRequest);
const newCompletion = execute(command);
requests[1].resolve(summary(false));
state = update(await newCompletion, state)[0];
assert.equal(checkFor(state).result.repositories[0].dirty, false);
assert.equal(checkFor(state).isLoading, false);
assert.equal(update(oldMessage, state)[0], state, 'An old result cannot overwrite a newer result');

[state, command] = step(state, 'RefreshProjectGit', 'fixture');
const failed = execute(command);
requests[2].reject('Git server unavailable');
state = update(await failed, state)[0];
assert.equal(checkFor(state).result, undefined, 'An error must not retain a clean result');
assert.match(checkFor(state).error, /Git server unavailable/);
assert.equal(checkFor(state).isLoading, false);

[state, command] = step(state, 'RefreshProjectGit', 'fixture');
const recovery = execute(command);
requests[3].resolve(summary(true));
state = update(await recovery, state)[0];
assert.equal(checkFor(state).result.repositories[0].dirty, true);
assert.equal(checkFor(state).error, undefined);
state = step(state, 'OnGetProjectsSuccess', [])[0];
assert.equal(Array.from(state.projectGit).length, 0);
assert.equal(update(oldMessage, state)[0], state);

const render = check => renderToStaticMarkup(React.createElement(ProjectGitIndicators, { check }));
const ready = result => ({ requestId: 'fixture', path: 'C:/repo', remote: undefined, isLoading: false, error: undefined, result });
const multiple = { ...summary(false), repositories: [...summary(false).repositories, { ...summary(true).repositories[0], root: 'C:/second', name: 'second' }] };
const html = render(ready(multiple));
assert.equal((html.match(/<svg/g) ?? []).length, 2);
assert.match(html, /Clean/);
assert.match(html, /Uncommitted changes/);
assert.match(html, /Branch: main/);
assert.match(render(ready({ ...summary(false), repositories: [] })), /No Git repository/);
assert.match(render(ready({ repositories: [], warnings: [], unsupported: true })), /Remote Git unavailable/);
assert.match(render({ ...ready(undefined), error: 'No Git', isLoading: false }), /Git unavailable/);
const unknown = render(ready({ ...summary(false), repositories: [{ ...summary(false).repositories[0], dirty: undefined, error: 'Access denied' }] }));
assert.match(unknown, /Status unavailable/);
assert.doesNotMatch(unknown, /Clean/);
assert.match(render({ ...ready(undefined), isLoading: true }), /Checking Git/);

console.log('PASS: Git refresh, deduplication of requests, stale replies, errors/recovery and card indicators.');
