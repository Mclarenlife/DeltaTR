"""Procedural symmetric soundwave animation for KS Pro's 780 x 248 screen.
Pillow renders original frames; native dashboard text stays live and separate.
"""
from pathlib import Path
import copy, hashlib, json, math, re
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'src/DeltaTR.Hardware.Moza/Assets/DeltaTR KS Pro'
RES = DEST / 'Resource/MD5'
PREVIEW = ROOT / 'artifacts/moza-wave-design'
RES.mkdir(parents=True, exist_ok=True)
PREVIEW.mkdir(parents=True, exist_ok=True)
dashboard = DEST / 'DeltaTR KS Pro.mzdash'
old_resources = json.loads(dashboard.read_text(encoding='utf-8')).get('imageResources', []) if dashboard.exists() else []
doc = json.loads((Path(__file__).parent / 'VoiceTemplate.mzdash').read_text(encoding='utf-8-sig'))
doc['name'] = 'DeltaTR KS Pro Wave Radio'
# Versioned identity avoids Pit House retaining the old dashboard's cached layout.
doc['window']['GUID'] = 'deltatr-ks-pro-radio-20261110-v3'
doc['imageResources'] = []
screen = doc['children'][0]
screen['name'] = doc['name']
base = copy.deepcopy(screen['children'][0])
screen['children'] = []
state = 'String(_result || "").split("|")[0]'

def binding(expr):
    return {'type': 'METHOD_CHAINING', 'methods': ['Telemetry.get("v1/gameData/DeltaTRRadio").value', expr]}

def control(i, name, x, y, w, h):
    c = copy.deepcopy(base)
    c.update(id=i, name=name, binding={})
    c['general'].update(x=x, y=y, width=w, height=h, backgroundColor={'color': '#00000000', 'type': 'SOLID'})
    c['text'].update(fontFamily='Source Han Sans CN', wrapText=True,
                     horizontalAlignment='AlignCenter', verticalAlignment='AlignCenter',
                     paddingLeft=0, paddingRight=0, paddingTop=0, paddingBottom=0)
    return c

def waveform(frame, mode):
    phase = 2 * math.pi * frame / 48
    layer = Image.new('RGB', (780, 248))
    draw = ImageDraw.Draw(layer)
    energy = {'idle': .38, 'listen': 1.0, 'reply': .62}[mode]
    # Construct one half, then mirror pixel-for-pixel about the screen center.
    for i in range(32):
        distance = (i + .5) / 32
        envelope = math.exp(-2.6 * distance * distance)
        motion = .62 + .24 * math.cos(phase - distance * 7) + .14 * math.cos(2 * phase + distance * 11)
        half_height = 3 + (34 + 48 * energy) * envelope * motion
        x = 384 - i * 11
        t = i / 31
        color = (int(95 - 50 * t), int(232 - 54 * t), int(220 + 30 * t))
        if mode == 'reply': color = (int(95 - 30*t), int(188 + 20*t), 248)
        draw.rounded_rectangle((x - 3, 123.5 - half_height, x + 3, 123.5 + half_height), radius=3, fill=color)
    left = layer.crop((0, 0, 390, 248))
    layer.paste(left.transpose(Image.Transpose.FLIP_LEFT_RIGHT), (390, 0))
    layer = Image.blend(layer, layer.filter(ImageFilter.GaussianBlur(9)), .32)
    if mode != 'idle':
        mask = Image.new('L', layer.size)
        draw_mask = ImageDraw.Draw(mask)
        for x in range(780):
            edge = abs(x - 389.5) / 389.5
            brightness = (.11 + .16 * edge**1.7) if mode == 'listen' else (.16 + .30 * edge**1.7)
            draw_mask.line((x, 0, x, 247), fill=round(255 * brightness))
        layer = Image.composite(layer, Image.new('RGB', layer.size), mask)
    return layer

