// Fails when the AsyncAPI contract and the code that publishes or subscribes to events disagree.
//
// Checks, for every integration event:
//   1. Each Qf.Contracts record implementing IIntegrationEvent is described by exactly one AsyncAPI schema
//      (matched through x-csharp-type), with the same properties, JSON types and required set.
//   2. Each Subjects constant is an AsyncAPI message name, and each message name is a Subjects constant.
//   3. Each Topics constant is a channel address, and the reverse.
//   4. Each Service Bus subscription in Terraform filters on subjects that are published on that topic.
//
// Usage: node scripts/check-event-contracts.mjs [--root <repo root>]   (default: the folder above contracts/)
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parse } from "yaml";

const here = dirname(fileURLToPath(import.meta.url));
const rootArg = process.argv.indexOf("--root");
const root = rootArg > 0 ? resolve(process.argv[rootArg + 1]) : resolve(here, "..", "..");
const contractsDir = join(root, "src", "Contracts", "Qf.Contracts");
const asyncApiPath = join(root, "contracts", "asyncapi", "qf-integration-events.asyncapi.yaml");
const terraformPath = join(root, "infra", "terraform", "main.tf");

const errors = [];
const fail = (message) => errors.push(message);

// ---------------------------------------------------------------- C# side
const csharpTypes = {
  Guid: { type: "string", format: "uuid" },
  DateTimeOffset: { type: "string", format: "date-time" },
  DateOnly: { type: "string", format: "date" },
  string: { type: "string" },
  int: { type: "integer" },
  long: { type: "integer" },
  decimal: { type: "number" },
  double: { type: "number" },
  bool: { type: "boolean" },
};

const camel = (name) => name.charAt(0).toLowerCase() + name.slice(1);
const sources = readdirSync(contractsDir)
  .filter((f) => f.endsWith(".cs"))
  .map((f) => ({ file: f, text: readFileSync(join(contractsDir, f), "utf8") }));

const records = new Map(); // full C# name -> { file, properties: Map(json name -> { type, required }) }
for (const { file, text } of sources) {
  const ns = /^namespace\s+([\w.]+)\s*;/m.exec(text)?.[1] ?? "";
  const recordPattern = /record\s+(\w+)\s*\(([^)]*)\)\s*:\s*[^;{]*\bIIntegrationEvent\b/g;
  for (const match of text.matchAll(recordPattern)) {
    const properties = new Map();
    for (const raw of match[2].split(",").map((p) => p.trim()).filter(Boolean)) {
      const parts = raw.split(/\s+/);
      if (parts.length < 2) {
        fail(`${file}: cannot read parameter '${raw}' of ${match[1]}.`);
        continue;
      }
      const name = parts[parts.length - 1];
      let type = parts[parts.length - 2];
      const nullable = type.endsWith("?");
      type = type.replace(/\?$/, "");
      if (!csharpTypes[type]) {
        fail(`${file}: ${match[1]}.${name} has type '${type}', which this check does not map to JSON. Add it to csharpTypes.`);
        continue;
      }
      properties.set(camel(name), { ...csharpTypes[type], required: !nullable });
    }
    records.set(`${ns}.${match[1]}`, { file, properties });
  }
}

