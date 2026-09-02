# Generated Management API Clients

The Zentitle and Zenmeter raw Management API clients are generated with NSwag.
A single NSwag config is used twice with product-specific variables so each OpenAPI document
controls its own generated DTO and endpoint surface without duplicating generator settings.

Restore the repo-local NSwag tool and regenerate both clients with the product-specific
OpenAPI documents.

## Nalpeiron maintainer workflow

The commands below use the standard Nalpeiron Orion development environment.

Windows PowerShell:

```powershell
.\nswag\generate-management-api-clients.ps1 `
  -ZentitleOpenApiJson "https://oriondev.api.nalpeiron-dev.com:8443/openapi/v1/2025-10-10/openapi.json" `
  -ZenmeterOpenApiJson "https://oriondev.api.nalpeiron-dev.com:8443/openapi/v1/2026-07-01/openapi-zenmeter.json"
```

macOS/Linux:

```bash
./nswag/generate-management-api-clients.sh \
  "https://oriondev.api.nalpeiron-dev.com:8443/openapi/v1/2025-10-10/openapi.json" \
  "https://oriondev.api.nalpeiron-dev.com:8443/openapi/v1/2026-07-01/openapi-zenmeter.json"
```

## Custom OpenAPI source

Both scripts accept explicit OpenAPI URLs. Replace the maintainer endpoints above when generating
clients from another local or tenant environment.

The config uses `runtime: Net100` and NSwag 14.7.1 so generation runs on the same .NET 10
runtime family as the demo application. The generated clients derive from
`GeneratedManagementApiClientBase`, which adds the Management API base URL, bearer token,
`N-TenantId`, `N-Api-Version`, and shared error normalization.
