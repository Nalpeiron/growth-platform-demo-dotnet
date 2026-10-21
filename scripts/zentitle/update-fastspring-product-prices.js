#!/usr/bin/env node

const {
  runProductPriceUpdate,
} = require("../fastspring/product-price-updater");

const { readZentitlePrices } = require("../shared/catalog-prices");

const ScriptPath = "scripts/zentitle/update-fastspring-product-prices.js";
const PriceSource = "Zentitle.Prices (yearly and perpetual SKUs)";
async function main(args = process.argv.slice(2)) {
  await runProductPriceUpdate({
    args,
    productName: "Zentitle",
    scriptPath: ScriptPath,
    priceSource: PriceSource,
    readPrices: readZentitlePrices,
  });
}

if (require.main === module) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

module.exports = {
  main,
  readZentitlePrices,
};
