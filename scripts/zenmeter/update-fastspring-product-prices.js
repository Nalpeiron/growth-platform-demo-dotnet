#!/usr/bin/env node

const {
  readConfiguredPrices,
  runProductPriceUpdate,
} = require("../fastspring/product-price-updater");

const ScriptPath = "scripts/zenmeter/update-fastspring-product-prices.js";
const PriceSource = "Zenmeter.Prices";

function readZenmeterPrices(configuration) {
  return readConfiguredPrices(configuration, "Zenmeter");
}

async function main(args = process.argv.slice(2)) {
  await runProductPriceUpdate({
    args,
    productName: "Zenmeter",
    scriptPath: ScriptPath,
    priceSource: PriceSource,
    readPrices: readZenmeterPrices,
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
  readZenmeterPrices,
};
