param([int]$BridgePort = 17890, [int]$ControlPort = 40288, [switch]$ProbeRacingLab,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../../artifacts/moza-coexist/read-only-probe.json'))
$ErrorActionPreference = 'Stop'
# Only known READ requests. No device setters, serial port or configuration writes.
# Racing Lab control-channel connection/disconnection can itself affect its session lifecycle;
# therefore it is opt-in and must not be used during an active driving-assistance session.
$report = [ordered]@{ timestamp = [DateTimeOffset]::Now.ToString('O'); requests = @() }
if ($ProbeRacingLab) {
$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$cancel = [Threading.CancellationTokenSource]::new(7000)
try {
    $null = $ws.ConnectAsync([Uri]"ws://127.0.0.1:$BridgePort/control/main", $cancel.Token).GetAwaiter().GetResult()
    $request = [Text.Encoding]::UTF8.GetBytes('{"jsonrpc":"2.0","id":9137,"method":"ai.status","params":{}}')
    $null = $ws.SendAsync([ArraySegment[byte]]::new($request), [Net.WebSockets.WebSocketMessageType]::Text, $true, $cancel.Token).GetAwaiter().GetResult()
    while (-not $cancel.IsCancellationRequested) {
        $message = [IO.MemoryStream]::new()
        do {
            $buffer = [byte[]]::new(8192)
            $received = $ws.ReceiveAsync([ArraySegment[byte]]::new($buffer), $cancel.Token).GetAwaiter().GetResult()
            if ($received.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) { throw 'Bridge closed the connection.' }
            $message.Write($buffer,0,$received.Count)
            if ($message.Length -gt 1048576) { throw 'Unexpected response size.' }
        } while (-not $received.EndOfMessage)
        $reply = [Text.Encoding]::UTF8.GetString($message.ToArray()) | ConvertFrom-Json
        $message.Dispose()
        if ($reply.id -eq 9137) {
            $report.requests += @{ endpoint="ws://127.0.0.1:$BridgePort/control/main"; method='ai.status';
                received=$true; error=$reply.error; resultKeys=@($reply.result.PSObject.Properties.Name) }
            break
        }
    }
} catch { $report.requests += @{ endpoint='Racing Lab bridge'; method='ai.status'; error=$_.Exception.Message } }
finally { $ws.Abort(); $ws.Dispose(); $cancel.Dispose() }
}

# CBOR: {Head:{PacketId:4,Version:'1.0.4',ReplyPort:<port>},Payload:[read field names]}.
# This established Pit House operation reads steering limits; PacketId 3 is never sent.
function CborText([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    if ($bytes.Length -lt 24) { return ,([byte[]]@([byte](0x60+$bytes.Length)) + $bytes) }
    return ,([byte[]]@(0x78,[byte]$bytes.Length) + $bytes)
}
$udp = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0))
$udp.Client.ReceiveTimeout = 3000
try {
    $replyPort = $udp.Client.LocalEndPoint.Port
    [byte[]]$body = @(0xa2) + (CborText 'Head') + @(0xa3) + (CborText 'PacketId') + @(4) +
        (CborText 'Version') + (CborText '1.0.4') + (CborText 'ReplyPort') + @(0x19,($replyPort -shr 8),($replyPort -band 255)) +
        (CborText 'Payload') + @(0x82) + (CborText 'MotGetSteer_MaximumAngle') + (CborText 'MotGetSteer_LimitAngle')
    $null = $udp.Send($body,$body.Length,[Net.IPEndPoint]::new([Net.IPAddress]::Loopback,$ControlPort))
    $peer = [Net.IPEndPoint]::new([Net.IPAddress]::Any,0)
    $bytes = $udp.Receive([ref]$peer)
    $report.requests += @{endpoint="udp://127.0.0.1:$ControlPort"; method='PacketId 4 (read steering limits)';
        received=$true; responseBytes=$bytes.Length; responseHex=[Convert]::ToHexString($bytes)}
} catch { $report.requests += @{endpoint="udp://127.0.0.1:$ControlPort"; method='PacketId 4'; error=$_.Exception.Message} }
finally { $udp.Dispose() }
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$report | ConvertTo-Json -Depth 7
