#!/usr/bin/env node

const { runProductPriceUpdate } = require("../stripe/product-price-updater");
const { readZenmeterPrices } = require("../shared/catalog-prices");

async function main(args = process.argv.slice(2)) {
  await runProductPriceUpdate({
    args,
    productName: "Zenmeter",
    scriptPath: "scripts/zenmeter/update-stripe-product-prices.js",
    priceSource: "Zenmeter.Prices",
    readPrices: readZenmeterPrices,
  });
}

if (require.main === module) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

module.exports = { main };
