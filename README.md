# Nalpeiron Growth Platform E2E Demo

End-to-end integration demo for the **Nalpeiron Growth Platform**, built around the fictional
**Elevate** product family. The app contains two sales paths:

- **Elevate On-premise** (`/elevate/default`, `/elevate/fastspring`, `/elevate/stripe`) - Zentitle2
  licensing and entitlement demo.
- **Elevate SaaS** (`/elevate/saas/default`, `/elevate/saas/fastspring`,
  `/elevate/saas/stripe`) - Zenmeter subscription and metered usage demo.

The demo provisions real data through the live Nalpeiron Management API. There is no mock or
offline mode. Zenmeter product configuration comes from the live business model catalog API;
commercial SKU prices come from the billing system selected for the current route. Static Zentitle
and Zenmeter price entries are used by their `default` (`None`) routes.

- ASP.NET Core (net10), Blazor **Interactive Server** components.
- The browser stores only demo session ids in `localStorage`; server-side session stores hold
  the working demo state.
- Tailwind CSS via CDN (no frontend build step).
- Shared customer creation, token handling, API transport and error parsing across product slices.

> [!WARNING]
> This application performs write operations against the configured live tenant using server-side
> client credentials. Do not expose a configured instance to the public Internet without access
> control in front of it. Anyone who can use the UI can create customers, entitlements,
> subscriptions, activations, usage and add-ons in that tenant.

## Quick Start: Direct Flow Without Stripe Or FastSpring

Start with the direct `None` billing flow. It exercises the Nalpeiron integration without requiring
a Stripe account, FastSpring account, external checkout or billing webhook.

1. From the repository root, create the NuGet configuration used to restore the private
   `Zenmeter.Consumption.Client` package:

   ```powershell
   Copy-Item src/NalpeironGrowthPlatformDemo/nuget.config.template nuget.config
   ```

   On macOS or Linux, use `cp` instead of `Copy-Item`. Fill `nuget.config` with package registry
   credentials from `Administration -> API Credentials -> Package Registry` in Zentitle2.

2. Create the untracked local application settings file:

   ```powershell
   Copy-Item src/NalpeironGrowthPlatformDemo/appsettings.Local.example.json `
     src/NalpeironGrowthPlatformDemo/appsettings.Local.json
   ```

   Again, use `cp` on macOS or Linux. Fill in the `Nalpeiron` connection values,
   `Zentitle:ProductId` and `Zenmeter:BusinessModelId`.

3. Keep only the direct provider enabled. This is already the default in the example file:

   ```json
   {
     "Billing": {
       "DefaultBillingSystem": "None",
       "EnabledBillingSystems": ["None"]
     }
   }
   ```

   Stripe and FastSpring credentials may remain empty.

4. Start the application:

   ```bash
   dotnet run --project src/NalpeironGrowthPlatformDemo
   ```

5. Open the URL printed by Kestrel and use `/elevate/default` for Zentitle or
   `/elevate/saas/default` for Zenmeter.

`None` means direct provisioning without an external payment provider; it is not an offline or mock
mode. These flows still call the live Nalpeiron Management API and create data in the configured
tenant. Stripe and FastSpring buttons remain visible in the product picker, but their routes report
that the provider is disabled until it is added to `EnabledBillingSystems`.

## Architecture

The integration is organized as a **shared platform core + per-product slices**: one
Keycloak/OAuth flow, one tenant, shared customers, and per-product endpoint groups behind the
shared Management API transport.

```text
Nalpeiron/
  Generic/
    AccessTokenProvider        Keycloak client_credentials token, cached
    ManagementApiClient        Bearer + N-TenantId + N-Api-Version transport
    CustomersClient            shared customers resource
  Zentitle/
    ZentitleManagementClient   offerings, entitlements, activations, feature checkout/return
    Generated/                 generated Management API client and DTOs
    PricingCatalog             offerings + edition features -> pricing read model
    LicensingEnums             FeatureKind / BillingPeriod parsing
    UpgradePolicy              trial -> paid, paid -> next edition
  Zenmeter/
    ZenmeterManagementClient   business model catalog, compatible add-ons, subscriptions, users, meters, features, consumption
    Generated/                 generated Management API client and DTOs
    ZenmeterPricingCatalog     business model + compatible add-ons + selected billing prices -> pricing read model
    ZenmeterEnums              period, add-on type, renewal behavior parsing
