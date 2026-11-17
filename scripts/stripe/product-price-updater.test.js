const assert = require("node:assert/strict");
const { mkdtemp, rm, writeFile } = require("node:fs/promises");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const { execFile } = require("node:child_process");
const { promisify } = require("node:util");
const { runProductPriceUpdate, toUnitAmount } = require("./product-price-updater");
const { readConfiguredPrices } = require("../shared/catalog-prices");
const { main: updateZentitle } = require("../zentitle/update-stripe-product-prices");
const { main: updateZenmeter } = require("../zenmeter/update-stripe-product-prices");

test("toUnitAmount converts USD without floating-point rounding errors", () => {
  for (const [amount, cents] of [[0, 0], [19.99, 1999], [0.29, 29], [999999.99, 99999999]]) {
    assert.equal(toUnitAmount(amount), cents);
  }
  for (const amount of [-1, NaN, Infinity, 1.005, 0.00000000001, 1000000]) {
    assert.throws(() => toUnitAmount(amount), /Invalid USD price/);
  }
});

test("CLI rejects unknown, duplicate and missing options before loading configuration", async () => {
  for (const args of [["--dry-rnu"], ["--appsettings"], ["--appsettings=x", "--appsettings=y"], []]) {
    await assert.rejects(() => runUpdate(args), /Unknown option|Missing value|more than once|required/);
  }
  await runUpdate(["--help"]);
});

test("dry-run validates every SKU and sends no writes", async (t) => {
  const fixture = await setup(t, { prices: { first: makePrice("first"), second: makePrice("second") } });
  await runUpdate([...fixture.args, "--dry-run"]);
  assert.ok(fixture.requests.every(({ method }) => method === "GET"));
  const lookups = fixture.requests.filter(({ pathname }) => pathname === "/v1/prices");
  assert.deepEqual(lookups.map(({ sku }) => sku), ["first", "second"]);
  for (const request of lookups) {
    assert.equal(request.headers.authorization, "Bearer test-secret");
    assert.equal(request.query.get("expand[]"), "data.currency_options");
    assert.equal(request.query.get("active"), "true");
  }
});

test("missing SKU aborts the entire preflight before any writes", async (t) => {
  const fixture = await setup(t, {
    prices: { existing: makePrice("existing") },
    configured: { existing: { Price: 20 }, missing: { Price: 30 } },
  });
  await assert.rejects(() => runUpdate(fixture.args), /Preflight failed.*\nmissing:/);
  assert.equal(fixture.requests.filter(({ method }) => method === "POST").length, 0);
});

test("unchanged prices are skipped, including on a repeated run", async (t) => {
  const fixture = await setup(t, {
    prices: { unchanged: makePrice("unchanged", { unit_amount: 2000 }), changed: makePrice("changed") },
  });
  await runUpdate(fixture.args);
  await runUpdate(fixture.args);
  const writes = fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices");
  assert.equal(writes.length, 1);
  assert.equal(writes[0].body.get("lookup_key"), "changed");
  assert.deepEqual(fixture.requests.slice(0, 3).map(({ method }) => method), ["GET", "GET", "GET"]);
  assert.equal(fixture.allPrices.price_changed.active, false);
});

test("replacement preserves product, recurrence, tax, metadata and nickname and transfers lookup key", async (t) => {
  const fixture = await setup(t, { prices: { sku: makePrice("sku", {
    type: "recurring",
    recurring: { interval: "month", interval_count: 3, usage_type: "licensed", trial_period_days: null },
    metadata: { offering: "example", description: "a & b" },
    nickname: "Quarterly",
    tax_behavior: "inclusive",
  }) } });
  await runUpdate(fixture.args);
  const writes = fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices");
  assert.equal(writes.length, 1);
  const { body, headers, pathname } = writes[0];
  assert.equal(pathname, "/v1/prices");
  assert.deepEqual(Object.fromEntries(body), {
    product: "prod_sku", currency: "usd", unit_amount: "2000", billing_scheme: "per_unit",
    active: "true", lookup_key: "sku", transfer_lookup_key: "true", tax_behavior: "inclusive",
    nickname: "Quarterly", "metadata[offering]": "example", "metadata[description]": "a & b",
    "metadata[demo_price_update_previous]": "price_sku",
    "recurring[interval]": "month", "recurring[interval_count]": "3",
    "recurring[usage_type]": "licensed",
  });
  assert.match(headers["idempotency-key"], /^demo-price-update-[a-f0-9]{64}$/);
  assert.equal(headers["content-type"], "application/x-www-form-urlencoded");
});

