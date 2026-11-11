$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent
$script:BuildRoot = Join-Path $script:ProjectRoot 'artifacts/build-tr'
$env:DOTNET_CLI_HOME = Join-Path $script:ProjectRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $script:ProjectRoot '.tools/nuget'

function Get-DeltaTRDotnet {
    $localSdk = Join-Path $script:ProjectRoot '.tools/dotnet/dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { return $localSdk }
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($systemDotnet -and ((& $systemDotnet.Source --list-sdks) -match '^10\.')) { return $systemDotnet.Source }
    throw '需要 .NET 10 SDK。请先运行 scripts/bootstrap.ps1，或安装微软官方 .NET 10 SDK。'
}

function Invoke-DeltaTRDotnet {
    & (Get-DeltaTRDotnet) @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet 执行失败，退出码 $LASTEXITCODE" }
}
