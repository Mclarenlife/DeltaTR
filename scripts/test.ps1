. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    Invoke-DeltaTRDotnet build DeltaTR.slnx -c Release --artifacts-path $script:BuildRoot --ignore-failed-sources
    Invoke-DeltaTRDotnet (Join-Path $script:BuildRoot 'bin/DeltaTR.Tests/release/DeltaTR.Tests.dll')
}
finally { Pop-Location }
