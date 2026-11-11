"""Read-only ASAR inventory. Extract matching IPC excerpts to local research artifacts."""
import json,re,struct
from pathlib import Path
file=Path('C:/Users/Admin/AppData/Local/Programs/MOZA Racing Lab/resources/app.asar')
with file.open('rb') as f:
    prefix=f.read(16); _, header_size, _, json_size=struct.unpack('<4I',prefix)
    header=json.loads(f.read(json_size)); base=8+header_size
    rows=[]
    def walk(entries,path=''):
        for name,node in entries.items():
            fullname=path+'/'+name
            if 'files' in node: walk(node['files'],fullname)
            elif fullname.endswith(('.js','.json')) and '/node_modules/' not in fullname:
                rows.append((fullname,node))
    walk(header['files'])
    print('App source files:',[(n,v.get('size')) for n,v in rows][:30])
    hits=[]
    for name,node in rows:
        if 'offset' not in node or node.get('size',0)>15000000: continue
        f.seek(base+int(node['offset'])); text=f.read(node['size']).decode('utf8','replace')
        if name == '/out/main/index.js':
            Path('artifacts/moza-coexist/racinglab-main.local.js').write_text(text,encoding='utf8')
        for match in re.finditer(r'(?i)rpcPort|phPubPort|TelemetryRecordRequest|websocket|telemetryData|apexlab/|registerRl|rpc_request',text):
            hits.append({'file':name,'context':text[max(0,match.start()-150):match.end()+250]})
    Path('artifacts/moza-coexist/racinglab-js-clues.json').write_text(json.dumps(hits,ensure_ascii=False,indent=2),encoding='utf8')
    print('Matching contexts:',len(hits))
    for hit in hits[:24]: print(hit)
