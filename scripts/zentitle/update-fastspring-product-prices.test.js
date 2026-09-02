const assert = require("node:assert/strict");
const { mkdtemp, rm, writeFile } = require("node:fs/promises");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");

const {
  main,
  readZentitleYearlyPrices,
} = require("./update-fastspring-product-prices");

test("readZentitleYearlyPrices returns yearly prices and excludes perpetual prices", () => {
  const prices = readZentitleYearlyPrices({
    Zentitle: {
      Prices: {
        "elevate-standard-yearly": { Price: 499 },
        "elevate-standard-perpetual": { Price: 999 },
        "elevate-premium-yearly": { Price: 749 },
        "elevate-premium-perpetual": { Price: 1499 },
      },
    },
  });

  assert.deepEqual(prices, [
    ["elevate-standard-yearly", 499],
    ["elevate-premium-yearly", 749],
  ]);
});

test("readZentitleYearlyPrices rejects ambiguous SKU periods", () => {
  assert.throws(
    () =>
      readZentitleYearlyPrices({
        Zentitle: {
          Prices: {
            "elevate-standard": { Price: 499 },
          },
        },
      }),
    /Cannot determine the Zentitle billing period.*elevate-standard/,
  );
});

test("readZentitleYearlyPrices requires at least one yearly SKU", () => {
  assert.throws(
    () =>
      readZentitleYearlyPrices({
        Zentitle: {
          Prices: {
            "elevate-standard-perpetual": { Price: 999 },
          },
        },
      }),
    /does not contain any '-yearly' SKUs/,
  );
});

test("Zentitle entrypoint never sends perpetual SKUs to FastSpring", async (t) => {
  const directory = await mkdtemp(
    path.join(os.tmpdir(), "ngp-zentitle-price-update-"),
  );
  t.after(() => rm(directory, { recursive: true, force: true }));
  const appsettingsPath = path.join(directory, "appsettings.json");
  await writeFile(
    appsettingsPath,
    JSON.stringify({
      Zentitle: {
        Prices: {
          "elevate-standard-yearly": { Price: 499 },
          "elevate-standard-perpetual": { Price: 999 },
        },
      },
    }),
    "utf8",
  );

  const requestedProductPaths = [];
  const server = http.createServer((request, response) => {
    if (request.method !== "GET" || !request.url.startsWith("/products/")) {
      response.writeHead(405);
      response.end();
      return;
    }

    const productPath = decodeURIComponent(
      new URL(request.url, "http://127.0.0.1").pathname.slice(10),
    );
    requestedProductPaths.push(productPath);
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(
      JSON.stringify({
        products: [
          {
            product: productPath,
            display: { en: productPath },
            pricing: { price: { USD: 499 } },
          },
        ],
      }),
    );
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const address = server.address();

  await main([
    "--appsettings",
    appsettingsPath,
    "--api-username",
    "api-user",
    "--api-password",
    "api-password",
    "--base-url",
    `http://127.0.0.1:${address.port}/`,
    "--dry-run",
  ]);

  assert.deepEqual(requestedProductPaths, ["elevate-standard-yearly"]);
});
