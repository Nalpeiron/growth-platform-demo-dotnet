const assert = require("node:assert/strict");
const { mkdtemp, rm, writeFile } = require("node:fs/promises");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");

const {
  main,
  readZentitlePrices,
} = require("./update-fastspring-product-prices");

test("readZentitlePrices returns yearly and perpetual prices", () => {
  const prices = readZentitlePrices({
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
    ["elevate-standard-perpetual", 999],
    ["elevate-premium-yearly", 749],
    ["elevate-premium-perpetual", 1499],
  ]);
});

test("readZentitlePrices rejects ambiguous SKU periods", () => {
  assert.throws(
    () =>
      readZentitlePrices({
        Zentitle: {
          Prices: {
            "elevate-standard": { Price: 499 },
          },
        },
      }),
    /Cannot determine the Zentitle billing period.*elevate-standard/,
  );
});

test("readZentitlePrices accepts a perpetual-only catalog", () => {
  const prices = readZentitlePrices({
    Zentitle: {
      Prices: {
        "elevate-standard-perpetual": { Price: 999 },
      },
    },
  });

  assert.deepEqual(prices, [["elevate-standard-perpetual", 999]]);
});

test("Zentitle entrypoint previews and updates yearly and perpetual prices", async (t) => {
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
  const updatedProducts = [];
  const server = http.createServer(async (request, response) => {
    if (request.method === "POST" && request.url === "/products") {
      const chunks = [];
      for await (const chunk of request) {
        chunks.push(chunk);
      }
      const body = JSON.parse(Buffer.concat(chunks).toString("utf8"));
      updatedProducts.push(...body.products);
      response.writeHead(200, { "Content-Type": "application/json" });
      response.end(JSON.stringify({
        products: body.products.map(({ product }) => ({ product, result: "success" })),
      }));
      return;
    }

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
            pricing: { price: { USD: 100 } },
          },
        ],
      }),
    );
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const address = server.address();

  const args = [
    "--appsettings",
    appsettingsPath,
    "--api-username",
    "api-user",
    "--api-password",
    "api-password",
    "--base-url",
    `http://127.0.0.1:${address.port}/`,
  ];

  await main([...args, "--dry-run"]);

  assert.deepEqual(requestedProductPaths, [
    "elevate-standard-yearly",
    "elevate-standard-perpetual",
  ]);
  assert.deepEqual(updatedProducts, []);

  await main(args);

  assert.deepEqual(updatedProducts.map(({ product, pricing }) => [product, pricing.price.USD]), [
    ["elevate-standard-yearly", 499],
    ["elevate-standard-perpetual", 999],
  ]);
});
