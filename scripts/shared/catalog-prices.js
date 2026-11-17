const { readFile } = require("node:fs/promises");

async function loadConfiguration(appsettingsPath) {
  let content;
  try {
    content = await readFile(appsettingsPath, "utf8");
  } catch (error) {
    throw new Error(
      `Cannot read appsettings file '${appsettingsPath}': ${error.message}`,
    );
  }

  try {
    return JSON.parse(content);
  } catch (error) {
    throw new Error(
      `Invalid JSON in appsettings file '${appsettingsPath}': ${error.message}`,
    );
  }
}

function readConfiguredPrices(configuration, sectionName) {
  const priceSource = `${sectionName}.Prices`;
  const configuredPrices = configuration[sectionName]?.Prices;
  if (
    !configuredPrices ||
    typeof configuredPrices !== "object" ||
    Array.isArray(configuredPrices)
  ) {
    throw new Error(
      `${priceSource} must be a SKU-to-price object in the appsettings file.`,
    );
  }

  const normalizedSkus = new Set();
  const prices = Object.entries(configuredPrices).map(
    ([sku, priceConfiguration]) => {
      const normalizedSku = sku.trim();
      if (!normalizedSku) {
        throw new Error(`${priceSource} contains a blank SKU.`);
      }

      const skuIdentity = normalizedSku.toLowerCase();
      if (normalizedSkus.has(skuIdentity)) {
        throw new Error(
          `${priceSource} contains duplicate SKU '${normalizedSku}' after trimming and case normalization.`,
        );
      }

      normalizedSkus.add(skuIdentity);

      const price = priceConfiguration?.Price;
      if (typeof price !== "number" || !Number.isFinite(price) || price < 0) {
        throw new Error(
          `${priceSource}['${sku}'].Price must be a non-negative number.`,
        );
      }

      return [normalizedSku, price];
    },
  );

  if (prices.length === 0) {
    throw new Error(`${priceSource} does not contain any prices.`);
  }

  return prices;
}

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

function readZenmeterPrices(configuration) {
  return readConfiguredPrices(configuration, "Zenmeter");
}

module.exports = { loadConfiguration, readConfiguredPrices, readZentitlePrices, readZenmeterPrices };
