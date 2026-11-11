"""Prepare/install/restore a bounded custom-field experiment; no device settings.
The original installed answer bridge is restored byte-for-byte afterward.
Run the authenticated sender only after Pit House has restarted with the probe.
"""
import argparse, copy, hashlib, json, os, time, hmac
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/moza-custom-field'
BIN = Path('D:/software/Moza Pit House/bin')
FIELD = 'v1/gameData/DeltaTRRadio'
TARGETS = {'qml': BIN/'CustomQmlPlugin/PageManager.qml', 'telemetry': BIN/'GameConfigs/Telemetry.json'}
MANIFEST = OUT/'manifest.json'
sha = lambda data: hashlib.sha256(data).hexdigest()

def manifest(): return json.loads(MANIFEST.read_text(encoding='utf-8'))
def validate(kind):
    if sha((BIN/'MOZA Pit House.exe').read_bytes()).upper() != '7A76BE6E4052404395D1B65BB052CB324D30D1012B5BC409F4F0C78092001B94':
        raise RuntimeError('Unsupported Pit House binary')
    data = manifest()
    for name, path in TARGETS.items():
        if str(path) != data[name]['target'] or sha(path.read_bytes()) != data[name][kind]:
            raise RuntimeError('Unexpected file state: ' + name)
    return data

def prepare():
    if MANIFEST.exists(): raise RuntimeError('Existing backup retained; use status/install/restore')
    OUT.mkdir(parents=True, exist_ok=True)
    original = {name: path.read_bytes() for name, path in TARGETS.items()}
    source = original['qml'].decode('utf-8-sig')
    if source.count('v1/gameData/PlayerName') != 2 or '// DELTATR_PITHOUSE_BRIDGE_V1' not in source:
        raise RuntimeError('Expected installed V1 bridge with exactly two PlayerName references')
    source = source.replace('v1/gameData/PlayerName', FIELD)
    source += '\n// DELTATR_CUSTOM_FIELD_PROBE_V1\n'
    telemetry = json.loads(original['telemetry'])
    if any(x['url'] == FIELD for x in telemetry['sectors']): raise RuntimeError('Field already exists')
    entry = copy.deepcopy(next(x for x in telemetry['sectors'] if x['url'] == 'v1/gameData/PlayerName'))
    entry.update(name='DeltaTRRadio', url=FIELD, package_level=500)
    entry['short_name'] = {'zh_CN':'DeltaTR 专用文字（验证）', 'en_US':'DeltaTR Radio Probe'}
    entry['descs'] = {'zh_CN':'独立语音显示测试字段', 'en_US':'Isolated radio display probe'}
    telemetry['sectors'].append(entry)
    candidates = {'qml':source.encode('utf-8'), 'telemetry':json.dumps(telemetry, ensure_ascii=False, indent=4).encode('utf-8')}
    result = {}
    for name, path in TARGETS.items():
        (OUT/(name+'.original')).write_bytes(original[name])
        (OUT/(name+'.candidate')).write_bytes(candidates[name])
        result[name] = {'target':str(path), 'original':sha(original[name]), 'candidate':sha(candidates[name])}
    MANIFEST.write_text(json.dumps(result, indent=2), encoding='utf-8')
    doc = json.loads((ROOT/'tools/MozaScreenProbe/VoiceTemplate.mzdash').read_text(encoding='utf-8-sig'))
    doc.update(name='DeltaTR Field Probe', imageResources=[], binding={}, actions={})
    doc['window']['GUID'] = 'deltatr-custom-field-probe-20261110-v1'
    screen = doc['children'][0]; screen['name'] = doc['name']
    template = copy.deepcopy(screen['children'][0]); screen['children'] = []
    for idx, (y, height, size, label, color) in enumerate([
        (12, 38, 23, 'DeltaTR 独立字段验证', '#FF89CABD'),
        (60, 116, 29, '等待专用字段 · 尚未验证', '#FFFFFFFF'),
        (192, 40, 20, '游戏转速：等待遥测', '#FF90A2B3')]):
        item = copy.deepcopy(template)
        item.update(id=3100+idx, name=label, children=[], binding={}, actions={})
        item['general'].update(x=16, y=y, width=748, height=height)
        item['text'].update(text=label, fontFamily='Source Han Sans CN', fontSize=size, wrapText=True,
                            horizontalAlignment='AlignCenter', verticalAlignment='AlignCenter',
                            fontColor={'color':color,'type':'SOLID'})
        if idx in (1,2):
            field = FIELD if idx == 1 else 'v1/gameData/Rpm'
            expr = '(_result && String(_result).indexOf("|") >= 0) ? String(_result).split("|").slice(1).join("|") : "等待专用字段 · 尚未验证"' if idx == 1 else '"游戏转速：" + (_result == null ? "等待遥测" : String(_result))'
            item['binding']['text.text'] = {'type':'METHOD_CHAINING','methods':['Telemetry.get("'+field+'").value', expr]}
        screen['children'].append(item)
    dash = OUT/'DeltaTR Field Probe'; dash.mkdir(exist_ok=True)
    (dash/'DeltaTR Field Probe.mzdash').write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding='utf-8')
    print('Prepared custom string field, reversible configuration and 780x248 probe dashboard. No installed files changed.')

