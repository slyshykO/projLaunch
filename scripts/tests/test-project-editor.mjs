// Run after: dotnet fable src -o ./src/bin/fable-js --configuration Debug
// Uses an isolated in-memory IPC stub; never reads or writes the user's database.
import assert from 'node:assert/strict';

let saved;
let saveFailure;
globalThis.isTauri = true;
globalThis.window = {
    location: { hash: '', href: 'http://localhost/' },
    __TAURI_INTERNALS__: {
        invoke: async (command, args) => {
            assert.equal(command, 'rewrite_project_file');
            if (saveFailure) throw saveFailure;
            saved = { id: args.id, project: JSON.parse(args.content) };
        },
    },
};

const { init, update, Msg } = await import('../../src/bin/fable-js/State.js');
const projectModule = await import('../../src/bin/fable-js/ProjectData.js');
const decode = Object.entries(projectModule).find(([name]) => name.startsWith('ProjectData_fromJson_'))[1];
const cases = new Msg(0, []).cases();
const msg = (name, ...fields) => {
    const tag = cases.indexOf(name);
    assert.notEqual(tag, -1, `Unknown message: ${name}`);
    return new Msg(tag, fields);
};
const step = (state, name, ...fields) => update(msg(name, ...fields), state);
const runCommand = command => new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Command did not complete')), 2000);
    for (const start of command) start(message => {
        clearTimeout(timer);
        resolve(message);
    });
});

const fixture = remote => ({
    id: 'existing-project', name: 'Original', description: 'Description',
    path: 'C:\\Projects\\Original', ide: 'vscode',
    lastOpened: '2026-10-18T12:34:56.000Z', remote,
    environment: { EXTRA: 'значення=1', EMPTY: '', MULTILINE: 'one\ntwo' },
});
const baseState = init()[0];
const initialState = project => step(baseState, 'OnGetProjectsSuccess', [JSON.stringify(project)])[0];

for (const remote of [null, { Ssh: { host: 'example.test', username: '' } }, { Wsl: 'Ubuntu' }]) {
    const original = fixture(remote);
    let state = initialState(original);
    state = step(state, 'OpenEditProjectModal', original.id)[0];
    assert.equal(state.projectEditor.id, original.id);
    assert.equal(step(state, 'OnAddOrUpdateProjectSuccess', original.id)[0].projectEditor, state.projectEditor,
        'Background last-opened save must not close an editor');

    const cancelled = step(state, 'CloseProjectEditor');
    assert.equal(cancelled[0].projectEditor, undefined);
    assert.equal(Array.from(cancelled[1]).length, 0);
    assert.equal(Array.from(cancelled[0].projects)[0].name, 'Original');

    // Simulate a last-opened refresh while the form is open.
    const latest = { ...original, lastOpened: '2026-10-19T15:00:00.000Z' };
    state = step(state, 'OnGetProjectsSuccess', [JSON.stringify(latest)])[0];
    const draft = decode(JSON.stringify({ ...original, name: ' Renamed ', path: 'C:\\Projects\\New' }));
    const [saving, command] = step(state, 'SaveProject', draft);
    assert.equal(saving.isProjectSaving, true);
    assert.equal(step(saving, 'CloseProjectEditor')[0].projectEditor, state.projectEditor);
    assert.equal(Array.from(step(saving, 'SaveProject', draft)[1]).length, 0, 'No duplicate saves');
    saved = undefined;
    const completed = await runCommand(command);
    state = update(completed, saving)[0];
    assert.equal(saved.id, original.id);
    assert.equal(saved.project.id, original.id);
    assert.equal(saved.project.name, 'Renamed');
    assert.equal(saved.project.lastOpened, latest.lastOpened);
    assert.deepEqual(saved.project.remote, remote);
    assert.deepEqual(saved.project.environment, original.environment);
    assert.equal(Array.from(state.projects).length, 1, 'Editing must not create a second card');
    assert.equal(Array.from(state.projects)[0].name, 'Renamed');
    assert.equal(state.projectEditor, undefined);
    assert.equal(state.isProjectSaving, false);
}

let state = initialState(fixture(null));
state = step(state, 'OpenEditProjectModal', 'existing-project')[0];
let draft = decode(JSON.stringify({ ...fixture(null), name: 'Retry me' }));
saveFailure = new Error('Database is locked');
let [saving, command] = step(state, 'SaveProject', draft);
state = update(await runCommand(command), saving)[0];
assert.match(state.projectSaveError, /Database is locked/);
assert.equal(state.projectEditor.id, 'existing-project');
assert.equal(state.isProjectSaving, false);
assert.equal(Array.from(state.projects)[0].name, 'Original');
saveFailure = undefined;
[saving, command] = step(state, 'SaveProject', draft);
state = update(await runCommand(command), saving)[0];
assert.equal(Array.from(state.projects)[0].name, 'Retry me');

state = step(state, 'OpenEditProjectModal', 'existing-project')[0];
saveFailure = 'SQLite is unavailable';
[saving, command] = step(state, 'SaveProject', draft);
state = update(await runCommand(command), saving)[0];
assert.match(state.projectSaveError, /SQLite is unavailable/, 'Plain-string IPC errors must be visible');
saveFailure = undefined;
state = step(state, 'CloseProjectEditor')[0];

state = step(state, 'OpenAddProjectModal')[0];
assert.notEqual(state.projectEditor.id, 'existing-project');
assert.equal(state.projectEditor.name, '');
assert.equal(state.projectEditor.remote, undefined);
assert.equal(Array.from(state.projectEditor.environment).length, 0);
const invalid = step(state, 'SaveProject', state.projectEditor);
assert.equal(Array.from(invalid[1]).length, 0, 'Empty name/path must not reach SQLite');
draft = { ...state.projectEditor, name: 'New', path: 'C:\\Projects\\New' };
[saving, command] = step(state, 'SaveProject', draft);
state = update(await runCommand(command), saving)[0];
assert.equal(Array.from(state.projects).length, 2);
assert.equal(state.projectEditor, undefined);

console.log('PASS: project edits, identity, metadata, SSH/WSL, environment, cancel, errors, retry and add.');