Domain/
  ReferenceId                  _demo-z2- ids and length limits
Application/                   demo orchestration + read-model shaping (sliced per product)
  Shared/                      checkout guards, billing price facade, shared Stripe services and popup helpers
  Zentitle/
    ElevateDemoService         provider-aware Zentitle orchestration
    BillingProviders/          Zentitle provider capabilities, checkout and provisioning workflows
    ZentitleBillingStatusService asynchronous entitlement-group completion
    ZentitleDemoModels         Zentitle view models and service contract
    ZentitleDemoSession        Zentitle in-memory session store
  Zenmeter/
    ZenmeterDemoFacade         Zenmeter orchestration facade
    ZenmeterPurchaseService    customer creation, checkout and subscription completion
    BillingCheckoutService     per-session billing checkout provider resolver
    BillingCheckoutProviders/  direct no-billing, Stripe and FastSpring checkout providers
    ZenmeterDemoModels         Zenmeter view models and service contract
    Zenmeter*Projector         workspace DTO -> view-model projection
    Zenmeter*Policy            pricing/add-on/top-up selection rules
    ZenmeterWorkspaceBuilder   assembles the workspace read model
Components/
  Pages/                       Products, Zentitle pages, Zenmeter pages
  Zentitle/                    Zentitle workspace/pricing components
  Zenmeter/                    Zenmeter workspace/pricing components
Configuration/
  NalpeironOptions             shared connection
  ZentitleOptions              Zentitle product and prices
  ZenmeterOptions              Zenmeter business model id and static no-billing SKU prices
  DemoProductsOptions          product picker cards
