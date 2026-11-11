param(
    [Parameter(Mandatory=$true)][string]$ReferenceDashboard,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/moza-screen-probe/DeltaTR Screen Probe')
)
$ErrorActionPreference = 'Stop'
# Use only the installed editor's document structure and device geometry.
# No original artwork, game bindings, or images are carried into this probe.
$document = Get-Content -LiteralPath $ReferenceDashboard -Raw | ConvertFrom-Json -AsHashtable
$screen = $document.children[0]
function Find-TextElement($item) {
    if ($item.type -eq 'Text.qml') { return $item }
    foreach ($child in $item.children) { $found = Find-TextElement $child; if ($null -ne $found) { return $found } }
    return $null
}
$template = Find-TextElement $screen
if ($null -eq $template -or $document.general.width -ne 780 -or $document.general.height -ne 248) {
    throw 'Expected a KS Pro 780 x 248 dashboard and a text element.'
}
function New-ProbeText([int]$Id,[int]$Y,[int]$Height,[int]$Size,[string]$Text,[string]$Color,[bool]$Dynamic) {
    $item = $template | ConvertTo-Json -Depth 50 | ConvertFrom-Json -AsHashtable
    $item.id=$Id; $item.name=$Text; $item.children=@(); $item.actions=@{}; $item.binding=@{}; $item.macros=@{}; $item.plugins=@()
    $item.general.x=16; $item.general.y=$Y; $item.general.width=748; $item.general.height=$Height
    $item.general.backgroundColor=@{color='#00000000';type='SOLID'}
    $item.text.fontFamily='Source Han Sans CN'; $item.text.fontSize=$Size; $item.text.fontWeight=500
    $item.text.fontColor=@{color=$Color;type='SOLID'}; $item.text.text=$Text
    $item.text.wrapText=$true; $item.text.horizontalAlignment='AlignLeft'; $item.text.verticalAlignment='AlignCenter'
    if($Dynamic) { $item.binding=@{'text.text'=@{type='METHOD_CHAINING';methods=@('Telemetry.get("v1/gameData/PlayerName").value','_result ? String(_result) : "等待实时中文回答"')}} }
    return $item
}
$document.name='DeltaTR Screen Probe'; $document.imageResources=@(); $document.binding=@{}; $document.actions=@{}; $document.plugins=@(); $document.macros=@{}
$document.window.GUID=[guid]::NewGuid().ToString('N'); $document.lastModified=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$screen.name='DeltaTR Screen Probe'; $screen.binding=@{}; $screen.actions=@{}; $screen.plugins=@(); $screen.macros=@{}
$screen.children=@(
    (New-ProbeText 1001 8 48 28 'DeltaTR 中文屏幕验证' '#FFA3E4C8' $false),
    (New-ProbeText 1002 62 128 34 '等待实时中文回答' '#FFFFFFFF' $true),
    (New-ProbeText 1003 202 32 20 '测试消息 · 非真实遥测' '#FF9EAFC0' $false)
)
$document.children=@($screen)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$file=Join-Path $OutputDirectory 'DeltaTR Screen Probe.mzdash'
$document | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $file -Encoding utf8NoBOM
Write-Output (Resolve-Path -LiteralPath $file).Path
