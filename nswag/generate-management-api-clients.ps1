param(
    [Parameter(Mandatory = $true)]
    [string] $ZentitleOpenApiJson,

    [Parameter(Mandatory = $true)]
    [string] $ZenmeterOpenApiJson
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$zentitleOutput = (Join-Path $root 'src/NalpeironGrowthPlatformDemo/Nalpeiron/Zentitle/Generated/ZentitleManagementApiGeneratedClient.g.cs') -replace '\\', '/'
$zenmeterOutput = (Join-Path $root 'src/NalpeironGrowthPlatformDemo/Nalpeiron/Zenmeter/Generated/ZenmeterManagementApiGeneratedClient.g.cs') -replace '\\', '/'

Push-Location $root
try {
    dotnet tool restore

    dotnet tool run nswag run `
        nswag/management-api.nswag `
        /variables:OpenApiJson=$ZentitleOpenApiJson,ClassName=ZentitleManagementApiGeneratedClient,Namespace=NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated,ExceptionClass=ZentitleManagementApiException,Output=$zentitleOutput

    dotnet tool run nswag run `
        nswag/management-api.nswag `
        /variables:OpenApiJson=$ZenmeterOpenApiJson,ClassName=ZenmeterManagementApiGeneratedClient,Namespace=NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated,ExceptionClass=ZenmeterManagementApiException,Output=$zenmeterOutput
}
finally {
    Pop-Location
}
