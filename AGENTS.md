# AGENTS.md

## Hard Constraints

- Do not stage files or create commits unless the user explicitly asks for it after review.
- Do not revert user changes unless the user explicitly asks for it.
- Do not use project skills for this repository.
- Keep edits scoped to the requested demo application.

## Project Goal

Sales demo application for the **Elevate** product family, used to show prospects how to
integrate Nalpeiron platform products:

- `Elevate On-premise` - Zentitle2 licensing / entitlement demo (`/elevate/default`,
  `/elevate/fastspring`).
- `Elevate SaaS` - Zenmeter subscription / metered usage demo (`/elevate/saas`).

It is an ASP.NET Core (net10) app using Blazor **Interactive Server** components and Tailwind
CSS loaded from CDN. The browser stores only demo session ids in localStorage; server-side
in-memory stores hold the demo session state. There is no mock/offline mode for live operations.

## Architecture

Repo layout is `src/` (the app) + `tests/` (xUnit) as siblings; the solution is at the root.
Inside `src/NalpeironGrowthPlatformDemo/` the integration is a **shared platform core +
per-product slices**.

- `Nalpeiron/` - Nalpeiron Growth Platform integration.
  - `Generic/` - shared by every product:
    - `AccessTokenProvider` - Keycloak `client_credentials` token (cached).
    - `ManagementApiClient` - authenticated transport (`Bearer` + `N-TenantId` +
      `N-Api-Version`, `GetJson`/`SendJson`, friendly error extraction).
    - `CustomersClient` - shared customers resource.
  - `Zentitle/` - Zentitle Management API slice: `ZentitleManagementClient`,
    `ZentitleContracts`, `PricingCatalog`, `LicensingEnums`, `UpgradePolicy`.
  - `Zenmeter/` - Zenmeter Management API slice: `ZenmeterManagementClient`,
    `ZenmeterContracts`, `ZenmeterPricingCatalog`, `ZenmeterEnums`.
- `Domain/ReferenceId.cs` - demo reference ids (`_demo-z2-` prefix, length limits).
- `Application/` - application orchestration and read-model shaping, sliced per product:
  - `Shared/` - cross-product helpers and billing contracts: `CheckoutRequestGuard`,
    `DemoActionResult`, billing price resolver/catalog, FastSpring popup context,
    `UsageQuantity` (minimum quantity rule), `NalpeironWebLinks`.
  - `Zentitle/` - provider-aware `ElevateDemoService` orchestration, billing provider registry and
    capabilities, direct/FastSpring checkout, asynchronous provisioning status,
    `ZentitleDemoModels` / `ZentitleDemoSession`.
  - `Zenmeter/` - `ZenmeterDemoService` orchestration, `ZenmeterDemoModels` view models,
    `Zenmeter*Projector` (API DTO -> workspace read model), `Zenmeter*Policy`
    (plan/add-on/top-up rules), `ZenmeterWorkspaceBuilder`, `ZenmeterUsageSnapshotApplier`.
- `Components/` - Blazor UI:
  - `Pages/Products` (`/`)
  - `Pages/Zentitle` (`/elevate/{default|fastspring}`, provider checkout, billing return,
    `/elevate/workspace`)
  - `Pages/Zenmeter` (`/elevate/saas`, `/elevate/saas/checkout`, `/elevate/saas/workspace`)
  - `Zentitle/`, `Zenmeter/`, `Shared/` component folders.
- `Configuration/` - `NalpeironOptions`, `ZentitleOptions`, `ZenmeterOptions`,
  `DemoProductsOptions`.

Magic strings from API/config are parsed into enums at boundaries. Zentitle internals use
`FeatureKind` / `BillingPeriod`; Zenmeter internals use `ZenmeterOfferingPeriod`,
`ZenmeterAddonType` and `ZenmeterRenewalBehavior`.

## Demo Flow (live API)

### Zentitle / Elevate On-premise

- **Pricing**: live offerings + edition features. The default route uses `Zentitle:Prices`;
  FastSpring resolves account prices by offering SKU/product path.
- **Purchase**: shared customer creation. Default creates the entitlement group directly;
  FastSpring opens popup checkout and waits for Orion's `subscription.activated` provisioning.
- **Workspace**: live entitlement details and grouped usage-count / element-pool / boolean features.
- **Use a feature**: lazy activation, then feature checkout/return.
- **Upgrade**: direct/default sessions use `UpgradePolicy` and change-offering. Upgrade remains
  disabled for FastSpring-managed sessions until provider-side subscription changes are supported.
