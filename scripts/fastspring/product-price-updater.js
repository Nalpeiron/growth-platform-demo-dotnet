const { readFile } = require("node:fs/promises");
const path = require("node:path");

const DefaultBaseUrl = "https://api.fastspring.com/";
const DefaultCurrency = "USD";

async function runProductPriceUpdate({
  args,
  productName,
  scriptPath,
  priceSource,
  readPrices,
}) {
  const options = parseOptions(args);
  if (options.help) {
    printUsage(scriptPath, priceSource);
    return;
  }

  requireOption(
    options,
    "appsettings",
    "--appsettings",
    scriptPath,
    priceSource,
  );

  const appsettingsPath = path.resolve(options.appsettings);
  const configuration = await loadConfiguration(appsettingsPath);
  const prices = readPrices(configuration);
  const fastSpringSettings = configuration.Billing?.FastSpring ?? {};
  const apiUsername = options.apiUsername ?? fastSpringSettings.ApiUsername;
  const apiPassword = options.apiPassword ?? fastSpringSettings.ApiPassword;
  const baseUrl =
    options.baseUrl ?? fastSpringSettings.ApiUrl ?? DefaultBaseUrl;
  const currency = (options.currency ?? DefaultCurrency).toUpperCase();

  requireValue(
    apiUsername,
    "--api-username or Billing.FastSpring.ApiUsername",
    scriptPath,
    priceSource,
  );
  requireValue(
    apiPassword,
    "--api-password or Billing.FastSpring.ApiPassword",
    scriptPath,
    priceSource,
  );
  validateCurrency(currency);

  console.log("FastSpring product price update");
  console.log(`Catalog: ${productName}`);
  console.log(`Prices: ${appsettingsPath} (${priceSource})`);
  console.log(`Currency: ${currency}`);
  console.log("");

  const client = new FastSpringClient(
    baseUrl || DefaultBaseUrl,
    apiUsername,
    apiPassword,
  );
  const updates = [];
  const missingProductPaths = [];

  for (const [productPath, targetPrice] of prices) {
    const product = await client.getProduct(productPath);
    if (!product) {
      missingProductPaths.push(productPath);
      continue;
    }

    validateProductForUpdate(product, productPath);
    updates.push({
      product,
      productPath,
      targetPrice,
      currentPrice: readCurrentPrice(product, currency),
    });
  }

  if (missingProductPaths.length > 0) {
    console.error(
      `The following ${productName} SKUs do not exist in FastSpring:`,
    );
    for (const productPath of missingProductPaths) {
      console.error(`- ${productPath}`);
    }

    throw new Error("Preflight failed. No FastSpring prices were updated.");
  }

  console.log(`Validated ${updates.length} FastSpring product(s):`);
  for (const update of updates) {
    console.log(
      `- ${update.productPath}: ${formatPrice(update.currentPrice, currency)} -> ${formatPrice(update.targetPrice, currency)}`,
    );
  }

  if (options.dryRun) {
    console.log("");
    console.log("Dry run complete. No FastSpring prices were updated.");
    return;
  }

  const changedUpdates = updates.filter(
    (update) => update.currentPrice !== update.targetPrice,
  );
  console.log("");
  console.log(`Updating ${changedUpdates.length} changed price(s).`);

  for (const update of changedUpdates) {
    await client.updateProductPrice(
      update.product,
      update.productPath,
      currency,
      update.targetPrice,
    );
    console.log(
      `Updated ${update.productPath} to ${formatPrice(update.targetPrice, currency)}`,
    );
  }

  const unchangedCount = updates.length - changedUpdates.length;
  console.log(
    `Done. Updated: ${changedUpdates.length}; unchanged: ${unchangedCount}.`,
  );
}

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