tests/                         xUnit tests with local fakes
```

Magic API/config strings are parsed at boundaries. Zentitle flow uses `FeatureKind` and
`BillingPeriod`; Zenmeter flow uses `ZenmeterOfferingPeriod`, `ZenmeterAddonType` and
`ZenmeterRenewalBehavior`.

## Demo Flows

### Zentitle / Elevate On-premise

- **Pricing**: `GET /api/v1/offerings?productId=...&expand=edition,plan`, grouped by edition,
  plus `GET .../editions/{id}/features`. The default route uses `Zentitle:Prices`; FastSpring and
  Stripe resolve external prices by matching each offering SKU to the provider product path or
  active Stripe Price `lookup_key`.
- **Purchase**: creates a shared customer. The default route creates an entitlement group directly;
  FastSpring opens popup checkout and waits for Orion's `subscription.activated` webhook flow to
  provision the entitlement group. Stripe opens hosted Checkout and waits for Orion's initial
  `invoice.paid` subscription flow.
- **Workspace**: loads the live entitlement with product, attributes, features and offering.
- **Use a feature**: lazily creates an activation, then checks out or returns feature quantity.
- **Upgrade**: direct/default sessions use `UpgradePolicy` and change-offering. Upgrade is disabled
  for FastSpring- and Stripe-managed sessions until provider-side subscription changes are supported.
- **Reset demo**: clears the local session id/state only; live Zentitle data remains in tenant.

### Zenmeter / Elevate SaaS

- **Pricing**: loads the configured Zenmeter business model for tiers, offerings, included
  features, meters and feature-rate display. Compatible add-ons are fetched after a concrete base
  offering SKU is selected. Commercial prices come from the route's billing system; static
  `Zenmeter:Prices` entries are used only for direct no-billing provisioning.
- **Purchase**: creates a shared customer, then routes through the checkout provider selected on
  the product card. `None` creates the Zenmeter subscription directly by selected SKUs; external
  billing providers start checkout and complete after billing webhook provisioning. All paths
  create the Zenmeter subscription user from the first name, last name and email entered in the
  checkout form. The external user id is generated by the app from the entered name.
- **Workspace**: loads live subscription, features, meters, add-ons and user data. Workspace
  projectors reconcile meter grants, consumption snapshots and add-on source display.
- **Consume usage**: consumes a subscription feature for the checkout user and applies the returned
  usage snapshot locally for immediate workspace feedback.
- **Top up**: fetches compatible add-ons for the current base offering and adds the selected
  add-on to the live subscription. FastSpring one-time add-on purchases are attached to Zenmeter
  after paid-order verification using the actual FastSpring Order ID. FastSpring recurring add-ons
  update the existing FastSpring subscription; Orion provisions the Zenmeter add-on from the
  FastSpring webhook. Orion supports several recurring add-ons per subscription, so a recurring
  add-on stays purchasable after it is attached: buying it again raises the add-on quantity on the
  FastSpring subscription and Orion attaches another Zenmeter add-on instance.
- **Reset demo**: clears the local session id/state only; live Zenmeter data remains in tenant.

## Configuration

### Local NuGet Package Feed

The `Zenmeter.Consumption.Client` package is restored from the Nalpeiron package registry. For
local development, create an untracked `nuget.config` file from the template:

```bash
cp src/NalpeironGrowthPlatformDemo/nuget.config.template nuget.config
```

Credentials to the Nalpeiron package registry can be created in the Zentitle2 interface of your production tenant
(`Administration -> API Credentials -> Package Registry`). Replace the username and password in
`nuget.config` with those generated package registry credentials before running `dotnet restore`,
`dotnet build` or `dotnet test`.

### Local Application Settings

Before the first local run, create the untracked settings file from the committed template:

```powershell
Copy-Item src/NalpeironGrowthPlatformDemo/appsettings.Local.example.json `
  src/NalpeironGrowthPlatformDemo/appsettings.Local.json
```

On macOS or Linux, use:

```bash
cp src/NalpeironGrowthPlatformDemo/appsettings.Local.example.json \
  src/NalpeironGrowthPlatformDemo/appsettings.Local.json
```

Then fill in the tenant-specific values. Standard ASP.NET Core layering applies: committed defaults
live in `appsettings.json`, local machine values live in `appsettings.Local.json`, and deploys can
use environment variables with the `__` separator.

Required live connection keys:

- `Nalpeiron:ApiVersion` - the supported Management API contract version; a default is committed
  in `appsettings.json` and should be changed there when the integration moves to a newer contract.
- `Nalpeiron:ApiUrl`
- `Nalpeiron:OAuthUrl`
- `Nalpeiron:TenantId`
- `Nalpeiron:ClientId`
- `Nalpeiron:ClientSecret`
- `Zentitle:ProductId`
- `Zenmeter:BusinessModelId`

Optional keys commonly overridden per environment:

- `Nalpeiron:WebUrl` - admin deep-link base URL.
- `Zentitle:EditionOrder`
- `Zentitle:Prices:<sku>:Price`
- `Zenmeter:Prices:<sku>:Price` - used by the `None` billing route
- `Billing:DefaultBillingSystem` - provider used by the debug `/api/demo/zenmeter/pricing` endpoint
  when no billing system is specified
- `Billing:EnabledBillingSystems` - providers allowed to resolve prices and start checkout
- `Billing:Stripe:ZenmeterSuccessUrl` / `ZenmeterCancelUrl`
- `Billing:Stripe:ZentitleSuccessUrl` / `ZentitleCancelUrl`

The base configuration and example `appsettings.Local.json` enable only the direct `None` provider,
so a first local run does not need external credentials. The supplied Compose deployment enables
all three providers; other deployment pipelines should explicitly configure the same enabled list.
To enable only Stripe locally, list `None` and Stripe:

