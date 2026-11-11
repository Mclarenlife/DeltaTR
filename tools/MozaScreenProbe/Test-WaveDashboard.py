"""Validate generated KS Pro assets and native text geometry without hardware."""
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageStat

root = Path(__file__).resolve().parents[2]
folder = root / 'src/DeltaTR.Hardware.Moza/Assets/DeltaTR KS Pro'
doc = json.loads((folder / 'DeltaTR KS Pro.mzdash').read_text(encoding='utf-8'))
controls = doc['children'][0]['children']
assert doc['name'] == 'DeltaTR KS Pro Wave Radio'
assert 'v1/gameData/PlayerName' not in json.dumps(doc)
assert json.dumps(doc).count('v1/gameData/DeltaTRRadio') == 7
assert len(controls) == 6
brightness = []
for resource in doc['imageResources']:
    gif = Image.open(folder / 'Resource' / resource)
    assert gif.size == (780, 248) and gif.n_frames == 48
    energy = []
    for frame in range(gif.n_frames):
        gif.seek(frame)
        rgb = gif.convert('RGB')
        assert ImageChops.difference(rgb, rgb.transpose(Image.Transpose.FLIP_LEFT_RIGHT)).getbbox() is None
        assert gif.info['duration'] == 60
        energy.append(sum(ImageStat.Stat(rgb).mean))
    assert max(energy) - min(energy) > .1, 'Animation has no motion'
    brightness.append(sum(energy) / len(energy))
assert brightness[1] < brightness[0] * .35, 'Listening waveform must be visibly dimmer'
for control in controls:
    g = control['general']
    assert 0 <= g['x'] and g['x'] + g['width'] <= 780
    assert 0 <= g['y'] and g['y'] + g['height'] <= 248
    if 'text' in control:
        assert control['text']['horizontalAlignment'] == 'AlignCenter'
        assert control['text']['verticalAlignment'] == 'AlignCenter'
        assert g['x'] + g['width']/2 == 390
print('PASS: 144 animated frames, exact symmetry, listening dimming, centered native text, screen bounds')
