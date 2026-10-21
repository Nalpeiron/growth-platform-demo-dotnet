const { createHash } = require("node:crypto");
const { readFile } = require("node:fs/promises");
const path = require("node:path");
const { loadConfiguration } = require("../shared/catalog-prices");

const DefaultBaseUrl = "https://api.stripe.com/";
const PreviousPriceKey = "demo_price_update_previous";

async function runProductPriceUpdate({
  args,
  productName,
  scriptPath,
  priceSource,
  readPrices,
  validatePrice = () => {},
}) {
  const options = parseOptions(args);
  if (options.help) {
    console.log(usageText(scriptPath, priceSource));
    return;
  }
  if (!options.appsettings?.trim()) {
    throw new Error(`--appsettings is required.\n\n${usageText(scriptPath, priceSource)}`);
  }

  const appsettingsPath = path.resolve(options.appsettings);
  const configuration = await loadConfiguration(appsettingsPath);
  let prices = readPrices(configuration).map(([sku, amount]) => [sku, toUnitAmount(amount)]);
  const replacements = options.cleanupLog ? await readReplacementLog(options.cleanupLog) : new Map();
  for (const sku of replacements.keys()) {
    if (!prices.some(([configuredSku]) => configuredSku === sku)) {
      throw new Error(`Cleanup log SKU '${sku}' is not in ${priceSource}. Use a log for this catalog only.`);
    }
  }
  if (options.cleanupLog) prices = prices.filter(([sku]) => replacements.has(sku));
  const settings = configuration.Billing?.Stripe ?? {};
  const secretKey = options.secretKey ?? process.env.STRIPE_SECRET_KEY ?? settings.SecretKey;
  if (typeof secretKey !== "string" || !secretKey.trim()) {
    throw new Error("--secret-key, STRIPE_SECRET_KEY or Billing.Stripe.SecretKey is required.");
  }
  const client = new StripeClient(options.baseUrl ?? settings.ApiUrl ?? DefaultBaseUrl, secretKey);

  console.log(`Stripe product price update: ${productName}`);
  console.log(`Prices: ${appsettingsPath} (${priceSource}); currency: USD`);
  const updates = [];
  const errors = [];
  for (const [sku, configuredAmount] of prices) {
    try {
      const price = await client.getPrice(sku);
      validatePriceForUpdate(price, sku);
      validatePrice(price, sku);
      const unitAmount = options.cleanupLog ? price.unit_amount : configuredAmount;
      const previousIds = new Set();
      if (price.metadata?.[PreviousPriceKey]) previousIds.add(price.metadata[PreviousPriceKey]);
      const logged = replacements.get(sku);
      if (logged) {
        if (logged.newId !== price.id) throw new Error("Cleanup log replacement is not the current SKU Price.");
        previousIds.add(logged.oldId);
      }
      const previousPrices = [];
      for (const id of previousIds) {
        const previous = await client.getPriceById(id);
        validatePreviousPrice(previous, price);
        if (previous.active) previousPrices.push(previous);
      }
      if (unitAmount !== price.unit_amount || previousPrices.length) {
        await client.getProduct(price.product);
      }
      updates.push({ sku, unitAmount, price, previousPrices });
    } catch (error) {
      errors.push(`${sku}: ${error.message}`);
    }
  }
  if (errors.length) {
    throw new Error(`Preflight failed. No Stripe prices were updated.\n${errors.join("\n")}`);
  }

  for (const { sku, price, unitAmount, previousPrices } of updates) {
    console.log(`- ${sku}: ${(price.unit_amount / 100).toFixed(2)} USD -> ${(unitAmount / 100).toFixed(2)} USD`);
    for (const previous of previousPrices) console.log(`  Archive previously replaced Price: ${previous.id}`);
    if (price.unit_amount !== unitAmount) console.log(`  Create replacement, then archive ${price.id}`);
  }
  if (options.dryRun) {
    console.log("Dry run complete. No Stripe prices were updated.");
    return;
  }

  let completed = 0;
  let archived = 0;
  for (const { sku, price, unitAmount, previousPrices } of updates) {
    try {
      for (const previous of previousPrices) {
        await client.archiveReplacedPrice(previous.id, price);
        archived++;
        console.log(`Archived ${previous.id}`);
      }
      if (price.unit_amount === unitAmount) continue;
      const replacement = await client.replacePrice(price, unitAmount);
      console.log(`Updated ${sku}: ${price.id} -> ${replacement.id}`);
      await client.archiveReplacedPrice(price.id, replacement);
      archived++;
      completed++;
      console.log(`Archived ${price.id}`);
    } catch (error) {
      throw new Error(
        `Stopped at '${sku}' after ${completed} completed update(s). Earlier updates are not rolled back. ` +
        `A replacement may already exist; rerun to finish its archive step without creating another Price. ${error.message}`,
      );
    }
  }
  console.log(`Done. Updated: ${completed}; unchanged: ${updates.length - completed}; archived: ${archived}.`);
}

