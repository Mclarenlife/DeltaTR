"""Procedural symmetric soundwave animation for KS Pro's 780 x 248 screen.
Pillow renders original frames; native dashboard text stays live and separate.
"""
from pathlib import Path
import copy, hashlib, json, math, re
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from recording_halo import screen_mask, write_halo

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'src/DeltaTR.Hardware.Moza/Assets/DeltaTR KS Pro'
RES = DEST / 'Resource/MD5'
PREVIEW = ROOT / 'artifacts/moza-wave-design'
RES.mkdir(parents=True, exist_ok=True)
PREVIEW.mkdir(parents=True, exist_ok=True)
dashboard = DEST / 'DeltaTR KS Pro.mzdash'
old_resources = json.loads(dashboard.read_text(encoding='utf-8')).get('imageResources', []) if dashboard.exists() else []
doc = json.loads((Path(__file__).parent / 'VoiceTemplate.mzdash').read_text(encoding='utf-8-sig'))
doc['name'] = 'DeltaTR KS Pro Fullscreen Radio'
# Versioned identity avoids Pit House retaining the old dashboard's cached layout.
doc['window']['GUID'] = 'deltatr-ks-pro-radio-rounded-v9'
doc['lastModified'] = 1794384004
doc['imageResources'] = []
screen = doc['children'][0]
screen['name'] = doc['name']
base = copy.deepcopy(screen['children'][0])
screen['children'] = []
state = 'String(_result || "").split("|")[0]'

def binding(expr, row=0):
    return {'type': 'METHOD_CHAINING', 'methods': [f'Telemetry.get("v1/gameData/DeltaTRRadio{row + 1 if row else ""}").value', expr]}

def control(i, name, x, y, w, h):
    c = copy.deepcopy(base)
    c.update(id=i, name=name, binding={})
    c['general'].update(x=x, y=y, width=w, height=h, backgroundColor={'color': '#00000000', 'type': 'SOLID'})
    c['text'].update(fontFamily='Source Han Sans CN', wrapText=True,
                     horizontalAlignment='AlignCenter', verticalAlignment='AlignCenter',
                     paddingLeft=0, paddingRight=0, paddingTop=0, paddingBottom=0)
    return c

def waveform(frame):
    phase = 2 * math.pi * frame / 48
    layer = Image.new('RGB', (780, 248))
    draw = ImageDraw.Draw(layer)
    # Construct one half, then mirror pixel-for-pixel about the screen center.
    for i in range(32):
        distance = (i + .5) / 32
        envelope = math.exp(-2.6 * distance * distance)
        motion = .62 + .24 * math.cos(phase - distance * 7) + .14 * math.cos(2 * phase + distance * 11)
        half_height = 3 + 58 * envelope * motion
        x = 384 - i * 11
        t = i / 31
        lavender, ice_blue = (203, 180, 218), (146, 180, 207)
        color = tuple(round(a + (b - a) * t) for a, b in zip(lavender, ice_blue))
        draw.rounded_rectangle((x - 3, 123.5 - half_height, x + 3, 123.5 + half_height), radius=3, fill=color)
    left = layer.crop((0, 0, 390, 248))
    layer.paste(left.transpose(Image.Transpose.FLIP_LEFT_RIGHT), (390, 0))
    return Image.blend(layer, layer.filter(ImageFilter.GaussianBlur(9)), .32).crop(WAVE_BOX)

def animation(name, frames):
    # A fixed palette keeps unchanged pixels identical across frames. Disposal 1
    # retains the previous frame; the encoder only stores changed rectangles.
    palette = frames[0].quantize(colors=32)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    path = PREVIEW / (name + '.gif')
    indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=60,
                    loop=0, disposal=1, optimize=True, palette=palette.getpalette())
    return path

