# Download the pinned bilingual streaming model for local CPU inference.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root '.tools/models/zipformer-zh-en'
New-Item -Path $folder -ItemType Directory -Force | Out-Null
$source = 'https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20/resolve/98590b7ed6443e77b714204da2757d75e1a642f4'
$files = @{
    'encoder-epoch-99-avg-1.int8.onnx' = '8fa764187a261844f859d7143ebaa563af5d10adfece4c18a8f414c88cba2a9b'
    'decoder-epoch-99-avg-1.int8.onnx' = '1a70c593d71e53f023f5f55b0b4cfff5055abb786ee3992e5f63dc2e273cc4fa'
    'joiner-epoch-99-avg-1.int8.onnx' = '1ed689c5ed19dbaa725d9d191bb4822b5f4855a39e1ffd28cbc1f340d25b2ee0'
    'tokens.txt' = 'a8e0e4ec53810e433789b54a5c0134a7eaa2ffca595a6334d54c00da858841d3'
    'bpe.vocab' = 'd0b642f3a2eacd5fadefdeff9e0e1358cab729647cbb7fe58cf738e1f7407029'
}
foreach ($name in $files.Keys) {
    $file = Join-Path $folder $name
    $hash = $files[$name]
    if ((Test-Path -LiteralPath $file) -and $hash -and (Get-FileHash -LiteralPath $file).Hash -eq $hash) { continue }
    $temp = $file + '.' + [guid]::NewGuid().ToString('N') + '.part'
    try {
        & curl.exe --fail --location --silent --show-error --retry 3 --connect-timeout 20 --output $temp "$source/$name"
        if ($LASTEXITCODE -ne 0 -or ($hash -and (Get-FileHash -LiteralPath $temp).Hash -ne $hash)) { throw "Zipformer model download failed: $name" }
        Move-Item -LiteralPath $temp -Destination $file -Force
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