- **Reset demo**: clears local session state only; live Zentitle data is cleared manually.

### Zenmeter / Elevate SaaS

- **Pricing**: configured `Zenmeter` sales catalog for tiers, offerings, add-ons and rates.
- **Purchase**: shared customer creation, Zenmeter subscription creation by SKU, default user setup.
- **Workspace**: live subscription, user, features, meters and active add-ons projected into
  workspace read models.
- **Use a feature**: feature consumption for the default user; returned usage snapshots update
  local workspace state.
- **Top up**: visible meter top-ups (one-time and recurring) filtered by plan period and added to the
  live subscription. Recurring add-ons stay purchasable when the subscription already carries the
  same SKU, because Orion allows several recurring add-ons per subscription.
- **Reset demo**: clears local session state only; live Zenmeter data is cleared manually.

## Configuration

Standard ASP.NET Core layering (no custom loaders). Main sections:

- `Nalpeiron` - shared API/OAuth/Web URL/tenant/client connection.
- `Zentitle` - product id, edition order and SKU price map.
- `Zenmeter` - committed sales catalog, feature rates, add-ons and tier order.
- `Products` - product picker cards and routes.
- `Billing` - enabled providers, shared provider credentials, separate
  `FastSpring:ZenmeterStorefrontUrl` / `FastSpring:ZentitleStorefrontUrl`, and polling settings.

Required live settings: `Nalpeiron:ApiUrl`, `Nalpeiron:OAuthUrl`, `Nalpeiron:TenantId`,
`Nalpeiron:ClientId`, `Nalpeiron:ClientSecret`, `Zentitle:ProductId`.

Optional common overrides: `Nalpeiron:WebUrl`, `Zentitle:EditionOrder`, `Zenmeter:TierOrder`,
and individual price/catalog entries.

Do not hardcode customer ids, offering ids, SKU ids, secrets, URLs, tenant ids, product ids,
OAuth clients, or credentials in code. Keep `.env.example`, `docker-compose.yml` and
`appsettings.Local.example.json` aligned when configuration keys change.

## Reference Ids

Objects created through the demo carry `_demo-z2-` reference ids where supported:

- customer `accountRefId` = `_demo-z2-<guid>` (max 32 chars)
- order/subscription references use `_demo-z2-<timestamp>-<customer-slug>` (max 50 chars)

## Frontend Expectations

- Product picker shows both Elevate On-premise and Elevate SaaS paths.
- Zentitle pricing stays close to the original Elevate store: plan cards, billing switch
  (Yearly / Perpetual), centered "Popular" badge on the featured edition.
- Zenmeter pricing supports tier cards, billing mode and add-on selection.
- Zentitle workspace groups entitlement features by type: usage count, element pool, boolean.
- Zenmeter workspace groups subscription summary, active add-ons, meter pools, usage features,
  access features and admin logs.
- Admin Logs panels are dark.
- US application: dates/numbers render as `en-US` (forced via culture + request localization).

## Test conventions

Unit tests follow the project conventions:

- Test files mirror the `src/NalpeironGrowthPlatformDemo/` folder structure, and the namespace
  matches the folder (`NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders`).
  Shared test doubles and builders live in `tests/NalpeironGrowthPlatformDemo.Tests/TestHelpers/`.
- Test classes are `public sealed class <TypeUnderTest>Tests`.
- Test methods are named `MethodUnderTest_Scenario_ExpectedBehavior`
  (for example `CreateCheckout_WhenProviderIsDisabled_Throws`); drop the scenario segment only when
  there is nothing meaningful to state.
- Bodies are split with lowercase `// arrange`, `// act`, `// assert` comments; omit a section that
  does not apply. For expected exceptions, capture the call as `var act = () => ...` under `// act`
  and assert with `Assert.Throws*(act)` under `// assert`.

## Verification

For code changes, prefer running:

```powershell
dotnet build NalpeironGrowthPlatformDemo.slnx
dotnet test NalpeironGrowthPlatformDemo.slnx
```

When changing anything under `scripts/`, also run the Node.js tests:

```powershell
node --test
```

If `bin/Debug` or `obj` is locked, use an alternate `OutDir` and remove only the generated
`artifacts/` directory afterwards. Tests use mocked dependencies until local fakes are needed and
run without an API connection. Do not start a long-running dev server unless needed for the current
task; if started, stop it before ending the turn unless the user asks to keep it running.