async function readReplacementLog(filePath) {
  const text = await readFile(path.resolve(filePath), "utf8");
  const replacements = new Map();
  for (const line of text.split(/\r?\n/)) {
    const match = /^Updated (.+): (price_[a-zA-Z0-9_]+) -> (price_[a-zA-Z0-9_]+)$/.exec(line.trim());
    if (!match) {
      if (line.trim().startsWith("Updated ")) throw new Error("Invalid Updated line in cleanup log.");
      continue;
    }
    const [, sku, oldId, newId] = match;
    if (replacements.has(sku)) throw new Error(`Duplicate SKU '${sku}' in cleanup log.`);
    replacements.set(sku, { oldId, newId });
  }
  if (!replacements.size) throw new Error("Cleanup log has no 'Updated SKU: price_OLD -> price_NEW' lines.");
  return replacements;
}

function validatePreviousPrice(previous, replacement) {
  if (previous.id === replacement.id || previous.lookup_key ||
      previous.product !== replacement.product || previous.currency !== replacement.currency ||
      previous.type !== replacement.type || previous.billing_scheme !== replacement.billing_scheme ||
      ["interval", "interval_count", "usage_type", "trial_period_days"].some(
        (field) => (previous.recurring?.[field] ?? null) !== (replacement.recurring?.[field] ?? null))) {
    throw new Error(`Cannot archive '${previous.id}': it is not a compatible, unkeyed predecessor of '${replacement.id}'.`);
  }
}

function toUnitAmount(amount) {
  const cents = amount * 100;
  const rounded = Math.round(cents);
  if (!Number.isFinite(amount) || amount < 0 || !Number.isSafeInteger(rounded) ||
      Number(amount.toFixed(2)) !== amount || rounded > 99999999) {
    throw new Error(`Invalid USD price '${amount}': expected at most two decimal places and at most 999999.99.`);
  }
  return rounded;
}

function validatePriceForUpdate(price, sku) {
  if (!price || !price.id || price.lookup_key !== sku || price.active !== true) {
    throw new Error("Expected exactly one active Price with lookup_key equal to the SKU.");
  }
  if (typeof price.product !== "string" || !price.product) {
    throw new Error("Price is missing its product id.");
  }
  if (price.currency !== "usd") {
    throw new Error(`Expected USD, got '${price.currency}'.`);
  }
  if (price.billing_scheme !== "per_unit" || !Number.isSafeInteger(price.unit_amount) ||
      price.unit_amount < 0 || price.custom_unit_amount || price.transform_quantity || price.tiers_mode) {
    throw new Error("Only fixed per-unit prices in whole cents without quantity transforms are supported.");
  }
  // Explicitly expand currency_options when reading: silently dropping another currency is unsafe.
  if (price.currency_options && Object.keys(price.currency_options).some((currency) => currency !== "usd")) {
    throw new Error("Multi-currency Prices are not supported.");
  }
  if (price.type === "recurring") {
    const recurring = price.recurring;
    if (!recurring || recurring.usage_type !== "licensed" || recurring.meter ||
        !["day", "week", "month", "year"].includes(recurring.interval) ||
        !Number.isSafeInteger(recurring.interval_count) || recurring.interval_count < 1) {
      throw new Error("Only licensed recurring Prices with a valid billing interval are supported.");
    }
    if (recurring.trial_period_days != null) {
      throw new Error("Legacy Price-level trial periods cannot be copied by the Price creation API.");
    }
  } else if (price.type !== "one_time" || price.recurring) {
    throw new Error("Expected a one-time or licensed recurring Price.");
  }
}

