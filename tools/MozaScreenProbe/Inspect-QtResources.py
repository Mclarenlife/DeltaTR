"""Read-only Qt RCC v2/v3 resource inspection, for local compatibility research.
Uses documented Qt resource layout, not a Qt code dependency; never edits the bundle.
"""
import json,re,struct,zlib
from pathlib import Path
data=Path('D:/software/Moza Pit House/bin/resource.rcc').read_bytes()
magic,version,tree,blob,names=struct.unpack_from('>4s4I',data)
assert magic==b'qres' and version in (2,3)
out=Path('artifacts/moza-coexist/qml');out.mkdir(parents=True,exist_ok=True)
hits=[]
def u32(at): return struct.unpack_from('>I',data,at)[0]
def walk(index,path='',depth=0):
    assert depth<40
    at=tree+index*22
    no,flags=struct.unpack_from('>IH',data,at)
    name_at=names+no
    length=struct.unpack_from('>H',data,name_at)[0]
    name=data[name_at+6:name_at+6+length*2].decode('utf-16be')
    full=path+'/'+name
    if flags & 2:
        count,first=struct.unpack_from('>II',data,at+6)
        for n in range(first,first+count):walk(n,full,depth+1)
    elif full.endswith(('.qml','.js','.json')):
        pos=blob+u32(at+10); size=u32(pos); content=data[pos+4:pos+4+size]
        if flags & 1:content=zlib.decompress(content[4:])
        if flags & 4:return
        text=content.decode('utf8','replace')
        if re.search(r'TestSendTelemetry|TelemetryDataInputBuffer|sendTelemetryData|receiveEncodedTelemetry|onRpcRequest|WebSocketServer|telemetryDataInputBuffer|NooxionDataReceiver',text):
            target=out/(str(index)+'-'+Path(full).name); target.write_text(text,encoding='utf8')
            hits.append({'resource':full,'local':str(target)})
walk(0)
(out/'matches.json').write_text(json.dumps(hits,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(hits,ensure_ascii=False,indent=2))