```json
{
  "Billing": {
    "DefaultBillingSystem": "None",
    "EnabledBillingSystems": ["None", "Stripe"]
  }
}
```

Add FastSpring to the same list to use all providers locally. Stripe additionally needs its secret
key and return URLs; FastSpring needs its API credentials and product-specific storefront URLs, as
described below. Keep `DefaultBillingSystem` in the enabled list. An empty
`EnabledBillingSystems` list falls back to all three providers for backward compatibility.

The product picker and its configured provider variants remain visible regardless of
`EnabledBillingSystems`. Selecting a disabled or unconfigured provider leaves the user on the
pricing page with a clear configuration error; disabling a provider does not hide its demo button.

The `Products` section is committed demo navigation copy. The `Zenmeter` section keeps the demo
product name, business model id and no-billing fallback SKU prices only; tiers, offerings, rates
and add-on compatibility come from the live API.

### Stripe Setup For Zentitle Checkout

The Zentitle Stripe route is `/elevate/stripe`. It supports recurring yearly offerings only.
Perpetual licenses and free trials continue through `/elevate/default`, and upgrades remain disabled
for Stripe-managed entitlements.

For every paid yearly Zentitle offering, create an active recurring USD Stripe Price whose
`lookup_key` exactly equals the Zentitle offering SKU. The demo resolves that Price for both the
pricing screen and Checkout; new catalogue entries do not need legacy `offering_sku` metadata.

The demo creates or reuses a Stripe Customer with its real `name` field and
`metadata.customer_ref` set to the existing Nalpeiron customer account reference. Checkout runs in
`subscription` mode and copies the generated demo `order_ref_id` into Subscription metadata. Orion
processes the initial paid subscription invoice, resolves the Price lookup key back to the Zentitle
offering, and provisions the entitlement group. The return page then polls Zentitle by the existing
customer and that application order reference until provisioning completes.

Required setup:

1. Add active yearly Stripe Prices with lookup keys equal to the Zentitle offering SKUs.
2. Configure the Stripe secret API key and webhook signing secret in
   `Administration -> Integrations -> Stripe` in Orion.
3. Forward Stripe events to Orion's `/stripe/webhook`; initial provisioning uses `invoice.paid`
   with `billing_reason=subscription_create`.
4. Configure `Billing:Stripe:SecretKey`, `ZentitleSuccessUrl`, and `ZentitleCancelUrl` in the demo.
5. Keep the Stripe listener running while testing so Orion can provision asynchronously.

### Stripe Setup For Zenmeter Checkout

The Zenmeter checkout uses Stripe on `/elevate/saas/stripe`.
The demo app does not store Stripe `price_...` ids or display prices in configuration. It retrieves
the Zenmeter business model and tier configuration through the Management API, then resolves active
Stripe Prices where `lookup_key` equals the selected Zenmeter SKU for both pricing display and
checkout.

During checkout the demo creates or reuses a Stripe Customer keyed by the Nalpeiron customer
reference in Stripe customer metadata, then passes that Stripe Customer to Checkout. The webhook
still provisions Zenmeter from subscription metadata after the initial paid invoice. After the
subscription lookup succeeds, the demo creates the Zenmeter subscription user with an explicit
Management API call. Its external user id is generated from the entered first and last name.

Follow this setup order:

1. Ensure the Stripe catalog has active Prices whose `lookup_key` values match the Zenmeter SKUs
   returned by the Management API.
2. Run `stripe listen` with Stripe CLI and keep it running while testing checkout.
3. Copy the `whsec_...` webhook signing secret printed by `stripe listen`.
4. In Orion, open `Administration -> Integrations -> Stripe` and save the Stripe secret API key
   plus the `whsec_...` webhook signing secret.
5. Configure the demo app local Stripe secret so it can create Stripe Checkout sessions.

#### Configure Local Stripe Secrets

