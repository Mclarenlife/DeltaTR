"""Read-only static inspection of local MOZA components; never loads vendor code.
Produces filtered symbol/string clues, not a statement of API support.
"""
import argparse, json, re
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--bin', default='D:/software/Moza Pit House/bin')
p.add_argument('--output', required=True)
args = p.parse_args()
patterns = re.compile(r'telemetry|websocket|coap|remoteobject|tcp://|ipc://|127\.0\.0\.1|localhost|PlayerName|GameData|SdkServer|sdkserver', re.I)
report = {}
for name in ['MOZA Pit House.exe', 'MOZADeviceService.exe', 'MOZA Dashboard Studio.exe', 'RacingLab.exe']:
    file = Path(args.bin) / name
    data = file.read_bytes()
    strings = {m.group().decode('ascii') for m in re.finditer(rb'[\x20-\x7e]{5,300}', data)}
    strings.update(m.group().decode('utf-16le') for m in re.finditer(rb'(?:[\x20-\x7e]\x00){5,300}', data))
    report[name] = sorted(s for s in strings if patterns.search(s))
out = Path(args.output); out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
for name, values in report.items():
    print(name, len(values))
    print('\n'.join(values[:95]))
