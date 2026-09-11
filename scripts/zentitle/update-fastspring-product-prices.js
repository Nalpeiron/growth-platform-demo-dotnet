#!/usr/bin/env node

const {
  readConfiguredPrices,
  runProductPriceUpdate,
} = require("../fastspring/product-price-updater");

const ScriptPath = "scripts/zentitle/update-fastspring-product-prices.js";
const PriceSource = "Zentitle.Prices (yearly and perpetual SKUs)";
const YearlySkuSuffix = "-yearly";
const PerpetualSkuSuffix = "-perpetual";

function readZentitlePrices(configuration) {
  const prices = readConfiguredPrices(configuration, "Zentitle");
  const unsupportedSkus = prices
    .map(([sku]) => sku)
    .filter(
      (sku) =>
        !sku.endsWith(YearlySkuSuffix) && !sku.endsWith(PerpetualSkuSuffix),
    );
  if (unsupportedSkus.length > 0) {
    throw new Error(
      "Cannot determine the Zentitle billing period from SKU(s): " +
        `${unsupportedSkus.join(", ")}. Expected '${YearlySkuSuffix}' or '${PerpetualSkuSuffix}' suffix.`,
    );
  }

  return prices;
}

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
