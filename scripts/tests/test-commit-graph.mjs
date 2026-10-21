// Compile Fable first. Trace rendered lanes back to the actual Git parents.
import assert from 'node:assert/strict';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { layout } from '../../src/bin/fable-js/src/domain/CommitGraph.js';
import { RepositoryPanel } from '../../src/bin/fable-js/src/ui/DesktopTabs.js';

const commit = (oid, ...parents) => ({
    oid, parents, subject: `Commit ${oid}`, refs: [], authorName: 'Author',
    authorEmail: 'author@example.test', authorDate: '2026-10-21T10:00:00Z',
    commitDate: '2026-10-21T10:00:00Z',
});
const plain = value => JSON.parse(JSON.stringify(value));

function verifyTopology(commits) {
    const graph = layout(commits);
    assert.deepEqual(graph.rows.map(row => row.oid), commits.map(commit => commit.oid));
    let boundary = new Map();
    for (const [index, row] of graph.rows.entries()) {
        const incoming = new Map([...row.passing, ...row.joining].map(edge => [edge.fromLane, edge.color]));
        if (row.incoming) incoming.set(row.lane, row.color);
        assert.deepEqual(incoming, boundary, `Continuous lanes/colors above ${row.oid}`);
        assert.ok(!row.passing.some(edge => edge.fromLane === row.lane), 'No unrelated route passes through a node');
        assert.ok(row.lane < graph.laneCount);
        assert.equal(row.parents.length, new Set(commits[index].parents).size);
        boundary = new Map([...row.passing, ...row.parents].map(edge => [edge.toLane, edge.color]));
        assert.ok([...boundary.keys()].every(lane => lane >= 0 && lane < graph.laneCount));

        const reached = row.parents.map(edge => {
            let lane = edge.toLane;
            for (const next of graph.rows.slice(index + 1)) {
                if ((next.incoming && next.lane === lane) || next.joining.some(edge => edge.fromLane === lane)) return next.oid;
                const route = next.passing.find(edge => edge.fromLane === lane);
                assert.ok(route, `Route from ${row.oid} survives unrelated rows`);
                lane = route.toLane;
            }
            assert.ok(graph.continuations[lane], 'Off-page parents retain a route');
            return graph.continuations[lane].target;
        });
        assert.deepEqual(reached, [...new Set(commits[index].parents)], `Exact parent order for ${row.oid}`);
    }
    assert.deepEqual(boundary, new Map(graph.continuations.map((lane, index) => [index, lane.color])));
    assert.equal(new Set(graph.continuations.map(lane => lane.color)).size, graph.continuations.length);
    return graph;
}

assert.equal(verifyTopology([]).laneCount, 0);
const linear = verifyTopology([commit('tip', 'middle'), commit('middle', 'root'), commit('root')]);
assert.equal(linear.laneCount, 1);
assert.equal(new Set(linear.rows.map(row => row.color)).size, 1);
assert.equal(linear.continuations.length, 0);

// Merge, independent tip, shared first parent, octopus merge, disconnected root.
const commits = [
    commit('merge', 'main', 'feature'), commit('other-tip', 'base'),
    commit('feature', 'base'), commit('main', 'base'),
    commit('base', 'left', 'right', 'third'), commit('left', 'root'),
    commit('right', 'root'), commit('third', 'root'),
    commit('unrelated-root'), commit('root'),
];
const graph = verifyTopology(commits);
assert.ok(graph.laneCount >= 3);
assert.equal(graph.rows[0].parents.length, 2);
assert.equal(graph.rows[4].parents.length, 3);
assert.equal(graph.rows[8].incoming, false);
assert.equal(graph.rows[8].parents.length, 0);
assert.equal(graph.continuations.length, 0);
for (let count = 1; count <= commits.length; count++) {
    const prefix = verifyTopology(commits.slice(0, count));
    assert.deepEqual(plain(prefix.rows), plain(graph.rows.slice(0, count)), 'Loading another page preserves existing geometry/colors');
}

// Names follow route identity, not a column or the repeated palette color.
const named = (oid, parents, refs = [], subject = `Commit ${oid}`) => ({ ...commit(oid, ...parents), refs, subject });
const namedCommits = [
    named('merge', ['main', 'feature'], ['refs/remotes/origin/main', 'refs/heads/main', 'HEAD']),
    named('main', ['base']),
    named('feature', ['feature-old'], ['refs/heads/feature/x']),
    named('feature-old', ['base']),
    named('unrelated', [], ['refs/heads/archive']),
    named('base', []),
];
const namedGraph = verifyTopology(namedCommits);
assert.deepEqual(namedGraph.rows.map(row => row.branch?.name), ['main', 'main', 'feature/x', 'feature/x', 'archive', 'main']);
assert.equal(namedGraph.rows[0].branch.reference, 'refs/heads/main', 'Local branch preferred over remote on the same commit');
assert.notEqual(namedGraph.rows[2].color, namedGraph.rows[0].color);
const sideFirst = [namedCommits[0], namedCommits[2], namedCommits[3], namedCommits[1], namedCommits[4], namedCommits[5]];
const sideFirstGraph = verifyTopology(sideFirst);
assert.equal(sideFirstGraph.rows.at(-1).branch.name, 'main', 'Shared ancestor retains the main first-parent line when the side branch arrives first');
assert.equal(sideFirstGraph.rows.at(-1).joining.length, 1, 'Side branch joins at its actual fork commit');

