"""Extract only local IPC-related static clues for a compatibility investigation."""
import json, re
from pathlib import Path
out = Path('artifacts/moza-coexist'); out.mkdir(parents=True, exist_ok=True)
for name, file in [('pithouse',Path('D:/software/Moza Pit House/bin/MOZA Pit House.exe')),
                   ('racinglab',Path('D:/software/Moza Pit House/bin/RacingLab.exe'))]:
    data=file.read_bytes()
    strings={m.group().decode('ascii') for m in re.finditer(rb'[\x20-\x7e]{4,200}',data)}
    strings.update(m.group().decode('utf-16le') for m in re.finditer(rb'(?:[\x20-\x7e]\x00){4,200}',data))
    hits=sorted(s for s in strings if re.search(r'ApexLab|apexlab|[Rr]pc|TelemetryDataInput|TestSendTelemetry|EncodedTelemetry|MessageType|CommandType',s) and not re.search(r'@@|lambda|^\.\?',s))
    (out/(name+'-ipc.txt')).write_text('\n'.join(hits),encoding='utf-8')
    print(name+'\n'+'\n'.join(hits))
