const moduleUrl = new URL("../serde-probe-lib/bin/fable-js/SerdeProbe.js", import.meta.url);

function fail(message, error) {
  console.error(`serde-probe-lib check failed: ${message}`);

  if (error) {
    console.error(error && error.stack ? error.stack : error);
  }

  process.exit(1);
}

let probe;

try {
  probe = await import(moduleUrl.href);
} catch (error) {
  fail(`could not import generated module ${moduleUrl.pathname}`, error);
}

const sampleJson = probe.Probe_sampleJson;

if (typeof sampleJson !== "string") {
  fail(`Probe_sampleJson should be a string, got ${typeof sampleJson}`);
}

let parsed;

try {
  parsed = JSON.parse(sampleJson);
} catch (error) {
  fail(`Probe_sampleJson is not valid JSON: ${sampleJson}`, error);
}

if (parsed.Name !== "serde-probe" || parsed.Count !== 7) {
  fail(`unexpected Probe_sampleJson payload: ${sampleJson}`);
}

let roundTrip;

try {
  roundTrip = probe.Probe_fromJson(sampleJson);
} catch (error) {
  fail(`Probe_fromJson could not deserialize Probe_sampleJson: ${sampleJson}`, error);
}

if (roundTrip.Name !== "serde-probe" || roundTrip.Count !== 7) {
  fail(`unexpected Probe_fromJson result: ${JSON.stringify(roundTrip)}`);
}

let serializedAgain;

try {
  serializedAgain = probe.Probe_toJson(roundTrip);
} catch (error) {
  fail("Probe_toJson could not serialize the deserialized value", error);
}

if (serializedAgain !== sampleJson) {
  fail(`round-trip JSON mismatch: ${serializedAgain}`);
}

console.log(`serde-probe-lib check passed: ${sampleJson}`);