class FastSpringClient {
  constructor(baseUrl, username, password) {
    this.baseUrl = new URL(baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`);
    this.authorization = `Basic ${Buffer.from(`${username}:${password}`, "utf8").toString("base64")}`;
  }

  async getProduct(productPath) {
    const body = await this.request(
      "GET",
      `products/${encodeURIComponent(productPath)}`,
      { allowNotFound: true },
    );
    return normalizeProducts(body)[0] ?? null;
  }

  async updateProductPrice(product, productPath, currency, targetPrice) {
    const pricing = {
      ...product.pricing,
      price: {
        ...(isObject(product.pricing.price) ? product.pricing.price : {}),
        [currency]: targetPrice,
      },
    };
    const body = await this.request("POST", "products", {
      body: {
        products: [
          {
            product: productPath,
            display: product.display,
            pricing,
          },
        ],
      },
    });

    ensureSuccessfulProductOperation(body, productPath);
  }

  async request(method, requestPath, options = {}) {
    const headers = {
      Authorization: this.authorization,
      "User-Agent": "NalpeironGrowthPlatformDemo-FastSpring-Price-Update/1.0",
      Accept: "application/json",
    };
    if (options.body) {
      headers["Content-Type"] = "application/json";
    }

    const response = await fetch(new URL(requestPath, this.baseUrl), {
      method,
      headers,
      body: options.body ? JSON.stringify(options.body) : undefined,
    });

    const responseText = await response.text();
    if (options.allowNotFound && response.status === 404) {
      return null;
    }

    if (!response.ok) {
      throw new Error(
        `${method} ${requestPath} failed with ${response.status} ${response.statusText}: ${responseText}`,
      );
    }

    if (!responseText.trim()) {
      return null;
    }

    try {
      return JSON.parse(responseText);
    } catch (error) {
      throw new Error(
        `${method} ${requestPath} returned invalid JSON: ${error.message}`,
      );
    }
  }
}

function validateProductForUpdate(product, expectedProductPath) {
  const actualProductPath = getProductPath(product);
  if (actualProductPath && actualProductPath !== expectedProductPath) {
    throw new Error(
      `FastSpring returned product '${actualProductPath}' when '${expectedProductPath}' was requested.`,
    );
  }

  if (!isObject(product.display)) {
    throw new Error(
      `FastSpring product '${expectedProductPath}' does not contain the display object required for an update.`,
    );
  }

  if (!isObject(product.pricing)) {
    throw new Error(
      `FastSpring product '${expectedProductPath}' does not contain the pricing object required for an update.`,
    );
  }
}

function ensureSuccessfulProductOperation(body, expectedProductPath) {
  const operations = normalizeProducts(body);
  if (operations.length === 0) {
    return;
  }

  const failedOperation = operations.find(
    (operation) =>
      typeof operation === "object" &&
      operation !== null &&
      typeof operation.result === "string" &&
      operation.result.toLowerCase() !== "success",
  );
  if (failedOperation) {
    throw new Error(
      `FastSpring failed to update '${expectedProductPath}': ${JSON.stringify(failedOperation)}`,
    );
  }
}

function normalizeProducts(body) {
  if (!body) {
    return [];
  }

  if (Array.isArray(body)) {
    return body;
  }

  if (Array.isArray(body.products)) {
    return body.products;
  }

  if (body.product && typeof body.product === "object") {
    return [body.product];
  }

  return [];
}

function parseOptions(args) {
  const valueOptions = new Set([
    "appsettings",
    "api-username",
    "api-password",
    "base-url",
    "currency",
  ]);
  const options = {};
  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === "--help" || arg === "-h") {
      options.help = true;
      continue;
    }

    if (arg === "--dry-run") {
      options.dryRun = true;
      continue;
    }

    if (!arg.startsWith("--")) {
      throw new Error(`Unexpected argument '${arg}'.`);
    }

    const option = arg.slice(2);
    const separatorIndex = option.indexOf("=");
    const rawKey =
      separatorIndex === -1 ? option : option.slice(0, separatorIndex);
    if (!valueOptions.has(rawKey)) {
      throw new Error(`Unknown option '--${rawKey}'.`);
    }

    const inlineValue =
      separatorIndex === -1 ? null : option.slice(separatorIndex + 1);
    const key = toCamelCase(rawKey);
    if (Object.hasOwn(options, key)) {
      throw new Error(`Option '--${rawKey}' was provided more than once.`);
    }

    const value = inlineValue ?? args[++i];
    if (!value || value.startsWith("--")) {
      throw new Error(`Missing value for --${rawKey}.`);
    }

    options[key] = value;
  }

  return options;
}

function requireOption(options, name, usageName, scriptPath, priceSource) {
  requireValue(options[name], usageName, scriptPath, priceSource);
}

function requireValue(value, usageName, scriptPath, priceSource) {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(
      `${usageName} is required.\n\n${usageText(scriptPath, priceSource)}`,
    );
  }
}

function validateCurrency(currency) {
  if (!/^[A-Z]{3}$/.test(currency)) {
    throw new Error(
      "--currency must be a three-letter ISO currency code, for example USD.",
    );
  }
}

function toCamelCase(value) {
  return value.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
}

function printUsage(scriptPath, priceSource) {
  console.log(usageText(scriptPath, priceSource));
}

function usageText(scriptPath, priceSource) {
  return [
    "Usage:",
    `  node ${scriptPath} \\`,
    "    --appsettings src/NalpeironGrowthPlatformDemo/appsettings.json \\",
    "    --api-username <username> \\",
    "    --api-password <password>",
    "",
    `The script uses ${priceSource} entries selected by the product-specific entrypoint.`,
    "Billing.FastSpring.ApiUsername and ApiPassword from appsettings are used when command options are omitted.",
    "",
    "Optional:",
    `  --base-url <url>  Uses Billing.FastSpring.ApiUrl, then defaults to ${DefaultBaseUrl}`,
    `  --currency <code>  Defaults to ${DefaultCurrency}`,
    "  --dry-run          Validates and prints changes without updating FastSpring",
    "  --help, -h         Prints this help text",
  ].join("\n");
}

function getProductPath(product) {
  if (typeof product === "string") {
    return product;
  }

  return product.product ?? product.path ?? product.id ?? "";
}

function readCurrentPrice(product, currency) {
  const price = product.pricing?.price?.[currency];
  return typeof price === "number" && Number.isFinite(price) ? price : null;
}

function formatPrice(price, currency) {
  return price === null
    ? `<not configured> ${currency}`
    : `${price} ${currency}`;
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

module.exports = {
  FastSpringClient,
  readConfiguredPrices,
  runProductPriceUpdate,
};
