const assert = require("node:assert/strict");
const test = require("node:test");

const { readZenmeterPrices } = require("./update-fastspring-product-prices");

test("readZenmeterPrices returns every configured SKU and numeric price", () => {
  const prices = readZenmeterPrices({
    Zenmeter: {
      Prices: {
        "tier-monthly": { Price: 49 },
        "addon-onetime": { Price: 15.5 },
      },
    },
  });

  assert.deepEqual(prices, [
    ["tier-monthly", 49],
    ["addon-onetime", 15.5],
  ]);
});