def mutate(kind):
    before = 'original' if kind == 'candidate' else 'candidate'
    data = manifest()
    if kind == 'candidate':
        validate('original')
    # Validate every target before writing any; allow recovery of a partially completed install.
    for name,path in TARGETS.items():
        if sha(path.read_bytes()) not in (data[name][before],data[name][kind]): raise RuntimeError('Externally changed: '+name)
        if sha((OUT/(name+'.'+kind)).read_bytes()) != data[name][kind]: raise RuntimeError('Invalid saved file: '+name)
    for name,path in TARGETS.items():
        temporary = path.with_name(path.name+'.deltatr-field.tmp')
        temporary.write_bytes((OUT/(name+'.'+kind)).read_bytes())
        os.replace(temporary,path)
    validate(kind)
    print('Installed field probe; restart Pit House before sending.' if kind == 'candidate' else 'Restored previous bridge and telemetry definitions byte-for-byte; restart Pit House to unload probe.')

def serve(seconds):
    validate('candidate')
    config = json.loads((Path(os.environ['LOCALAPPDATA'])/'DeltaTR/MozaBridge/connection.json').read_text(encoding='utf-8-sig'))
    session = os.urandom(16).hex()
    started = time.monotonic(); last_log = 0; count = 0; last_ack = 0
    log_path = OUT/('sender-'+time.strftime('%Y%m%d-%H%M%S')+'.log')
    def log(message):
        with log_path.open('a',encoding='utf-8') as file: file.write(time.strftime('%Y-%m-%d %H:%M:%S')+' '+message+'\n')
        print(message, flush=True)
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass
        def do_GET(self):
            nonlocal count,last_log,last_ack
            url = urlparse(self.path)
            if (url.path != '/v1/screen' or self.headers.get('Origin') or
                self.headers.get('Host') != '127.0.0.1:'+str(config['Port']) or
                not hmac.compare_digest(self.headers.get('X-DeltaTR-Key',''),config['Key'])):
                self.send_error(403); return
            elapsed = time.monotonic()-started
            # Updating phase then HOLD: same sequence, QML stops submitting new values.
            hold = elapsed % 60 >= 30
            count = int(elapsed//60)*11 + (11 if hold else int(elapsed%60)//3+1)
            text = ('保持测试：此行应持续保留 #'+str(count)) if hold else ('独立字段中文验证 #'+str(count))
            query = parse_qs(url.query)
            try: ack = int(query.get('ack',['0'])[0])
            except ValueError: ack=0
            if query.get('session',[''])[0] == session and 0 < ack <= count: last_ack=ack
            packet = dict(version=1, session=session, seq=count, enabled=elapsed<seconds,
                          allowReset=True, text='reply|'+text, expires=int(time.time()*1000)+2000)
            body = json.dumps(packet,ensure_ascii=False).encode('utf-8')
            self.send_response(200); self.send_header('Content-Type','application/json; charset=utf-8')
            self.send_header('Content-Length',str(len(body))); self.send_header('Connection','close'); self.end_headers()
            self.wfile.write(body)
            if elapsed-last_log>=3:
                log('phase='+('HOLD' if hold else 'UPDATE')+' seq='+str(count)+' qml_ack='+str(last_ack))
                last_log=elapsed
    server=HTTPServer(('127.0.0.1',config['Port']),Handler); server.timeout=.5
    log('CUSTOM FIELD ONLY '+FIELD+'; duration='+str(seconds)+'s; acknowledgements are not physical readback')
    try:
        while time.monotonic()-started < seconds+2: server.handle_request()
    finally: server.server_close(); log('Sender stopped; last_qml_ack='+str(last_ack))

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('action',choices=['prepare','install','restore','status','serve'])
p.add_argument('--seconds',type=int,default=120,choices=range(30,301))
a=p.parse_args()
if a.action=='prepare': prepare()
elif a.action in ('install','restore'): mutate('candidate' if a.action=='install' else 'original')
elif a.action=='serve': serve(a.seconds)
else:
    data=manifest()
    print(json.dumps({name: {'original':sha(path.read_bytes())==data[name]['original'], 'candidate':sha(path.read_bytes())==data[name]['candidate']} for name,path in TARGETS.items()}))
