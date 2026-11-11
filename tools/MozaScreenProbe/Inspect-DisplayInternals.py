"""Read-only local binary contexts for the display bridge investigation."""
import re
from pathlib import Path
data=Path('D:/software/Moza Pit House/bin/MOZA Pit House.exe').read_bytes()
for token in [b'DevScreenTestPopup',b'TelemetryDataOutputBuffer',b'TelemetryDataInputBuffer',b'updateTelemetry',b'ScreenProgram',b'TelemetryMessageMap']:
    print('\nTOKEN',token.decode())
    matches=list(re.finditer(re.escape(token),data))
    for m in matches[:12]:
        chunk=data[max(0,m.start()-90):m.end()+700]
        print(m.start(),re.sub(rb'[^\x20-\x7e\n]',b' ',chunk).decode())