const constants = (className) => {
  const block = sources
    .map(({ text }) => new RegExp(`class\\s+${className}\\s*\\{([\\s\\S]*?)\\n\\}`).exec(text)?.[1])
    .find(Boolean);
  if (!block) {
    fail(`Qf.Contracts: class ${className} not found.`);
    return new Set();
  }
  return new Set([...block.matchAll(/const\s+string\s+\w+\s*=\s*"([^"]+)"/g)].map((m) => m[1]));
};
const subjects = constants("Subjects");
const topics = constants("Topics");

// ---------------------------------------------------------------- AsyncAPI side
const doc = parse(readFileSync(asyncApiPath, "utf8"));
const deref = (node) => {
  if (node && typeof node === "object" && typeof node.$ref === "string") {
    const target = node.$ref.replace(/^#\//, "").split("/").reduce((n, key) => n?.[key], doc);
    if (!target) fail(`AsyncAPI: unresolved $ref ${node.$ref}.`);
    return deref(target);
  }
  return node;
};

// Flatten allOf into one property map and one required set.
const flatten = (schema) => {
  schema = deref(schema);
  const properties = new Map();
  const required = new Set(schema.required ?? []);
  for (const part of schema.allOf ?? []) {
    const flat = flatten(part);
    flat.properties.forEach((v, k) => properties.set(k, v));
    flat.required.forEach((r) => required.add(r));
  }
  for (const [name, prop] of Object.entries(schema.properties ?? {})) properties.set(name, deref(prop));
  return { properties, required };
};

const schemasByCsharp = new Map();
for (const [schemaName, schema] of Object.entries(doc.components?.schemas ?? {})) {
  const csharp = schema["x-csharp-type"];
  if (!csharp) continue;
  if (schemasByCsharp.has(csharp)) fail(`AsyncAPI: ${csharp} is described by more than one schema.`);
  schemasByCsharp.set(csharp, { schemaName, ...flatten(schema) });
}

// 1. Records <-> schemas
for (const [csharp, record] of records) {
  const schema = schemasByCsharp.get(csharp);
  if (!schema) {
    fail(`${record.file}: ${csharp} is published but has no AsyncAPI schema (x-csharp-type: ${csharp}).`);
    continue;
  }
  for (const [name, expected] of record.properties) {
    const actual = schema.properties.get(name);
    if (!actual) {
      fail(`${schema.schemaName}: missing property '${name}' that ${csharp} serializes.`);
      continue;
    }
    if (actual.type !== expected.type || (expected.format ?? null) !== (actual.format ?? null)) {
      fail(`${schema.schemaName}.${name}: AsyncAPI says ${actual.type}/${actual.format ?? "-"}, C# serializes ${expected.type}/${expected.format ?? "-"}.`);
    }
    if (expected.required !== schema.required.has(name)) {
      fail(`${schema.schemaName}.${name}: required is ${schema.required.has(name)} in AsyncAPI but ${expected.required} in C#.`);
    }
  }
  for (const name of schema.properties.keys()) {
    if (!record.properties.has(name)) fail(`${schema.schemaName}: property '${name}' is documented but ${csharp} does not serialize it.`);
  }
}
for (const csharp of schemasByCsharp.keys()) {
  if (!records.has(csharp)) fail(`AsyncAPI: x-csharp-type ${csharp} does not match any IIntegrationEvent record in Qf.Contracts.`);
}

// 2. Subjects <-> message names
const messageNames = new Set(Object.values(doc.components?.messages ?? {}).map((m) => m.name));
for (const s of subjects) if (!messageNames.has(s)) fail(`Subjects.${s}: no AsyncAPI message is named '${s}'.`);
for (const m of messageNames) if (!subjects.has(m)) fail(`AsyncAPI message '${m}' has no constant in Qf.Contracts.Subjects.`);

// 3. Topics <-> channel addresses, and which subjects each topic carries
const subjectsByTopic = new Map();
for (const channel of Object.values(doc.channels ?? {})) {
  subjectsByTopic.set(channel.address, new Set(Object.values(channel.messages ?? {}).map((m) => deref(m).name)));
}
for (const t of topics) if (!subjectsByTopic.has(t)) fail(`Topics.${t}: no AsyncAPI channel has address '${t}'.`);
for (const t of subjectsByTopic.keys()) if (!topics.has(t)) fail(`AsyncAPI channel '${t}' has no constant in Qf.Contracts.Topics.`);

// 4. Terraform subscriptions filter on subjects their topic actually carries
const terraform = readFileSync(terraformPath, "utf8");
const subscriptionPattern = /(\w+)\s*=\s*\{\s*topic\s*=\s*"([^"]+)"\s*,\s*subjects\s*=\s*\[([^\]]*)\]\s*\}/g;
let subscriptionCount = 0;
for (const [, name, topic, list] of terraform.matchAll(subscriptionPattern)) {
  subscriptionCount++;
  const carried = subjectsByTopic.get(topic);
  if (!carried) {
    fail(`Terraform subscription '${name}': topic '${topic}' is not an AsyncAPI channel.`);
    continue;
  }
  for (const subject of [...list.matchAll(/"([^"]+)"/g)].map((m) => m[1])) {
    if (!carried.has(subject)) fail(`Terraform subscription '${name}' filters on '${subject}', which is not published on '${topic}'.`);
  }
}
if (subscriptionCount === 0) fail(`${terraformPath}: no Service Bus subscriptions found; the pattern may need updating.`);

// ---------------------------------------------------------------- result
if (errors.length) {
  console.error(`Event contract check failed with ${errors.length} problem(s):`);
  for (const e of errors) console.error(`  - ${e}`);
  process.exit(1);
}
console.log(`Event contracts match: ${records.size} events, ${subjects.size} subjects, ${topics.size} topics, ${subscriptionCount} subscriptions.`);
