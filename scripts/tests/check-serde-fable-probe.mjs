import assert from "node:assert/strict";
import { readdir } from "node:fs/promises";

const output = new URL("../../serde-fable-probe/Client/bin/fable-js/", import.meta.url);
const libraries = (await readdir(new URL("fable_modules/", output)))
  .filter(name => name.startsWith("fable-library-js."));
assert.equal(libraries.length, 1, "Expected one generated Fable runtime; clean the probe output if stale");
const { startAsPromise } = await import(new URL(`fable_modules/${libraries[0]}/Async.js`, output));
const { echo, echoProject } = await import(new URL("Probe.js", output));

const record = { Name: "serde-probe", Count: 7 };
const remotes = [null, { Case: "Wsl", Fields: ["Ubuntu"] }, { Case: "Ssh", Fields: ["example.test", "user"] }];
const project = remote => ({
  id: "probe",
  name: "Serde probe",
  lastOpened: "2026-10-19T12:00:00.000Z",
  description: "Fable compatibility",
  path: "E:/Projects/probe",
  ide: "VSCode",
  environment: [["EXTRA", "1"], ["UNICODE", "тест"]],
  remote,
});

const originalFetch = globalThis.fetch;
let requests = 0;

// No HTTP server or external network: validate the generated request and supply
// independent response fixtures, including the application's current JSON shapes.
function respond(method, expectedBody, responseBody, status = 200) {
  globalThis.fetch = async (url, options) => {
    requests++;
    assert.equal(url, `http://serde-probe.invalid/rpc/IProbeApi/${method}`);
    assert.equal(options.method, "POST");
    assert.equal(options.headers["Content-Type"], "application/json");
    const actual = JSON.parse(options.body);
    if (actual.lastOpened) {
      assert.equal(Date.parse(actual.lastOpened), Date.parse(expectedBody.lastOpened));
      actual.lastOpened = expectedBody.lastOpened;
    }
    assert.deepEqual(actual, expectedBody);
    return new Response(JSON.stringify(responseBody), { status });
  };
}

try {
  respond("Echo", record, record);
  assert.equal(await startAsPromise(echo()), "serde-probe");
  console.log("PASS: generated RPC record encoder, HTTP request and decoder");

  for (let i = 0; i < remotes.length; i++) {
    const fixture = project(remotes[i]);
    respond("EchoProject", fixture, fixture);
    assert.equal(await startAsPromise(echoProject(i)), "probe");
  }
  console.log("PASS: ProjectData-shaped DateTime, Map and Remote option (None/Wsl/Ssh)");

  const local = project(null);
  const appMap = { ...local, environment: { EXTRA: "1", UNICODE: "тест" } };
  respond("EchoProject", local, appMap);
  await assert.rejects(startAsPromise(echoProject(0)), /Environment mismatch: decoded count=0/);
  console.log("CONFIRMED INCOMPATIBILITY: current environment JSON object silently decodes as empty Map");

  const wsl = project(remotes[1]);
  respond("EchoProject", wsl, { ...wsl, remote: { Wsl: "Ubuntu" } });
  await assert.rejects(startAsPromise(echoProject(1)), /Unknown union case/);
  console.log("CONFIRMED INCOMPATIBILITY: current externally tagged Remote is rejected");

  respond("Echo", record, { error: "probe failure" }, 500);
  await assert.rejects(startAsPromise(echo()), /HTTP 500/);
  assert.equal(requests, 7);
  console.log("PASS: HTTP failure propagation");
  console.log("Serde.FS.Fable probe passed; current application wire format is incompatible.");
} finally {
  globalThis.fetch = originalFetch;
}
