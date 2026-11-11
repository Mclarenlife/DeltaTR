# Download the pinned CPU-int8 SenseVoice model; no user audio leaves the machine.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root '.tools/models/sensevoice'
New-Item -Path $folder -ItemType Directory -Force | Out-Null
$source = 'https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/2365baeacb507f821a0c8120fcee3d484dba7a07'
$files = @{
    'model.int8.onnx' = 'c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51'
    'tokens.txt' = 'f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc'
}
foreach ($name in $files.Keys) {
    $file = Join-Path $folder $name
    if ((Test-Path -LiteralPath $file) -and (Get-FileHash -LiteralPath $file).Hash -eq $files[$name]) { continue }
    $temp = $file + '.' + [guid]::NewGuid().ToString('N') + '.part'
    try {
        & curl.exe --fail --location --silent --show-error --retry 3 --retry-all-errors --connect-timeout 20 --max-time 600 --output $temp "$source/$name"
        if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $temp).Hash -ne $files[$name]) { throw "SenseVoice model download or SHA-256 verification failed: $name" }
        Move-Item -LiteralPath $temp -Destination $file -Force
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
