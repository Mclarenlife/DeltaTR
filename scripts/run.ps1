param([switch]$Demo)
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    Invoke-DeltaTRDotnet build src/DeltaTR.App/DeltaTR.App.csproj -c Release --artifacts-path $script:BuildRoot --ignore-failed-sources
    $app = Join-Path $script:BuildRoot 'bin/DeltaTR.App/release/DeltaTR.App.dll'
    if ($Demo) { Invoke-DeltaTRDotnet $app --demo }
    else { Invoke-DeltaTRDotnet $app }
}
finally { Pop-Location }