for (const [scenario, override, expected] of [
  ["wrong currency", { currency: "eur" }, /Expected USD/],
  ["inactive Price", { active: false }, /active Price/],
  ["mismatched lookup key", { lookup_key: "other" }, /active Price/],
  ["missing product", { product: null }, /product id/],
  ["tiered Price", { billing_scheme: "tiered" }, /fixed per-unit/],
  ["fractional cents", { unit_amount: null, unit_amount_decimal: "1.5" }, /fixed per-unit/],
  ["custom amount", { custom_unit_amount: { enabled: true } }, /fixed per-unit/],
  ["quantity transform", { transform_quantity: { divide_by: 10, round: "up" } }, /fixed per-unit/],
  ["multi-currency Price", { currency_options: { usd: {}, eur: {} } }, /Multi-currency/],
  ["metered Price", { type: "recurring", recurring: { interval: "month", interval_count: 1, usage_type: "metered" } }, /licensed recurring/],
  ["invalid interval", { type: "recurring", recurring: { interval: "month", interval_count: 0, usage_type: "licensed" } }, /licensed recurring/],
  ["legacy Price-level trial", { type: "recurring", recurring: { interval: "month", interval_count: 1, usage_type: "licensed", trial_period_days: 14 } }, /Legacy Price-level trial/],
  ["unknown type", { type: "unknown" }, /one-time or licensed/],
]) {
  test(`preflight rejects ${scenario} without writes`, async (t) => {
    const fixture = await setup(t, { prices: { sku: makePrice("sku", override) } });
    await assert.rejects(() => runUpdate(fixture.args), expected);
    assert.deepEqual(fixture.requests.map(({ method }) => method), ["GET"]);
  });
}

test("ambiguous or truncated list results are rejected", async (t) => {
  for (const listResult of [
    { data: [makePrice("sku"), makePrice("sku")], has_more: false },
    { data: [makePrice("sku")], has_more: true },
    { unexpected: true },
  ]) {
    const fixture = await setup(t, { prices: { sku: makePrice("sku") }, listResult });
    await assert.rejects(() => runUpdate(fixture.args), /exactly one active/);
    assert.equal(fixture.requests.length, 1);
  }
});

test("invalid configured amount is rejected before API access", async (t) => {
  const fixture = await setup(t, { prices: { sku: makePrice("sku") }, configured: { sku: { Price: 2.345 } } });
  await assert.rejects(() => runUpdate(fixture.args), /Invalid USD price/);
  assert.equal(fixture.requests.length, 0);
});

