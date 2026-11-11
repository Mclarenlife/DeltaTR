. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try { Invoke-DeltaTRDotnet build DeltaTR.slnx -c Release --artifacts-path $script:BuildRoot --ignore-failed-sources }
finally { Pop-Location }