Copy `src/NalpeironGrowthPlatformDemo/appsettings.Local.example.json` to
`src/NalpeironGrowthPlatformDemo/appsettings.Local.json`, then fill:

```json
{
  "Billing": {
    "Stripe": {
      "SecretKey": "sk_test_xxx",
      "ZenmeterSuccessUrl": "http://localhost:5142/elevate/saas/billing/return",
      "ZenmeterCancelUrl": "http://localhost:5142/elevate/saas/stripe/checkout",
      "ZentitleSuccessUrl": "http://localhost:5142/elevate/billing/return",
      "ZentitleCancelUrl": "http://localhost:5142/elevate/stripe/checkout"
    }
  }
}
```

The enabled list controls whether an integration can resolve prices and start checkout. Both
Zentitle and Zenmeter accept `default`, `stripe` and `fastspring`. `DefaultBillingSystem` only picks
the provider used by the debug `/api/demo/zenmeter/pricing` endpoint. For example, this enables
Stripe while leaving FastSpring disabled:

```json
{
  "Billing": {
    "DefaultBillingSystem": "None",
    "EnabledBillingSystems": ["None", "Stripe"]
  }
}
```

#### Forward Stripe Webhooks And Configure Orion

Install the Stripe CLI on macOS with Homebrew:

```bash
brew install stripe
stripe --version
stripe login
```

Forward Stripe events to the Orion Stripe webhook URL configured for your tenant. Obtain the
public integration host from the environment configuration or infrastructure owner; do not infer it
from the tenant administration URL.

```bash
stripe listen --forward-to https://[STRIPE_INTEGRATION_HOST]/stripe/webhook
```

The Stripe CLI prints a webhook signing secret beginning with `whsec_`. In Orion, open the tenant
administration area, go to `Administration -> Integrations -> Stripe`, and save both:

- Stripe secret API key, for example `sk_test_...`
- Webhook signing secret printed by `stripe listen`, for example `whsec_...`

The webhook signing secret must be the one from the currently running `stripe listen` process.
Otherwise the local Stripe host will reject forwarded events during signature validation.

For the Zenmeter subscription purchase flow, the important Stripe event is the initial paid
subscription invoice. Keep the `stripe listen` process running while testing checkout.

If you prefer keeping the secret out of any file:

```bash
dotnet user-secrets set "Nalpeiron:ClientSecret" "<secret>" --project src/NalpeironGrowthPlatformDemo
```

### FastSpring Setup For Zentitle Checkout

The Zentitle checkout uses FastSpring on `/elevate/fastspring`. Configure
`Billing:FastSpring:ZentitleStorefrontUrl` with the `data-storefront` value of a dedicated popup,
for example `elevatetest.test.onfastspring.com/popup-zentitle`.

FastSpring and Orion requirements:

- Create FastSpring subscription products whose product paths exactly match the Zentitle offering
  SKUs. Orion resolves each path against the Zentitle offering catalogue.
- Add those products to the dedicated Zentitle popup and add the demo origin to FastSpring's
  allowed website domains.
- FastSpring's price API is account-wide and cannot confirm popup product membership. The demo can
  display a product that exists in the account but is missing from this storefront, so the popup's
  product list must be kept aligned with the Zentitle offering SKUs.
- Configure the FastSpring integration webhook in Orion. Zentitle provisioning is triggered by
  `subscription.activated`; FastSpring's `order.completed` event does not provision an entitlement.
- Keep yearly paid offerings as recurring FastSpring subscriptions. Perpetual purchases remain on
  `/elevate/default` because they are one-time orders, and free trials use the default checkout
  because no external payment is required.
- The demo creates the customer before checkout and passes its `accountRefId` as `customer_ref`.
  Orion uses that reference to reuse the customer when it provisions the entitlement group.
- After checkout, the demo polls Zentitle by customer and the actual FastSpring original-order ID
  until the entitlement group appears. Closing checkout before FastSpring returns an order ID does
  not complete the demo session.

