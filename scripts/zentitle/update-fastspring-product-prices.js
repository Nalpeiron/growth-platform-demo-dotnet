#!/usr/bin/env node

const {
  readConfiguredPrices,
  runProductPriceUpdate,
} = require("../fastspring/product-price-updater");

const ScriptPath = "scripts/zentitle/update-fastspring-product-prices.js";
const PriceSource = "Zentitle.Prices (yearly SKUs only)";
const YearlySkuSuffix = "-yearly";
const PerpetualSkuSuffix = "-perpetual";

function readZentitleYearlyPrices(configuration) {
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

  const yearlyPrices = prices.filter(([sku]) => sku.endsWith(YearlySkuSuffix));
  if (yearlyPrices.length === 0) {
    throw new Error(
      `Zentitle.Prices does not contain any '${YearlySkuSuffix}' SKUs for FastSpring.`,
    );
  }

  return yearlyPrices;
}

async function main(args = process.argv.slice(2)) {
  await runProductPriceUpdate({
    args,
    productName: "Zentitle yearly",
    scriptPath: ScriptPath,
    priceSource: PriceSource,
    readPrices: readZentitleYearlyPrices,
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
  readZentitleYearlyPrices,
};
