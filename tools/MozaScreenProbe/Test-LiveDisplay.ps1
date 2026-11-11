param([string]$Port='COM3',[ValidateRange(10,120)][int]$Seconds=60,[switch]$SelfTest)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot '../../src/DeltaTR.Hardware.Moza/DisplaySession.cs')
[MozaDisplaySession]::SelfTest()
if($SelfTest) { Write-Output 'Protocol CRC, frame and Chinese UTF-8 boundary checks passed.'; return }
if(Get-Process -Name 'MOZA Pit House' -ErrorAction SilentlyContinue) { throw 'Exit Pit House before running this display test.' }
if(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -match 'iRacingSim|AC2-Win64|Le Mans Ultimate'}) { throw 'Exit the driving simulator before testing.' }
$probeLogDirectory=Join-Path $PSScriptRoot '../../artifacts/moza-screen-probe'
New-Item -ItemType Directory -Path $probeLogDirectory -Force | Out-Null
$probeLogPath=Join-Path $probeLogDirectory ('live-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$probeSerial=[System.IO.Ports.SerialPort]::new($Port,115200,[System.IO.Ports.Parity]::None,8,[System.IO.Ports.StopBits]::One)
$probeSerial.ReadTimeout=30; $probeSerial.WriteTimeout=500; $probeSerial.ReadBufferSize=65536; $probeSerial.DtrEnable=$true
try {
    $probeSerial.Open()
    $probe=[MozaDisplaySession]::new(
        [Action[byte[]]]{param($bytes) $probeSerial.Write($bytes,0,$bytes.Length)},
        [Func[int]]{try {return $probeSerial.ReadByte()} catch [TimeoutException] {return -1}},
        [Action[string]]{param($line) Add-Content -LiteralPath $probeLogPath -Value $line -Encoding utf8; [Console]::WriteLine($line)})
    $probe.Run($Seconds)
} finally { $probeSerial.Dispose(); Write-Output "Diagnostic log: $probeLogPath" }