The Zentitle and Zenmeter popup storefronts must belong to the FastSpring account addressed by the
configured API username and password.

### FastSpring Setup For Zenmeter Checkout

The Zenmeter checkout uses FastSpring on `/elevate/saas/fastspring`.
The demo is the product-selection surface and uses FastSpring Store Builder popup checkout only to
collect payment. Configure `Billing:FastSpring:ZenmeterStorefrontUrl` with the `data-storefront`
value from FastSpring's "Place on your Website" snippet, for example
`elevatetest.test.onfastspring.com/popup-zenmeter`.

FastSpring setup required for the popup checkout:

- Create or sync FastSpring products/subscriptions with product paths equal to Zenmeter SKUs.
- Use a dedicated popup checkout such as `popup-zenmeter`.
- Manually add all demo products/SKUs to that popup checkout's product list. FastSpring documents
  this as an app UI operation; there is no documented API-only endpoint for popup product membership.
- Disable shopping cart review, quantity editing, upsells, cross-sells, product recommendations,
  bundles and coupon prompts where FastSpring exposes those settings.
- Add the local demo origin to allowed website domains, including protocol and port, for example
  `http://localhost:5142`.
- Keep the checkout in test/offline mode for now.
- Configure the FastSpring webhook/integration in Orion so successful FastSpring orders provision
  Zenmeter subscriptions from the order tags/reference ids passed by the demo.
- FastSpring one-time add-on purchases are attached to Zenmeter by the demo after order
  verification, using the actual FastSpring Order ID as the Zenmeter add-on order reference.
- FastSpring recurring add-ons are not attached by the demo. The demo updates the existing
  FastSpring subscription, and Orion provisions the Zenmeter add-on from the FastSpring webhook.
- A recurring add-on can be purchased more than once for the same subscription. FastSpring treats
  the add-on quantity in a subscription update as the target total for that product, so the demo
  reads the subscription first and sends the current quantity plus one. Keep recurring add-on
  products configured in FastSpring as subscription add-ons that allow a quantity above one.

FastSpring pricing is resolved by treating each Zenmeter SKU as the matching FastSpring product
path and retrieving that product's price from FastSpring.

#### Update FastSpring Product Prices

The product-specific entrypoints share one FastSpring update engine but select prices using their
own catalog rules. Always run with `--dry-run` first.

Update every product path configured in `Zenmeter.Prices` with:

```powershell
node scripts/zenmeter/update-fastspring-product-prices.js `
  --appsettings src/NalpeironGrowthPlatformDemo/appsettings.json `
  --api-username <test-store-api-username> `
  --api-password <test-store-api-password> `
  --dry-run
```

Update only the recurring Zentitle product paths from `Zentitle.Prices` with:

```powershell
node scripts/zentitle/update-fastspring-product-prices.js `
  --appsettings src/NalpeironGrowthPlatformDemo/appsettings.json `
  --api-username <test-store-api-username> `
  --api-password <test-store-api-password> `
  --dry-run
```

The Zentitle entrypoint requires every configured SKU to end in `-yearly` or `-perpetual`. It
updates only `-yearly` products and deliberately excludes perpetual products from FastSpring.
An ambiguous SKU fails preflight instead of being updated or silently skipped.

Remove `--dry-run` to apply prices. Before making any changes, each entrypoint verifies that every
selected SKU exists in FastSpring. The shared updater preserves the current pricing configuration
and prices in other currencies, replacing only USD by default; use `--currency <code>` for another
currency.

FastSpring credentials can alternatively come from `Billing.FastSpring.ApiUsername` and
`ApiPassword` in the selected appsettings file. `--base-url` overrides `Billing.FastSpring.ApiUrl`.
Run either entrypoint with `--help` to list all supported options.

### Docker Environment

Copy `.env.example` to the ignored `.env` file and fill in the tenant-specific values. The same
ignored root `nuget.config` used for local development is mounted as a build secret while restoring
packages; it is excluded from the Docker build context and image layers.