class StripeClient {
  constructor(baseUrl, secretKey) {
    this.baseUrl = new URL(baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`);
    this.secretKey = secretKey;
  }

  async getPrice(sku) {
    const query = new URLSearchParams({
      active: "true",
      "lookup_keys[]": sku,
      limit: "2",
      "expand[]": "data.currency_options",
    });
    const body = await this.request("GET", `v1/prices?${query}`);
    if (!Array.isArray(body.data) || body.has_more !== false || body.data.length !== 1) {
      throw new Error("Expected exactly one active Price with lookup_key equal to the SKU.");
    }
    return body.data[0];
  }

  async getPriceById(id) {
    const price = await this.request("GET", `v1/prices/${encodeURIComponent(id)}`);
    if (price.id !== id || typeof price.active !== "boolean") throw new Error(`Invalid Price response for '${id}'.`);
    return price;
  }

  async getProduct(id) {
    const product = await this.request("GET", `v1/products/${encodeURIComponent(id)}`);
    if (product.id !== id || product.active !== true) throw new Error(`Product '${id}' is missing or inactive.`);
    return product;
  }

  async archiveReplacedPrice(previousId, replacement) {
    // Recheck identities before any archive: never infer predecessors from names or amounts.
    const current = await this.getPrice(replacement.lookup_key);
    if (current.id !== replacement.id || current.active !== true) {
      throw new Error("SKU Price changed during the run. No previous Price was archived.");
    }
    const previous = await this.getPriceById(previousId);
    validatePreviousPrice(previous, current);
    if (!previous.active) return;
    const product = await this.getProduct(current.product);
    if (product.default_price === previous.id) {
      const updated = await this.request("POST", `v1/products/${encodeURIComponent(product.id)}`,
        new URLSearchParams({ default_price: current.id }));
      if (updated.id !== product.id || updated.default_price !== current.id) {
        throw new Error("Could not verify the product's replacement default Price; the old Price was not archived.");
      }
    }
    const archived = await this.request("POST", `v1/prices/${encodeURIComponent(previous.id)}`,
      new URLSearchParams({ active: "false" }));
    if (archived.id !== previous.id || archived.active !== false) {
      throw new Error(`Could not verify archival of '${previous.id}'.`);
    }
  }

  async replacePrice(price, unitAmount) {
    const body = new URLSearchParams({
      product: price.product,
      currency: "usd",
      unit_amount: String(unitAmount),
      billing_scheme: "per_unit",
      active: "true",
      lookup_key: price.lookup_key,
      transfer_lookup_key: "true",
    });
    if (price.tax_behavior) body.set("tax_behavior", price.tax_behavior);
    if (price.nickname) body.set("nickname", price.nickname);
    for (const [key, value] of Object.entries(price.metadata ?? {})) {
      body.set(`metadata[${key}]`, value);
    }
    body.set(`metadata[${PreviousPriceKey}]`, price.id);
    if (price.recurring) {
      for (const field of ["interval", "interval_count", "usage_type"]) {
        if (price.recurring[field] != null) body.set(`recurring[${field}]`, String(price.recurring[field]));
      }
    }
    // A retry for the same source Price and request must not create a second replacement.
    const idempotencyKey = "demo-price-update-" + createHash("sha256")
      .update(`${price.id}:${body}`).digest("hex");
    const replacement = await this.request("POST", "v1/prices", body, idempotencyKey);
    if (!replacement.id || replacement.id === price.id || replacement.lookup_key !== price.lookup_key ||
        replacement.unit_amount !== unitAmount || replacement.product !== price.product ||
        replacement.currency !== "usd" || replacement.active !== true) {
      throw new Error("Stripe returned an unexpected replacement Price; verify the result before retrying.");
    }
    return replacement;
  }

  async request(method, requestPath, body, idempotencyKey) {
    const headers = { Authorization: `Bearer ${this.secretKey}`, Accept: "application/json" };
    if (body) headers["Content-Type"] = "application/x-www-form-urlencoded";
    if (idempotencyKey) headers["Idempotency-Key"] = idempotencyKey;
    const response = await fetch(new URL(requestPath, this.baseUrl), {
      method, headers, body, redirect: "error", signal: AbortSignal.timeout(30000),
    });
    const text = await response.text();
    let result;
    try {
      result = JSON.parse(text);
    } catch {
      throw new Error(`Stripe ${method} failed to return JSON (HTTP ${response.status}).`);
    }
    if (!response.ok) {
      const message = String(result?.error?.message ?? response.statusText).replaceAll(this.secretKey, "[redacted]");
      throw new Error(`Stripe ${method} failed (HTTP ${response.status}): ${message}`);
    }
    if (!result || typeof result !== "object") throw new Error("Stripe returned an invalid response.");
    return result;
  }
}

function parseOptions(args) {
  const valueOptions = new Set(["appsettings", "secret-key", "base-url", "cleanup-log"]);
  const options = {};
  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === "--help" || arg === "-h") { options.help = true; continue; }
    if (arg === "--dry-run") { options.dryRun = true; continue; }
    const match = /^--([^=]+)(?:=(.*))?$/.exec(arg);
    if (!match || !valueOptions.has(match[1])) throw new Error(`Unknown option '${arg}'.`);
    const key = match[1].replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
    if (Object.hasOwn(options, key)) throw new Error(`Option '--${match[1]}' was provided more than once.`);
    const value = match[2] ?? args[++i];
    if (!value?.trim() || value.startsWith("--")) throw new Error(`Missing value for --${match[1]}.`);
    options[key] = value;
  }
  return options;
}

function usageText(scriptPath, priceSource) {
  return [
    `Usage: node ${scriptPath} --appsettings <file> [--dry-run]`,
    `Reads ${priceSource} in USD from the selected JSON file (no configuration layering).`,
    "Each SKU must already have one active Stripe Price with a matching lookup_key.",
    "Changed amounts create replacement Prices, transfer lookup keys and archive the old Prices.",
    "Product default_price follows its replacement when needed; existing subscriptions keep their prices.",
    "Archiving also disables payment links that use the old Price. Archived Prices remain in history.",
    "",
    "Options:",
    "  --secret-key <key>  Overrides STRIPE_SECRET_KEY, then Billing.Stripe.SecretKey",
    `  --base-url <url>    Overrides Billing.Stripe.ApiUrl; defaults to ${DefaultBaseUrl} (without /v1)`,
    "  --cleanup-log <file>  Only archive predecessors from an earlier run's Updated lines; creates no Prices",
    "  --dry-run           Validates the complete catalog without writing to Stripe",
    "  --help, -h          Prints this help text",
  ].join("\n");
}

module.exports = { runProductPriceUpdate, toUnitAmount };
