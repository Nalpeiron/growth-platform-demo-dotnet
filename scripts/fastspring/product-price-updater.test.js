const assert = require("node:assert/strict");
const { mkdtemp, rm, writeFile } = require("node:fs/promises");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");

const {
  FastSpringClient,
  readConfiguredPrices,
  runProductPriceUpdate,
} = require("./product-price-updater");

test("readConfiguredPrices validates the selected configuration section", () => {
  assert.deepEqual(
    readConfiguredPrices(
      { Product: { Prices: { sku: { Price: 10 } } } },
      "Product",
    ),
    [["sku", 10]],
  );
  assert.throws(
    () =>
      readConfiguredPrices(
        { Product: { Prices: { sku: { Price: -1 } } } },
        "Product",
      ),
    /Product\.Prices\['sku'\]\.Price must be a non-negative number/,
  );
  assert.throws(
    () =>
      readConfiguredPrices(
        {
          Product: {
            Prices: {
              sku: { Price: 10 },
              " SKU ": { Price: 20 },
            },
          },
        },
        "Product",
      ),
    /duplicate SKU 'SKU' after trimming and case normalization/,
  );
});

test("runProductPriceUpdate rejects unknown options before reading configuration", async () => {
  let readPricesCalled = false;

  await assert.rejects(
    () =>
      runProductPriceUpdate({
        args: ["--dry-rnu", "true"],
        productName: "Test",
        scriptPath: "scripts/test.js",
        priceSource: "Test.Prices",
        readPrices: () => {
          readPricesCalled = true;
          return [["sku", 10]];
        },
      }),
    /Unknown option '--dry-rnu'/,
  );
  assert.equal(readPricesCalled, false);
});

test("runProductPriceUpdate completes the full preflight before any POST", async (t) => {
  const appsettingsPath = await createAppsettings(t, {
    Test: {
      Prices: {
        "existing-sku": { Price: 20 },
        "missing-sku": { Price: 30 },
      },
    },
  });
  const api = await startFastSpringApi(t, {
    products: { "existing-sku": 10 },
  });

  await assert.rejects(
    () => runTestProductPriceUpdate(appsettingsPath, api.baseUrl),
    /Preflight failed\. No FastSpring prices were updated/,
  );

  assert.deepEqual(
    api.requests.map(({ method, productPath }) => [method, productPath]),
    [
      ["GET", "existing-sku"],
      ["GET", "missing-sku"],
    ],
  );
});

test("runProductPriceUpdate dry-run never sends a POST", async (t) => {
  const appsettingsPath = await createAppsettings(t, {
    Test: { Prices: { sku: { Price: 20 } } },
  });
  const api = await startFastSpringApi(t, { products: { sku: 10 } });

  await runTestProductPriceUpdate(appsettingsPath, api.baseUrl, ["--dry-run"]);

  assert.deepEqual(
    api.requests.map(({ method }) => method),
    ["GET"],
  );
});

test("runProductPriceUpdate skips unchanged prices", async (t) => {
  const appsettingsPath = await createAppsettings(t, {
    Test: { Prices: { sku: { Price: 20 } } },
  });
  const api = await startFastSpringApi(t, { products: { sku: 20 } });

  await runTestProductPriceUpdate(appsettingsPath, api.baseUrl);

  assert.deepEqual(
    api.requests.map(({ method }) => method),
    ["GET"],
  );
});

test("runProductPriceUpdate reports a failed FastSpring update operation", async (t) => {
  const appsettingsPath = await createAppsettings(t, {
    Test: { Prices: { sku: { Price: 20 } } },
  });
  const api = await startFastSpringApi(t, {
    products: { sku: 10 },
    postResult: "error",
  });

  await assert.rejects(
    () => runTestProductPriceUpdate(appsettingsPath, api.baseUrl),
    /FastSpring failed to update 'sku'/,
  );

  assert.deepEqual(
    api.requests.map(({ method }) => method),
    ["GET", "POST"],
  );
});

test("updateProductPrice preserves pricing configuration and other currencies", async (t) => {
  let capturedRequestBody;
  const server = http.createServer(async (request, response) => {
    const chunks = [];
    for await (const chunk of request) {
      chunks.push(chunk);
    }

    capturedRequestBody = JSON.parse(Buffer.concat(chunks).toString("utf8"));
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(
      JSON.stringify({
        products: [
          {
            product: "tier-monthly",
            action: "product.updated",
            result: "success",
          },
        ],
      }),
    );
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => server.close());

  const address = server.address();
  const client = new FastSpringClient(
    `http://127.0.0.1:${address.port}/`,
    "api-user",
    "api-password",
  );

  await client.updateProductPrice(
    {
      product: "tier-monthly",
      display: { en: "Tier" },
      pricing: {
        interval: "month",
        intervalLength: 1,
        quantityBehavior: "hide",
        quantityDefault: 1,
        price: {
          USD: 0,
          EUR: 40,
        },
      },
    },
    "tier-monthly",
    "USD",
    49,
  );

  const updatedProduct = capturedRequestBody.products[0];
  assert.equal(updatedProduct.product, "tier-monthly");
  assert.deepEqual(updatedProduct.display, { en: "Tier" });
  assert.deepEqual(updatedProduct.pricing, {
    interval: "month",
    intervalLength: 1,
    quantityBehavior: "hide",
    quantityDefault: 1,
    price: {
      USD: 49,
      EUR: 40,
    },
  });
});

async function createAppsettings(t, configuration) {
  const directory = await mkdtemp(path.join(os.tmpdir(), "ngp-price-update-"));
  t.after(() => rm(directory, { recursive: true, force: true }));

  const appsettingsPath = path.join(directory, "appsettings.json");
  await writeFile(appsettingsPath, JSON.stringify(configuration), "utf8");
  return appsettingsPath;
}

async function startFastSpringApi(
  t,
  { products = {}, postResult = "success" } = {},
) {
  const requests = [];
  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, "http://127.0.0.1");
    if (request.method === "GET" && url.pathname.startsWith("/products/")) {
      const productPath = decodeURIComponent(url.pathname.slice(10));
      requests.push({ method: "GET", productPath });
      if (!Object.hasOwn(products, productPath)) {
        response.writeHead(404);
        response.end();
        return;
      }

      writeJson(response, {
        products: [
          {
            product: productPath,
            display: { en: productPath },
            pricing: { price: { USD: products[productPath] } },
          },
        ],
      });
      return;
    }

    if (request.method === "POST" && url.pathname === "/products") {
      const body = await readJsonBody(request);
      const productPath = body.products[0].product;
      requests.push({ method: "POST", productPath, body });
      writeJson(response, {
        products: [{ product: productPath, result: postResult }],
      });
      return;
    }

    response.writeHead(404);
    response.end();
  });

  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const address = server.address();
  return {
    baseUrl: `http://127.0.0.1:${address.port}/`,
    requests,
  };
}

function runTestProductPriceUpdate(appsettingsPath, baseUrl, extraArgs = []) {
  return runProductPriceUpdate({
    args: [
      "--appsettings",
      appsettingsPath,
      "--api-username",
      "api-user",
      "--api-password",
      "api-password",
      "--base-url",
      baseUrl,
      ...extraArgs,
    ],
    productName: "Test",
    scriptPath: "scripts/test.js",
    priceSource: "Test.Prices",
    readPrices: (configuration) => readConfiguredPrices(configuration, "Test"),
  });
}

async function readJsonBody(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }

  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

function writeJson(response, body) {
  response.writeHead(200, { "Content-Type": "application/json" });
  response.end(JSON.stringify(body));
}