`docker-compose.yml` maps host-side variables to ASP.NET Core configuration:

- `NALPEIRON_API_URL`
- `NALPEIRON_OAUTH_URL`
- `NALPEIRON_TENANT_ID`
- `NALPEIRON_CLIENT_ID`
- `NALPEIRON_CLIENT_SECRET`
- `NALPEIRON_WEB_URL`
- `ZENTITLE_PRODUCT_ID`
- `ZENTITLE_EDITION_ORDER`
- `ZENMETER_BUSINESS_MODEL_ID`
- `BILLING_DEFAULT_SYSTEM`
- `STRIPE_API_URL`
- `STRIPE_SECRET_KEY`
- `STRIPE_ZENMETER_SUCCESS_URL`
- `STRIPE_ZENMETER_CANCEL_URL`
- `STRIPE_ZENTITLE_SUCCESS_URL`
- `STRIPE_ZENTITLE_CANCEL_URL`
- `FASTSPRING_ZENMETER_STOREFRONT_URL`
- `FASTSPRING_ZENTITLE_STOREFRONT_URL`
- `FASTSPRING_API_URL`
- `FASTSPRING_API_USERNAME`
- `FASTSPRING_API_PASSWORD`

Keep `.env.example` and `docker-compose.yml` aligned when these keys change.
Unlike a local `dotnet run`, the supplied Compose configuration enables `None`, FastSpring and
Stripe because it represents the complete sales-demo deployment.

Build and start the application with:

```bash
docker compose up --build
```

The runtime image includes ICU globalization data and time-zone data so the application's
`en-US` culture works when using the default Alpine-based .NET image.

## Run Locally

```bash
dotnet run --project src/NalpeironGrowthPlatformDemo
```

Open the URL printed by Kestrel, then choose a product on `/`.

Useful paths:

- `/` - product picker
- `/elevate/default`, `/elevate/stripe`, `/elevate/fastspring` - Zentitle pricing
- `/elevate/workspace` - Zentitle workspace
- `/elevate/billing/return` - Zentitle external-billing return and provisioning status
- `/elevate/saas/default`, `/elevate/saas/stripe`, `/elevate/saas/fastspring` - Zenmeter pricing
- `/elevate/saas/workspace` - Zenmeter workspace

## Diagnostics

Development-only endpoints:

- `GET /api/demo/config` - resolved API/Web URL, tenant, product ids and configuration state.
- `GET /api/demo/zentitle/pricing` - live Zentitle pricing read model.
- `GET /api/demo/zenmeter/pricing` - Zenmeter business model pricing read model.

## Demo Data Cleanup

Objects created by the demo carry the `_demo-z2-` reference id prefix where the API supports it:

- customer `accountRefId` = `_demo-z2-<guid>` (max 32 chars)
- Zentitle entitlement group `orderRefId` = `_demo-z2-<timestamp>-<customer-slug>` (max 50 chars)
- Zenmeter subscription order ref uses the same order reference helper

Use the Nalpeiron admin UI/API to manually review and delete live demo data.

## Build / Test

```bash
dotnet build NalpeironGrowthPlatformDemo.slnx
dotnet test NalpeironGrowthPlatformDemo.slnx
```

If a running app or tool locks `bin/Debug` or `obj`, use an alternate output directory:

```bash
dotnet build NalpeironGrowthPlatformDemo.slnx -p:OutDir=artifacts/build-check/ -v:minimal
dotnet test NalpeironGrowthPlatformDemo.slnx -p:OutDir=artifacts/test-check/ -v:minimal
```

Remove only the generated `artifacts/` directory afterwards. Tests use local fakes and do not
need a live API connection.

The FastSpring price-update scripts under `scripts/` have their own tests on the Node.js built-in
test runner. They stub the FastSpring API with a local HTTP server, so they also need no
credentials or network access:

```bash
node --test
```
