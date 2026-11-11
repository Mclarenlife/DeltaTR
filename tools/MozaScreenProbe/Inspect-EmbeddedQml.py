"""Read-only inspection of compressed Qt source resources in the installed EXE."""
import re,struct,zlib
from pathlib import Path
data=Path('D:/software/Moza Pit House/bin/MOZA Pit House.exe').read_bytes()
out=Path('artifacts/moza-coexist/qml');out.mkdir(exist_ok=True,parents=True)
count=0;hits=0
for m in re.finditer(rb'\x78[\x01\x5e\x9c\xda]',data):
    pos=m.start()
    if pos<8:continue
    size=struct.unpack_from('>I',data,pos-4)[0]
    if not 100<=size<=4000000:continue
    try:
        dec=zlib.decompressobj(); raw=dec.decompress(data[pos:pos+size+512],size+1)
        if len(raw)!=size or not dec.eof:continue
    except zlib.error:continue
    if b'import Qt' not in raw and b'function ' not in raw:continue
    count+=1
    if re.search(rb'TestSendTelemetry|TelemetryDataInput|sendTelemetryData|receiveEncoded|WebSocket|onRpcRequest|Nooxion|[Tt]elemetry|ZmqModules|[Dd]evPopup|screenProgram|ScreenProgram|DevScreenTest',raw):
        file=out/(str(pos)+'.qml');file.write_bytes(raw);hits+=1
        print('Matched',pos,len(raw))
print('Source-like resources',count,'matches',hits)
for token in [b'TestSendTelemetryData',b'receiveEncodedTelemetryData',b'TelemetryDataInputBuffer']:
    for m in list(re.finditer(re.escape(token),data))[:4]:
        print(token.decode(),m.start(),re.sub(rb'[^\x20-\x7e\n]',b' ',data[m.start()-180:m.end()+250]).decode())
