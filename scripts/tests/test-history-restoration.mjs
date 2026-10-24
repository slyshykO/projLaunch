// Restart with a fresh Elmish model and an independent, controlled Git session.
import assert from 'node:assert/strict';
import { init, update, Msg, workspaceSnapshot } from '../../src/bin/fable-js/src/State.js';

const requests = [];
const writes = [];
const native = [];
globalThis.isTauri = true;
globalThis.window = { __TAURI_INTERNALS__: {
    metadata: { currentWindow: { label: 'main' } },
    invoke(command, args) {
        if (command === 'get_storage_access') return Promise.resolve({ canWrite: true });
        if (command === 'git_status') return Promise.resolve({ entries: [] });
        if (command === 'git_branches') return Promise.resolve({ branches: [] });
        if (command === 'save_workspace') { writes.push(JSON.parse(JSON.stringify(args.workspace))); return Promise.resolve(); }
        if (command.startsWith('plugin:') || command === 'frontend_log') { native.push(command); return Promise.resolve(); }
        return new Promise((resolve, reject) => requests.push({ command, args, resolve, reject }));
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
const tick = () => new Promise(resolve => setImmediate(resolve));
const next = async command => {
    await tick();
    const request = requests.shift();
    assert.equal(request?.command, command);
    return request;
};
const tab = root => Array.from(state.repositoryTabs).find(tab => tab.root === root);
const oid = number => number.toString(16).padStart(40, '0');
const page = (start, count = 100) => Array.from({ length: count }, (_, index) => ({
    oid: oid(start + index), parents: [], subject: `Commit ${start + index}`, refs: [],
    authorName: 'Author', authorEmail: 'a@example.test', authorDate: '2026-10-25T10:00:00Z', commitDate: '2026-10-25T10:00:00Z',
}));
const position = { top: 5300, left: 45, anchor: oid(173), offset: 7 };
const saved = {
    version: 3, activeRoot: 'C:/one', tabs: ['one', 'two'].map(name => ({
        root: `C:/${name}`, name, filesWidth: 40, filesHeight: 40,
        history: { count: name === 'one' ? 300 : 200, scroll: position },
    })),
};
const restart = async (snapshot = saved) => {
    assert.equal(requests.length, 0);
    state = init()[0];
    send('LoadWorkspace');
    (await next('load_workspace')).resolve(snapshot);
    await tick();
};
const firstPage = async (commits = page(1), cursor = 'fresh-100') => {
    (await next('git_open')).resolve({ repoId: 'fresh-session' });
    const request = await next('git_log');
    assert.equal(request.args.params.cursor, undefined);
    request.resolve({ commits, nextCursor: cursor });
    await tick();
};

await restart();
assert.equal(tab('C:/two').history.hasLoaded, false);
await firstPage();
assert.equal(tab('C:/one').historyView.count, 300, 'Partial restore must not overwrite the desired depth');
const second = await next('git_log');
assert.equal(second.args.params.repoId, 'fresh-session');
assert.equal(second.args.params.cursor, 'fresh-100');
send('ShowProjects');
second.resolve({ commits: page(101), nextCursor: 'fresh-200' });
await tick();
assert.equal(requests.length, 0, 'Switching away pauses automatic paging after the current read');
assert.equal(tab('C:/one').restoringHistory, true);
assert.equal(workspaceSnapshot(state).tabs[0].history.count, 300);
send('SelectRepositoryTab', tab('C:/one').id);
const third = await next('git_log');
assert.equal(third.args.params.cursor, 'fresh-200');
third.resolve({ commits: page(201), nextCursor: 'fresh-300' });
await tick();
assert.equal(tab('C:/one').history.commits.length, 300);
assert.equal(tab('C:/one').restoringHistory, false);
assert.equal(requests.length, 0, 'Restoration stops at the saved depth even with more history available');
const token = tab('C:/one').scrollRestoreId;
send('HistoryScrollRestored', tab('C:/one').id, 'stale-token', { ...position, top: 0 });
assert.equal(tab('C:/one').scrollRestoreId, token);
send('HistoryScrollRestored', tab('C:/one').id, token, position);
assert.equal(tab('C:/one').scrollRestoreId, undefined);
send('HistoryScrolled', tab('C:/one').id, { ...position, top: 5600 });
send('WindowSave');
await tick(); await tick();
assert.equal(writes.at(-1).tabs[0].history.scroll.top, 5600, 'Close flushes the latest scroll before its save debounce fires');
assert.ok(native.includes('plugin:window|close'));
assert.equal(writes.at(-1).tabs[1].history.count, 200, 'Unopened background tab keeps its saved depth');
assert.ok(!JSON.stringify(writes.at(-1)).includes('fresh-session'));
assert.ok(!JSON.stringify(writes.at(-1)).includes('fresh-300'));

// A failed page retains the desired depth and position. Refresh uses a fresh cursor.
await restart();
await firstPage();
(await next('git_log')).reject('Expired cursor');
await tick();
assert.equal(tab('C:/one').history.commits.length, 100);
assert.equal(tab('C:/one').historyView.count, 300);
assert.deepEqual(workspaceSnapshot(state).tabs[0].history.scroll, position);
assert.equal(requests.length, 0);
send('RefreshRepositoryHistory', tab('C:/one').id);
await firstPage(page(1, 25), null);
assert.equal(tab('C:/one').restoringHistory, false, 'A shortened history stops at EOF');
assert.equal(tab('C:/one').historyView.count, 25);

// Cancellation stops subsequent pages without accepting a late scroll restore.
await restart();
await firstPage();
const running = await next('git_log');
const cancelledToken = tab('C:/one').scrollRestoreId;
send('CancelHistoryRestore', tab('C:/one').id);
running.resolve({ commits: page(101), nextCursor: 'unused' });
await tick();
send('HistoryScrollRestored', tab('C:/one').id, cancelledToken, position);
assert.equal(requests.length, 0);
assert.equal(tab('C:/one').historyView.count, 200);
assert.equal(tab('C:/one').historyView.scroll.top, 0);

// Closing a restoring tab invalidates both Git and scroll callbacks.
await restart();
await firstPage();
const late = await next('git_log');
const closedId = tab('C:/one').id;
send('CloseRepositoryTab', closedId);
late.resolve({ commits: page(101), nextCursor: 'unused' });
await tick();
assert.equal(tab('C:/one'), undefined);
assert.equal(requests.length, 0);

// No-progress pages cannot spin forever, and user navigation wins over restoration.
await restart();
await firstPage();
(await next('git_log')).resolve({ commits: page(1), nextCursor: 'same-page' });
await tick();
assert.match(tab('C:/one').history.error, /no progress/);
assert.equal(requests.length, 0);
send('HistoryScrolled', tab('C:/one').id, { ...position, top: 12 });
assert.equal(tab('C:/one').restoringHistory, false);
assert.equal(tab('C:/one').scrollRestoreId, undefined);

// Branch navigation calls the internal update path and must also supersede restore.
await restart();
await firstPage();
const navigatingPage = await next('git_log');
tab('C:/one').branches = { branches: [{ refName: 'refs/heads/missing', oid: oid(900) }] };
send('NavigateToBranch', tab('C:/one').id, 'refs/heads/missing');
assert.equal(tab('C:/one').restoringHistory, false);
assert.equal(tab('C:/one').scrollRestoreId, undefined);
send('CancelCommitNavigation', tab('C:/one').id);
navigatingPage.resolve({ commits: page(101), nextCursor: 'no-more-automatic-reads' });
await tick();
assert.equal(requests.length, 0);
console.log('PASS: history depth, fresh sessions, lazy/paused tabs, retry, shortened history, cancellation, stale replies and close-time scroll save.');