const merged = [
    named('merge', ['main', 'feature'], [], "Merge branch 'deleted-feature' into dev"),
    named('main', ['root']), named('feature', ['root']), named('root', []),
];
assert.deepEqual(layout(merged).rows.map(row => row.branch?.name), ['dev', 'dev', 'deleted-feature', 'dev']);
assert.equal(layout(merged).rows[2].branch.reference, undefined, 'Merge subjects are only hints');
const realRef = merged.map(row => row.oid === 'feature' ? { ...row, refs: ['refs/remotes/upstream/actual-feature'] } : row);
assert.equal(layout(realRef).rows[2].branch.name, 'upstream/actual-feature', 'Real refs override merge hints');
assert.equal(layout(realRef).rows[2].branch.reference, 'refs/remotes/upstream/actual-feature');
const prefixBeforeRef = layout(realRef.slice(0, 2));
const geometry = rows => plain(rows.map(({ branch, ...row }) => row));
assert.deepEqual(geometry(prefixBeforeRef.rows), geometry(layout(realRef).rows.slice(0, 2)), 'Resolving branch names never reroutes existing rows');
const remoteMerge = merged.map((row, index) => index === 0 ? { ...row, subject: "Merge remote-tracking branch 'origin/fix'" } : row);
assert.equal(layout(remoteMerge).rows[2].branch.name, 'origin/fix');
assert.equal(layout(remoteMerge).rows[0].branch, undefined, 'No guessed destination if the merge subject omits it');
assert.equal(layout([named('t', [], ['HEAD', 'refs/tags/v1', 'refs/remotes/origin/HEAD'])]).rows[0].branch, undefined);
assert.equal(layout([named('t', ['r'], [], "Merge branch 'not-a-merge' into main"), named('r', [])]).rows[0].branch, undefined);
const octopus = layout([
    named('merge', ['a', 'b', 'c'], [], "Merge branch 'ambiguous' into main"),
    named('a', []), named('b', []), named('c', []),
]);
assert.deepEqual(octopus.rows.map(row => row.branch?.name), ['main', 'main', undefined, undefined]);
const reusedColumns = layout(Array.from({ length: 10 }, (_, index) => named(String(index), [], [`refs/heads/branch-${index}`])));
assert.equal(reusedColumns.laneCount, 1);
assert.equal(new Set(reusedColumns.rows.map(row => row.color)).size, 10);
assert.equal(new Set(reusedColumns.rows.map(row => row.branch.name)).size, 10);

// A name found later can identify the preceding undecorated part of the same line.
const laterRef = [named('tip', ['older']), named('older', [], ['refs/heads/release'])];
assert.equal(layout(laterRef.slice(0, 1)).rows[0].branch, undefined);
assert.equal(layout(laterRef).rows[0].branch.name, 'release');
assert.deepEqual(geometry(layout(laterRef.slice(0, 1)).rows), geometry(layout(laterRef).rows.slice(0, 1)));

// Deterministic DAGs cover interleaved branches, cross merges and lane collapse.
let seed = 641;
const random = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32);
for (let trial = 0; trial < 80; trial++) {
    const dag = Array.from({ length: 40 }, (_, index) => {
        const available = 39 - index;
        const parents = Array.from({ length: Math.min(available, Math.floor(random() * 4)) },
            () => String(index + 1 + Math.floor(random() * available)));
        return commit(String(index), ...new Set(parents));
    });
    const full = verifyTopology(dag);
    const count = 1 + Math.floor(random() * 39);
    assert.deepEqual(plain(verifyTopology(dag.slice(0, count)).rows), plain(full.rows.slice(0, count)));
    assert.equal(full.continuations.length, 0);
}

const tab = {
    id: 'graph-test', name: 'Graph repository', root: 'C:/graph',
    history: { commits, hasLoaded: true },
};
const render = tab => renderToStaticMarkup(React.createElement(RepositoryPanel, { tab, dispatch() {} }));
const html = render(tab);
assert.match(html, /<th>Graph<\/th>/);
assert.equal((html.match(/class="commit-graph-node"/g) ?? []).length, commits.length);
assert.match(html, /Merge commit \(3 parents\)/);
assert.match(html, /Root commit/);
assert.match(html, /vector-effect="non-scaling-stroke"/);
assert.doesNotMatch(html, /commit-graph-continuation/);
const partial = { ...tab, history: { commits: commits.slice(0, 2), hasLoaded: true, nextCursor: 'page-2' } };
assert.match(render(partial), /History continues below/);
assert.match(render(partial), /stroke-dasharray="3,3"/);
assert.match(render({ ...partial, history: { ...partial.history, nextCursor: undefined, error: 'Cursor expired' } }),
    /Some parents are outside the loaded history/);
assert.match(render({ ...tab, selection: { commit: commits[0], parent: 1, files: [], hasLoaded: true } }), /r="9"/);
assert.equal(render({ ...tab, history: { commits: [], hasLoaded: true } }).includes('commit-graph-node'), false);
const namedHtml = render({ ...tab, history: { commits: namedCommits, hasLoaded: true } });
assert.match(namedHtml, /title="refs\/heads\/feature\/x"[^>]*>feature\/x<\/span>/);
assert.match(namedHtml, /class="commit-branch-hint[^>]*Branch line: main/);
assert.match(namedHtml, /data-graph-member="false"/);
assert.match(render({ ...tab, history: { commits: merged, hasLoaded: true } }), /~ deleted-feature/);
const unsafeName = [named('tip', ['root'], ['refs/heads/<img>']), named('root', [])];
const escapedHtml = render({ ...tab, history: { commits: unsafeName, hasLoaded: true } });
assert.match(escapedHtml, /&lt;img&gt;/);
assert.doesNotMatch(escapedHtml, /<img>/);
console.log('PASS: graph topology, pagination, branch labels and merge hints, distinct route identities, selection and SVG rendering.');