conditions = {'idle': f'{state} === "idle"',
              'listen': f'!["idle","reply"].includes({state})', 'reply': f'{state} === "reply"'}
for index, mode in enumerate(['idle', 'listen', 'reply']):
    frames = [waveform(n, mode) for n in range(48)]
    temporary = PREVIEW / (mode + '.gif')
    frames[0].save(temporary, save_all=True, append_images=frames[1:], duration=60, loop=0, optimize=False, disposal=2)
    data = temporary.read_bytes()
    path = RES / (hashlib.md5(data).hexdigest() + '.gif')
    path.write_bytes(data)
    src = 'MD5/' + path.name
    doc['imageResources'].append(src)
    item = control(2100 + index, 'Wave ' + mode, 0, 0, 780, 248)
    item.pop('text', None); item.pop('textEffect', None)
    item.update(type='Image.qml', image={'src': src})
    item['binding']['general.visible'] = binding(conditions[mode])
    screen['children'].append(item)

title = control(2200, 'Centered state', 40, 18, 700, 28)
title['text'].update(text='DELTATR', fontSize=18, fontColor={'color': '#FF7E9B9F', 'type': 'SOLID'})
title['binding']['text.text'] = binding(f'({{listen:"正在聆听",think:"正在处理",reply:"车队工程师",offline:"无线电未连接"}})[{state}] || "DELTATR"')
screen['children'].append(title)
text = control(2201, 'Centered live transcript and answer', 52, 70, 676, 108)
text['text'].update(text='等待 DeltaTR 连接', fontSize=30, fontWeight=500,
                    fontColor={'color': '#FFF2F8F7', 'type': 'SOLID'})
text['binding']['text.text'] = binding('String(_result || "").includes("|") ? String(_result).substring(String(_result).indexOf("|")+1) : "等待 DeltaTR 连接"')
text['binding']['general.visible'] = binding(f'{state} !== "idle"')
screen['children'].append(text)
footer = control(2202, 'Centered hint', 40, 207, 700, 24)
footer['text'].update(text='TEAM RADIO', fontSize=15, fontColor={'color': '#FF607B81', 'type': 'SOLID'})
footer['binding']['text.text'] = binding(f'({{listen:"识别预览 · 松开发送",think:"等待确认与回复",reply:"DELTATR  /  TEAM RADIO",offline:"连接游戏或启动演示"}})[{state}] || "TEAM RADIO"')
screen['children'].append(footer)
dashboard.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding='utf-8')

# Remove only obsolete generated hashes named by the previous project dashboard.
for name in old_resources:
    if re.fullmatch(r'MD5/[0-9a-f]{32}\.gif', name) and name not in doc['imageResources']:
        path = RES / Path(name).name
        if path.is_file():
            try: path.unlink()
            except PermissionError: print('Unused resource is open in the editor; retained:', path.name)

font_path = Path('C:/Windows/Fonts/msyh.ttc')
font = lambda size: ImageFont.truetype(str(font_path), size)
for mode, heading, lines, hint in [
    ('idle', 'DELTATR', [], 'TEAM RADIO'),
    ('listen', '正在聆听', ['工程师，我应该第几圈进站？'], '识别预览 · 松开发送'),
    ('reply', '车队工程师', ['建议第五圈进站。', '保持当前节奏，留意前车距离。'], 'DELTATR  /  TEAM RADIO')]:
    preview = waveform(12, mode)
    draw = ImageDraw.Draw(preview)
    draw.text((390, 32), heading, font=font(18), anchor='mm', fill='#7e9b9f')
    for n, line in enumerate(lines): draw.text((390, 124 + (n - (len(lines)-1)/2)*40), line, font=font(30), anchor='mm', fill='#f2f8f7')
    draw.text((390, 219), hint, font=font(15), anchor='mm', fill='#607b81')
    preview.save(PREVIEW / (mode + '.png'))
print('780 x 248; 3 symmetric waveform loops x 48 frames; centered live text')
print(dashboard)
