param([switch]$Voice, [switch]$SpeechBenchmark)
. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    Invoke-DeltaTRDotnet build DeltaTR.slnx -c Release --artifacts-path $script:BuildRoot --ignore-failed-sources
    Invoke-DeltaTRDotnet (Join-Path $script:BuildRoot 'bin/DeltaTR.Tests/release/DeltaTR.Tests.dll')
    $runDir = Join-Path $script:ProjectRoot ('artifacts/validation/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $runDir | Out-Null
    $smokeArgs = @('--smoke-test', (Join-Path $runDir 'radio.png'), '--smoke-shell')
    if ($Voice) { $smokeArgs += '--smoke-voice' }
    if ($SpeechBenchmark) { $smokeArgs += '--smoke-sensevoice' }
    Invoke-DeltaTRDotnet (Join-Path $script:BuildRoot 'bin/DeltaTR.App/release/DeltaTR.App.dll') @smokeArgs
    if (-not (Test-Path -LiteralPath (Join-Path $runDir 'radio.png'))) { throw 'UI did not render.' }
    if ((Get-Item (Join-Path $runDir 'radio.bindings.log')).Length -gt 0) { throw 'UI binding errors detected.' }
    Write-Host "验证完成：$runDir"
}
finally { Pop-Location }
