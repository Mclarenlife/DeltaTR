param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ProbeArgs)
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    Invoke-DeltaTRDotnet build src/DeltaTR.Diagnostics/DeltaTR.Diagnostics.csproj -c Release --artifacts-path $script:BuildRoot --ignore-failed-sources
    Invoke-DeltaTRDotnet (Join-Path $script:BuildRoot 'bin/DeltaTR.Diagnostics/release/DeltaTR.Diagnostics.dll') @ProbeArgs
}
finally { Pop-Location }