test("API write failure reports partial progress and stops subsequent writes", async (t) => {
  const fixture = await setup(t, {
    prices: { first: makePrice("first"), second: makePrice("second"), third: makePrice("third") },
    failSku: "second",
  });
  await assert.rejects(() => runUpdate(fixture.args), /Stopped at 'second' after 1 completed.*HTTP 400/);
  assert.deepEqual(fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices").map(({ body }) => body.get("lookup_key")), ["first", "second"]);
});

test("uncertain write can be retried with the same idempotency key", async (t) => {
  const fixture = await setup(t, { prices: { sku: makePrice("sku") }, failSku: "sku" });
  await assert.rejects(() => runUpdate(fixture.args), /HTTP 400/);
  await assert.rejects(() => runUpdate(fixture.args), /HTTP 400/);
  const writes = fixture.requests.filter(({ method }) => method === "POST");
  assert.equal(writes[0].headers["idempotency-key"], writes[1].headers["idempotency-key"]);
});

test("malformed and unsuccessful read responses never cause writes", async (t) => {
  for (const readFailure of ["invalid-json", "unauthorized"]) {
    const fixture = await setup(t, { prices: { sku: makePrice("sku") }, readFailure });
    await assert.rejects(() => runUpdate(fixture.args), /Preflight failed/);
    assert.equal(fixture.requests.length, 1);
  }
});

test("unexpected successful write response is reported for manual verification", async (t) => {
  const fixture = await setup(t, { prices: { sku: makePrice("sku") }, postResult: {} });
  await assert.rejects(() => runUpdate(fixture.args), /unexpected replacement Price/);
});

test("Zentitle entrypoint updates yearly and perpetual SKUs from its own section", async (t) => {
  const fixture = await setup(t, { section: "Zentitle", prices: {
    "standard-yearly": makePrice("standard-yearly", { type: "recurring", recurring: { interval: "year", interval_count: 1, usage_type: "licensed" } }),
    "standard-perpetual": makePrice("standard-perpetual"),
  } });
  await updateZentitle([...fixture.args, "--dry-run"]);
  assert.ok(fixture.requests.every(({ method }) => method === "GET"));
  await updateZentitle(fixture.args);
  const writes = fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices");
  assert.equal(writes.length, 2);
  assert.equal(writes[0].body.get("recurring[interval]"), "year");
  assert.equal(writes[1].body.has("recurring[interval]"), false);
});

test("Zentitle rejects ambiguous SKUs and mismatched billing periods", async (t) => {
  for (const sku of ["ambiguous", "standard-yearly", "standard-perpetual"]) {
    const fixture = await setup(t, { section: "Zentitle", prices: {
      [sku]: makePrice(sku, { type: "recurring", recurring: { interval: "month", interval_count: 1, usage_type: "licensed" } }),
    } });
    await assert.rejects(() => updateZentitle(fixture.args), /Cannot determine|Expected yearly|Expected one-time/);
    assert.equal(fixture.requests.filter(({ method }) => method === "POST").length, 0);
  }
});

test("Zenmeter entrypoint preserves monthly, yearly and one-time add-on billing", async (t) => {
  const fixture = await setup(t, { section: "Zenmeter", prices: {
    monthly: makePrice("monthly", { type: "recurring", recurring: { interval: "month", interval_count: 1, usage_type: "licensed" } }),
    yearly: makePrice("yearly", { type: "recurring", recurring: { interval: "year", interval_count: 1, usage_type: "licensed" } }),
    topup: makePrice("topup"),
  } });
  await updateZenmeter(fixture.args);
  const writes = fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices");
  assert.deepEqual(writes.map(({ body }) => body.get("recurring[interval]")), ["month", "year", null]);
});

test("CLI uses config credentials and endpoint, with environment and argument overrides", async (t) => {
  const fixture = await setup(t, { section: "Zenmeter", prices: { sku: makePrice("sku") } });
  const env = { ...process.env };
  delete env.STRIPE_SECRET_KEY;
  const script = path.resolve(__dirname, "../zenmeter/update-stripe-product-prices.js");
  const args = [script, "--appsettings", fixture.appsettingsPath, "--dry-run"];
  await promisify(execFile)(process.execPath, args, { env });
  await promisify(execFile)(process.execPath, args, { env: { ...env, STRIPE_SECRET_KEY: "environment-secret" } });
  await promisify(execFile)(process.execPath, [...args, "--secret-key=argument-secret"], { env: { ...env, STRIPE_SECRET_KEY: "environment-secret" } });
  assert.deepEqual(fixture.requests.filter(({ pathname }) => pathname === "/v1/prices").map(({ headers }) => headers.authorization), ["Bearer config-secret", "Bearer environment-secret", "Bearer argument-secret"]);
});

test("replacement moves the default Price before archiving the predecessor", async (t) => {
  const fixture = await setup(t, {
    prices: { sku: makePrice("sku") }, defaultPrices: { prod_sku: "price_sku" },
  });
  await runUpdate(fixture.args);
  assert.deepEqual(fixture.requests.filter(({ method }) => method === "POST").map(({ pathname }) => pathname),
    ["/v1/prices", "/v1/products/prod_sku", "/v1/prices/price_sku"]);
  assert.equal(fixture.products.prod_sku.default_price, "price_new_sku");
  assert.equal(fixture.allPrices.price_sku.active, false);
  assert.equal(fixture.allPrices.price_sku.lookup_key, null);
  assert.equal(fixture.allPrices.price_new_sku.lookup_key, "sku");
});

test("replacement preserves an unrelated product default Price", async (t) => {
  const fixture = await setup(t, {
    prices: { sku: makePrice("sku") }, defaultPrices: { prod_sku: "price_unrelated" },
  });
  await runUpdate(fixture.args);
  assert.equal(fixture.products.prod_sku.default_price, "price_unrelated");
  assert.equal(fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname.startsWith("/v1/products/")).length, 0);
});

