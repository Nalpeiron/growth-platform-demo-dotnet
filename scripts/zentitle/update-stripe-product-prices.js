#!/usr/bin/env node

const { runProductPriceUpdate } = require("../stripe/product-price-updater");
const { readZentitlePrices } = require("../shared/catalog-prices");

function validateZentitlePrice(price, sku) {
  const yearly = sku.endsWith("-yearly");
  if (yearly ? price.type !== "recurring" || price.recurring.interval !== "year" ||
      price.recurring.interval_count !== 1 : price.type !== "one_time") {
    throw new Error(`Expected ${yearly ? "yearly recurring" : "one-time perpetual"} Stripe Price.`);
  }
}

async function main(args = process.argv.slice(2)) {
  await runProductPriceUpdate({
    args,
    productName: "Zentitle",
    scriptPath: "scripts/zentitle/update-stripe-product-prices.js",
    priceSource: "Zentitle.Prices (yearly and perpetual SKUs)",
    readPrices: readZentitlePrices,
    validatePrice: validateZentitlePrice,
  });
}

if (require.main === module) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

module.exports = { main };
