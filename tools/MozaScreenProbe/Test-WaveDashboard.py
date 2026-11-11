"""Check the imported KS Pro dashboard and decode its actual device resources."""
import hashlib
import json
import sys
from pathlib import Path
from PIL import Image, ImageChops, ImageStat

root = Path(__file__).resolve().parents[2]
folder = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'src/DeltaTR.Hardware.Moza/Assets/DeltaTR KS Pro'
doc = json.loads((folder / 'DeltaTR KS Pro.mzdash').read_text(encoding='utf-8'))
controls = doc['children'][0]['children']
poster, wave, shade, ring, *rows, corners = controls
assert doc['name'] == 'DeltaTR KS Pro Fullscreen Radio'
assert 'v1/gameData/PlayerName' not in json.dumps(doc)
assert '等待 DeltaTR' not in json.dumps(doc, ensure_ascii=False)
images = [folder / 'Resource' / c['image']['src'] for c in (ring, poster, wave, corners)]
assert doc['imageResources'] == [c['image']['src'] for c in (poster, wave, ring, corners)]
assert all(hashlib.md5(p.read_bytes()).hexdigest() == p.stem for p in images)
assert sum(p.stat().st_size for p in images) < 600 * 1024

def box(control):
    return tuple(control['general'][k] for k in ('x', 'y', 'width', 'height'))

for control in controls:
    x, y, width, height = box(control)
    assert 0 <= x < x + width <= 780 and 0 <= y < y + height <= 248

# A late or duplicate state/text packet must have no way to restart the decoder.
assert all(c['binding'] == {} and c['general']['visible'] for c in (wave, poster))
assert box(wave) == box(poster) == box(shade)
assert not ring['general']['visible'] and not shade['general']['visible']
assert '=== "listen"' in ring['binding']['general.visible']['methods'][-1]
assert '["listen","think","reply","offline"]' in shade['binding']['general.visible']['methods'][-1]
assert shade['general']['backgroundColor']['color'] == '#CA000000'

assert corners['general']['visible'] and not corners['binding'] and box(corners) == (0, 0, 780, 248)
with Image.open(images[3]) as mask:
    assert mask.size == (780, 248)
    for x in (8, 771):
        for y in (8, 239):
            assert mask.getpixel((x, y)) == (0, 0, 0, 255)
    assert mask.getchannel('A').crop((46, 26, 734, 222)).getbbox() is None

with Image.open(images[1]) as still, Image.open(images[2]) as animated, Image.open(images[0]) as border:
    assert box(ring) == (0, 0, 780, 248) and abs(border.width / border.height - 780 / 248) < .01
    assert animated.size == still.size == box(wave)[2:]
    assert animated.n_frames == border.n_frames == 48
    first = animated.convert('RGB')
    assert ImageChops.difference(still.convert('RGB'), first).getbbox() is None
    previous, energy, edge_colors = None, [], []
    for index in range(48):
        for gif, duration in ((animated, 60), (border, 120)):
            gif.seek(index)
            assert gif.disposal_method == 1 and gif.info['duration'] == duration and gif.info['loop'] == 0
        frame = animated.convert('RGB')
        assert ImageChops.difference(frame, frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)).getbbox() is None
        energy.append(sum(ImageStat.Stat(frame).mean))
        if previous is not None:
            assert ImageChops.difference(frame, previous).getbbox()
        previous = frame
        alpha = border.convert('RGBA').getchannel('A')
        w, h = border.size
        assert alpha.crop((w//5, h//4, w*4//5, h*3//4)).getbbox() is None
        edge = border.convert('RGB')
        # A single inward falloff along the flat upper edge, with no secondary crest.
        for x in (w//4, w//2, w*3//4):
            levels = [sum(edge.getpixel((x, y))) / 3 for y in (2, 4, 6, 9, 13, 17)]
            assert all(a >= b - 3 for a, b in zip(levels, levels[1:]))
        edge_colors.append([edge.getpixel(point) for point in
                            ((w//2, 3), (w-4, h//2), (w//2, h-4), (3, h//2))])
        assert edge.getextrema()[1][1] > 180
        if index == 0:
            first_edge, aperture, steps = edge, alpha, []
        else:
            assert ImageChops.difference(aperture, alpha).getbbox() is None
            steps.append(sum(ImageStat.Stat(ImageChops.difference(last_edge, edge)).mean))
        last_edge = edge
    seam = sum(ImageStat.Stat(ImageChops.difference(last_edge, first_edge)).mean)
    assert min(steps) > 0 and 0 < seam < 2 * max(steps), 'Abrupt transition at the halo loop boundary'
    # Pink and blue must both visit every side, in clockwise order, on each loop.
    for side in range(4):
        pink = edge_colors[12 * side][side]
        blue = edge_colors[(12 * side + 24) % 48][side]
        assert pink[0] > pink[1] + 25 and blue[1] > blue[0] + 20 and blue[2] > blue[0] + 30
    assert min(energy) > max(energy) * .65, 'Black or unusually dim wave frame'
    assert ImageChops.difference(previous, first).getbbox(), 'Duplicate frame at loop boundary'

assert len(rows) == 5
for i, row in enumerate(rows):
    x, _, width, _ = box(row)
    assert x + width / 2 == 390 and width >= 744
    assert row['text']['horizontalAlignment'] == row['text']['verticalAlignment'] == 'AlignCenter'
    assert row['text']['text'] == ''
    for key in ('text.text', 'general.y'):
        field = 'DeltaTRRadio' + (str(i+1) if i else '')
        assert row['binding'][key]['methods'][0] == f'Telemetry.get("v1/gameData/{field}").value'
print('PASS: rounded corners in all states, single diffuse recording halo, continuous wave and five live rows')
