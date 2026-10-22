// Compile Fable first. Controlled IPC replies exercise the real Elmish state.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const requests = [];
globalThis.isTauri = true;
globalThis.window = {
    __TAURI_INTERNALS__: {
        invoke(command, args) {
        if (command === 'git_branches') return Promise.resolve({ branches: [], head: null, currentBranch: null });
            if (command === "git_status") return Promise.resolve({ entries: [] });
            assert.ok(['git_open', 'git_log'].includes(command));
            return new Promise((resolve, reject) => requests.push({ command, params: args.params, resolve, reject }));
        },
    },
};
const { init, update, Msg } = await import('../../src/bin/fable-js/src/State.js');
const { RepositoryPanel } = await import('../../src/bin/fable-js/src/ui/DesktopTabs.js');
const cases = new Msg(0, []).cases();
const step = (state, name, ...fields) => update(new Msg(cases.indexOf(name), fields), state);
const execute = commands => new Promise(resolve => {
    assert.ok(Array.from(commands).length > 0);
    for (const run of commands) run(resolve);
});
const nextRequest = async command => {
    await new Promise(resolve => setImmediate(resolve));
    const request = requests.shift();
    assert.equal(request?.command, command);
    return request;
};
const tab = (state, id = state.activeRepositoryTab) => Array.from(state.repositoryTabs).find(tab => tab.id === id);
const repo = root => ({ root, name: 'repo', dirty: false, branch: 'main' });
const commit = (oid, subject = `Commit ${oid}`) => ({
    oid, subject, parents: [], authorName: 'Олексій', authorEmail: 'author@example.test',
    authorDate: '2026-10-20T10:00:00Z', commitDate: '2026-10-20T10:00:00Z', refs: ['main', 'tag: v1'],
});
const render = tab => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab, dispatch() {} }));
let [state, command] = step(init()[0], 'OpenRepositoryTab', repo('C:/one'));
const firstId = state.activeRepositoryTab;
[state, command] = update(await execute(command), state);
assert.ok(tab(state).history.requestId);
assert.match(render(tab(state)), /Loading commit history/);
const initial = execute(command);
const opened = await nextRequest('git_open');
assert.equal(opened.params.path, 'C:/one');
opened.resolve({ repoId: 'session-one', root: 'C:/one' });
const log = await nextRequest('git_log');
assert.equal(log.params.repoId, 'session-one');
assert.equal(log.params.limit, 100);
assert.equal(log.params.cursor, undefined);
assert.notEqual(log.params.operationId, opened.params.operationId);
log.resolve({ commits: [commit('a'.repeat(40), '<script>not HTML</script>')], nextCursor: 'page-two' });
state = update(await initial, state)[0];
assert.equal(tab(state).history.commits.length, 1);
assert.equal(Array.from(step(state, 'OpenRepositoryTab', repo('C:/one'))[1]).length, 0);
const html = render(tab(state));
assert.match(html, /Олексій|&#/);
assert.match(html, /aaaaaaaa/);
assert.match(html, /tag: v1/);
assert.match(html, /&lt;script&gt;not HTML&lt;\/script&gt;/);
assert.doesNotMatch(html, /<script>/);
assert.match(html, /Load more/);

[state, command] = step(state, 'LoadMoreCommits', firstId);
const pageTwo = execute(command);
assert.equal(Array.from(step(state, 'LoadMoreCommits', firstId)[1]).length, 0, 'One page request per tab at a time');
assert.equal(Array.from(step(state, 'RefreshRepositoryHistory', firstId)[1]).length, 0);
const next = await nextRequest('git_log');
assert.equal(next.params.cursor, 'page-two');
assert.equal(next.params.repoId, 'session-one');
next.resolve({ commits: [commit('a'.repeat(40)), commit('b'.repeat(40))], nextCursor: 'page-three' });
state = update(await pageTwo, state)[0];
assert.deepEqual(tab(state).history.commits.map(commit => commit.oid), ['a'.repeat(40), 'b'.repeat(40)]);

[state, command] = step(state, 'LoadMoreCommits', firstId);
const failure = execute(command);
(await nextRequest('git_log')).reject('Cursor expired or session lost');
state = update(await failure, state)[0];
assert.equal(tab(state).history.commits.length, 2, 'Preserve the last successful page on failure');
assert.equal(tab(state).history.repoId, undefined);
assert.equal(tab(state).history.nextCursor, undefined);
assert.match(render(tab(state)), /Previously loaded commits/);
assert.doesNotMatch(render(tab(state)), /Load more/);
[state, command] = step(state, 'RefreshRepositoryHistory', firstId);
const recovery = execute(command);
(await nextRequest('git_open')).resolve({ repoId: 'session-two', root: 'C:/one' });
const refreshed = await nextRequest('git_log');
assert.equal(refreshed.params.repoId, 'session-two');
assert.equal(refreshed.params.cursor, undefined, 'Never reuse an old snapshot after reconnect');
refreshed.resolve({ commits: [commit('c'.repeat(40))], nextCursor: undefined });
state = update(await recovery, state)[0];
assert.deepEqual(tab(state).history.commits.map(commit => commit.oid), ['c'.repeat(40)]);
assert.equal(tab(state).history.error, undefined);
assert.equal(Array.from(step(state, 'LoadMoreCommits', firstId)[1]).length, 0);

// A result for another tab stays attached to that tab, even when it is hidden.
[state, command] = step(state, 'OpenRepositoryTab', repo('C:/two'));
const secondId = state.activeRepositoryTab;
[state, command] = update(await execute(command), state);
const second = execute(command);
(await nextRequest('git_open')).resolve({ repoId: 'second', root: 'C:/two' });
const emptyLog = await nextRequest('git_log');
state = step(state, 'SelectRepositoryTab', firstId)[0];
emptyLog.resolve({ commits: [], nextCursor: undefined });
state = update(await second, state)[0];
assert.equal(tab(state, firstId).history.commits.length, 1);
assert.match(render(tab(state, secondId)), /No commits yet/);

// Closing/reopening the same root creates a different identity; late replies die.
[state, command] = step(state, 'RefreshRepositoryHistory', firstId);
const obsolete = execute(command);
(await nextRequest('git_open')).resolve({ repoId: 'obsolete', root: 'C:/one' });
const obsoleteLog = await nextRequest('git_log');
state = step(state, 'CloseRepositoryTab', firstId)[0];
[state] = step(state, 'OpenRepositoryTab', repo('C:/one'));
assert.notEqual(state.activeRepositoryTab, firstId);
obsoleteLog.resolve({ commits: [commit('d'.repeat(40))], nextCursor: 'obsolete' });
state = update(await obsolete, state)[0];
assert.equal(tab(state).history.commits.length, 0);
assert.equal(tab(state).history.nextCursor, undefined);

// Initial failures are visibly different from an empty repository.
[state, command] = step(state, 'RefreshRepositoryHistory', state.activeRepositoryTab);
const unavailable = execute(command);
(await nextRequest('git_open')).reject('Repository not found');
state = update(await unavailable, state)[0];
assert.match(render(tab(state)), /Repository not found/);
assert.doesNotMatch(render(tab(state)), /No commits yet/);
const focusCommands = step(state, 'RefreshGitStatuses')[1];
assert.equal(cases[(await execute(focusCommands)).tag], 'RefreshRepositoryHistory');
console.log('PASS: commit history IPC, pagination, reconnect, empty/error states, escaping and isolated tab lifetimes.');
