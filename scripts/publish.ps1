. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    & "$PSScriptRoot/install-zipformer.ps1"
    & "$PSScriptRoot/install-sensevoice.ps1"
    $projectSettings = [xml](Get-Content -LiteralPath Directory.Build.props -Raw)
    $releaseVersion = $projectSettings.Project.PropertyGroup.Version
    $releaseRoot = Join-Path $script:ProjectRoot "artifacts/releases/$releaseVersion"
    $appOutput = Join-Path $releaseRoot 'app'
    Invoke-DeltaTRDotnet publish src/DeltaTR.App/DeltaTR.App.csproj -c Release --artifacts-path $script:BuildRoot --no-self-contained -o $appOutput --ignore-failed-sources
    Copy-Item -LiteralPath docs/licenses -Destination $appOutput -Recurse -Force
    Copy-Item -LiteralPath docs/THIRD_PARTY.md -Destination (Join-Path $appOutput 'THIRD_PARTY.md')
    Copy-Item -LiteralPath README.md -Destination (Join-Path $appOutput 'README.md')
    Copy-Item -LiteralPath docs/MOZA_KS_PRO.md -Destination (Join-Path $appOutput 'MOZA_KS_PRO.md')
    Write-Host "桌面程序：$appOutput/DeltaTR.App.exe（需 Windows x64 .NET 10 Desktop Runtime）"
}
finally { Pop-Location }