test("failed archive is completed on rerun without creating another Price", async (t) => {
  const fixture = await setup(t, { prices: { sku: makePrice("sku") }, archiveFailures: 1 });
  await assert.rejects(() => runUpdate(fixture.args), /Archive failed/);
  assert.equal(fixture.allPrices.price_sku.active, true);
  assert.equal(fixture.allPrices.price_new_sku.metadata.demo_price_update_previous, "price_sku");
  await runUpdate(fixture.args);
  assert.equal(fixture.allPrices.price_sku.active, false);
  assert.equal(fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices").length, 1);
});

test("failed default update does not archive the default Price", async (t) => {
  const fixture = await setup(t, {
    prices: { sku: makePrice("sku") }, defaultPrices: { prod_sku: "price_sku" }, defaultFailure: true,
  });
  await assert.rejects(() => runUpdate(fixture.args), /Default Price update failed/);
  assert.equal(fixture.allPrices.price_sku.active, true);
  assert.equal(fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices/price_sku").length, 0);
});

test("cleanup log previews and archives only exact predecessors without creating Prices", async (t) => {
  const fixture = await setup(t, {
    prices: { sku: makePrice("sku"), unrelated: makePrice("unrelated") },
    oldPrices: [makePrice("sku", { id: "price_old", lookup_key: null, unit_amount: 0 })],
    defaultPrices: { prod_sku: "price_old" },
  });
  const args = await cleanupArgs(fixture, "Updated sku: price_old -> price_sku");
  await runUpdate([...args, "--dry-run"]);
  assert.ok(fixture.requests.every(({ method }) => method === "GET"));
  assert.equal(fixture.allPrices.price_old.active, true);
  await runUpdate(args);
  assert.equal(fixture.allPrices.price_old.active, false);
  assert.equal(fixture.allPrices.price_sku.active, true);
  assert.equal(fixture.allPrices.price_sku.unit_amount, 1000);
  assert.equal(fixture.allPrices.price_unrelated.active, true);
  assert.equal(fixture.products.prod_sku.default_price, "price_sku");
  assert.equal(fixture.requests.filter(({ method, pathname }) => method === "POST" && pathname === "/v1/prices").length, 0);
  const before = fixture.requests.filter(({ method }) => method === "POST").length;
  await runUpdate(args);
  assert.equal(fixture.requests.filter(({ method }) => method === "POST").length, before);
});

test("cleanup refuses stale, malformed or unrelated log entries before writing", async (t) => {
  for (const log of [
    "Updated sku: price_old -> price_wrong",
    "Updated unknown: price_old -> price_sku",
    "Updated sku: price_old -> price_sku\nUpdated sku: price_other -> price_sku",
    "Updated sku: bad -> price_sku",
    "No updates here",
  ]) {
    const fixture = await setup(t, { prices: { sku: makePrice("sku") } });
    const args = await cleanupArgs(fixture, log);
    await assert.rejects(() => runUpdate(args), /Cleanup log|cleanup log|Duplicate SKU|Invalid Updated/);
    assert.equal(fixture.requests.filter(({ method }) => method === "POST").length, 0);
  }
});

test("cleanup does not archive a keyed or incompatible Price", async (t) => {
  for (const override of [
    { lookup_key: "another-sku" }, { product: "prod_other" }, { currency: "eur" },
    { type: "recurring", recurring: { interval: "month", interval_count: 1, usage_type: "licensed" } },
  ]) {
    const fixture = await setup(t, {
      prices: { sku: makePrice("sku") },
      oldPrices: [makePrice("sku", { id: "price_old", lookup_key: null, ...override })],
    });
    const args = await cleanupArgs(fixture, "Updated sku: price_old -> price_sku");
    await assert.rejects(() => runUpdate(args), /not a compatible, unkeyed predecessor/);
    assert.equal(fixture.requests.filter(({ method }) => method === "POST").length, 0);
  }
});

test("one invalid predecessor aborts cleanup of the entire catalog", async (t) => {
  const fixture = await setup(t, {
    prices: { first: makePrice("first"), second: makePrice("second") },
    oldPrices: [
      makePrice("first", { id: "price_old_first", lookup_key: null }),
      makePrice("second", { id: "price_old_second", lookup_key: "still-in-use" }),
    ],
  });
  const args = await cleanupArgs(fixture,
    "Updated first: price_old_first -> price_first\nUpdated second: price_old_second -> price_second");
  await assert.rejects(() => runUpdate(args), /Preflight failed/);
  assert.equal(fixture.requests.filter(({ method }) => method === "POST").length, 0);
});

async function cleanupArgs(fixture, log) {
  const file = path.join(fixture.directory, "cleanup.log");
  await writeFile(file, log);
  return [...fixture.args, "--cleanup-log", file];
}

function makePrice(sku, overrides = {}) {
  return {
    id: `price_${sku}`, product: `prod_${sku}`, active: true, lookup_key: sku, currency: "usd",
    billing_scheme: "per_unit", unit_amount: 1000, type: "one_time", recurring: null,
    tax_behavior: "exclusive", currency_options: { usd: { unit_amount: 1000 } },
    ...overrides,
  };
}

function runUpdate(args) {
  return runProductPriceUpdate({
    args, productName: "Test", scriptPath: "scripts/test.js", priceSource: "Test.Prices",
    readPrices: (configuration) => readConfiguredPrices(configuration, "Test"),
  });
}

async function setup(t, { prices, configured, section = "Test", listResult, failSku, readFailure, postResult,
  oldPrices = [], defaultPrices = {}, archiveFailures = 0, defaultFailure = false }) {
  const requests = [];
  const catalog = { ...prices };
  const allPrices = Object.fromEntries([...Object.values(prices), ...oldPrices].map((price) => [price.id, price]));
  const products = Object.fromEntries(Object.values(prices).map((price) => [price.product, {
    id: price.product, active: true, default_price: defaultPrices[price.product] ?? null,
  }]));
  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, "http://127.0.0.1");
    const sku = url.searchParams.get("lookup_keys[]");
    const record = { method: request.method, headers: request.headers, pathname: url.pathname, query: url.searchParams, sku };
    requests.push(record);
    response.setHeader("Content-Type", "application/json");
    if (request.method === "GET" && url.pathname === "/v1/prices") {
      if (readFailure === "invalid-json") { response.end("not JSON"); return; }
      if (readFailure === "unauthorized") {
        response.writeHead(401);
        response.end(JSON.stringify({ error: { message: "Unauthorized" } }));
        return;
      }
      response.end(JSON.stringify(listResult ?? { data: catalog[sku] ? [catalog[sku]] : [], has_more: false }));
      return;
    }
    if (request.method === "GET" && url.pathname.startsWith("/v1/prices/")) {
      const price = allPrices[decodeURIComponent(url.pathname.slice("/v1/prices/".length))];
      if (!price) response.writeHead(404);
      response.end(JSON.stringify(price ?? {}));
      return;
    }
    if (request.method === "GET" && url.pathname.startsWith("/v1/products/")) {
      response.end(JSON.stringify(products[decodeURIComponent(url.pathname.slice("/v1/products/".length))] ?? {}));
      return;
    }
    if (request.method === "POST") {
      const chunks = [];
      for await (const chunk of request) chunks.push(chunk);
      const body = new URLSearchParams(Buffer.concat(chunks).toString("utf8"));
      record.body = body;
    }
    if (request.method === "POST" && url.pathname.startsWith("/v1/prices/")) {
      if (archiveFailures-- > 0) {
        response.writeHead(500);
        response.end(JSON.stringify({ error: { message: "Archive failed" } }));
        return;
      }
      const price = allPrices[decodeURIComponent(url.pathname.slice("/v1/prices/".length))];
      price.active = false;
      response.end(JSON.stringify(price));
      return;
    }
    if (request.method === "POST" && url.pathname.startsWith("/v1/products/")) {
      if (defaultFailure) {
        response.writeHead(400);
        response.end(JSON.stringify({ error: { message: "Default Price update failed" } }));
        return;
      }
      const product = products[decodeURIComponent(url.pathname.slice("/v1/products/".length))];
      product.default_price = record.body.get("default_price");
      response.end(JSON.stringify(product));
      return;
    }
    if (request.method === "POST" && url.pathname === "/v1/prices") {
      const body = record.body;
      const lookupKey = body.get("lookup_key");
      if (lookupKey === failSku) {
        response.writeHead(400);
        response.end(JSON.stringify({ error: { message: "Rejected" } }));
        return;
      }
      const metadata = Object.fromEntries([...body].filter(([key]) => key.startsWith("metadata[")).map(([key, value]) => [key.slice(9, -1), value]));
      const replacement = { ...catalog[lookupKey], id: `price_new_${lookupKey}`, unit_amount: Number(body.get("unit_amount")), metadata };
      catalog[lookupKey].lookup_key = null;
      allPrices[replacement.id] = replacement;
      catalog[lookupKey] = replacement;
      response.end(JSON.stringify(postResult ?? replacement));
      return;
    }
    response.writeHead(404);
    response.end("{}");
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const baseUrl = `http://127.0.0.1:${server.address().port}`;
  const directory = await mkdtemp(path.join(os.tmpdir(), "ngp-stripe-price-update-"));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const appsettingsPath = path.join(directory, "appsettings.json");
  await writeFile(appsettingsPath, JSON.stringify({
    [section]: { Prices: configured ?? Object.fromEntries(Object.keys(prices).map((sku) => [sku, { Price: 20 }])) },
    Billing: { Stripe: { SecretKey: "config-secret", ApiUrl: baseUrl } },
  }));
  return { requests, allPrices, products, directory, appsettingsPath, args: ["--appsettings", appsettingsPath, "--secret-key", "test-secret", "--base-url", baseUrl] };
}