def image_control(i, name, file, box, visible=True):
    data = file.read_bytes()
    path = RES / (hashlib.md5(data).hexdigest() + file.suffix)
    path.write_bytes(data)
    src = 'MD5/' + path.name
    doc['imageResources'].append(src)
    x, y, right, bottom = box
    item = control(i, name, x, y, right-x, bottom-y)
    item['general']['visible'] = visible
    item.pop('text', None); item.pop('textEffect', None)
    item.update(type='Image.qml', image={'src': src})
    screen['children'].append(item)
    return item

WAVE_BOX = (16, 48, 764, 200)
wave = animation('wave', [waveform(n) for n in range(48)])
poster = PREVIEW / 'wave-first.png'
with Image.open(wave) as encoded:
    encoded.convert('RGB').save(poster)
image_control(2101, 'Wave first frame', poster, WAVE_BOX)
image_control(2102, 'Continuous wave', wave, WAVE_BOX)

# Only the native shade and perimeter respond to telemetry. Text re-delivery
# and idle heartbeats never change the wave's source, visibility or geometry.
shade = control(2103, 'Text contrast', 16, 48, 748, 152)
shade['text']['text'] = ''
shade['general'].update(visible=False, backgroundColor={'type': 'SOLID', 'color': '#CA000000'})
shade['binding']['general.visible'] = binding(f'["listen","think","reply","offline"].includes({state})')
screen['children'].append(shade)

halo = PREVIEW / 'recording-halo.gif'
write_halo(halo)
ring = image_control(2100, 'Recording halo', halo, (0, 0, 780, 248), False)
ring['binding']['general.visible'] = binding(f'{state} === "listen"')

# Each row is sent independently to fit the firmware's UTF-8 field limit.
# State and physical y coordinate precede the text; pipes in a driver's reply survive.
for row in range(5):
    text = control(2201 + row, f'Radio line {row+1}', 12, 102, 756, 44)
    text['text'].update(text='', fontSize=30, fontWeight=500, wrapText=False,
                        fontColor={'color': '#FFF2F8F7', 'type': 'SOLID'})
    text['binding']['text.text'] = binding('String(_result || "").split("|").slice(2).join("|")', row)
    text['binding']['general.y'] = binding('Number(String(_result || "").split("|")[1]) || 102', row)
    screen['children'].append(text)

mask = screen_mask()
mask_file = PREVIEW / 'screen-corners.png'
mask.save(mask_file)
image_control(2300, 'Rounded screen corners', mask_file, (0, 0, 780, 248))
dashboard.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding='utf-8')

# Remove only obsolete generated hashes named by the previous project dashboard.
for name in old_resources:
    if re.fullmatch(r'MD5/[0-9a-f]{32}\.(gif|png)', name) and name not in doc['imageResources']:
        path = RES / Path(name).name
        if path.is_file():
            try: path.unlink()
            except PermissionError: print('Unused resource is open in the editor; retained:', path.name)

font_path = Path('C:/Windows/Fonts/msyh.ttc')
font = lambda size: ImageFont.truetype(str(font_path), size)
for mode, lines in [('idle', []), ('listen', ['工程师，我应该第几圈进站？']),
                    ('reply', ['你目前第三，落后前车一点二秒。', '保持当前节奏。'])]:
    preview = Image.new('RGB', (780, 248))
    with Image.open(wave) as encoded:
        encoded.seek(12)
        tile = encoded.convert('RGB')
    if mode != 'idle':
        tile = Image.blend(tile, Image.new('RGB', tile.size), 202/255)
    preview.paste(tile, WAVE_BOX)
    if mode == 'listen':
        with Image.open(halo) as edge:
            edge.seek(6)
            light = edge.convert('RGBA').resize(preview.size, Image.Resampling.BILINEAR)
            preview.paste(light, mask=light.getchannel('A'))
    draw = ImageDraw.Draw(preview)
    for n, line in enumerate(lines):
        draw.text((390, 124 + (n - (len(lines)-1)/2)*44), line, font=font(30), anchor='mm', fill='#f2f8f7')
    preview.paste(mask, mask=mask.getchannel('A'))
    preview.save(PREVIEW / (mode + '.png'))
print(dashboard)
