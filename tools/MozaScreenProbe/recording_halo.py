"""Render the KS Pro recording indicator, or preview it without writing a dashboard."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SIZE = (780, 248)
FRAMES = 48
DURATION_MS = 120
HALO_SIZE = (352, 112)
CORNER_RADIUS = 44
SCREEN_INSET = 4


def screen_geometry():
    width, height = SIZE
    y, x = np.mgrid[:height, :width].astype(np.float32)
    x += .5 - width / 2
    y += .5 - height / 2
    qx = abs(x) - (width / 2 - SCREEN_INSET - CORNER_RADIUS)
    qy = abs(y) - (height / 2 - SCREEN_INSET - CORNER_RADIUS)
    distance = CORNER_RADIUS - np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) - np.minimum(np.maximum(qx, qy), 0)
    return x, y, distance


def screen_mask():
    # The full-resolution overlay clips every layer, including native text.
    _, _, distance = screen_geometry()
    inside = np.clip(distance + .5, 0, 1)
    rim = .12 * np.exp(-.5 * (np.maximum(distance, 0) / 3.0) ** 2) * inside
    alpha = 1 - inside + rim
    rgba = np.zeros((*distance.shape, 4), dtype=np.uint8)
    rgba[..., :3] = np.rint(170 * rim / np.maximum(alpha, 1e-8))[..., None]
    rgba[..., 3] = np.rint(255 * alpha)
    return Image.fromarray(rgba)


def render_frames():
    x, y, distance = screen_geometry()
    # Clockwise arc length from the top midpoint, including each circular corner.
    flat_x = SIZE[0] / 2 - SCREEN_INSET - CORNER_RADIUS
    flat_y = SIZE[1] / 2 - SCREEN_INSET - CORNER_RADIUS
    quarter = flat_x + np.pi * CORNER_RADIUS / 2 + flat_y
    corner_angle = np.arctan2(np.maximum(abs(x) - flat_x, 0), np.maximum(abs(y) - flat_y, 0))
    arc = np.minimum(abs(x), flat_x) + CORNER_RADIUS * corner_angle + np.maximum(flat_y - abs(y), 0)
    arc = np.where(y > 0, 2 * quarter - arc, arc)
    arc = np.where(x < 0, 4 * quarter - arc, arc)
    angle = arc * (np.pi / (2 * quarter))
    depth = np.maximum(distance, 0)
    # Fixed cross-section: only hue and luminous intensity travel along the edge.
    falloff = .52 * np.exp(-.5 * (depth / 6.5) ** 2) + .48 * np.exp(-.5 * (depth / 17) ** 2)
    falloff *= np.clip((42 - depth) / 8, 0, 1)
    stops = [0, .25, .52, .78, 1]
    colors = np.array([(237, 132, 197), (159, 126, 230), (125, 151, 242),
                       (106, 182, 232), (157, 223, 240)])
    for i in range(FRAMES):
        phase = 2 * np.pi * i / FRAMES
        moving_angle = angle - phase
        across = .5 - .5 * np.cos(moving_angle)
        hue = np.stack([np.interp(across, stops, channel) for channel in colors.T], axis=-1)
        intensity = .96 + .09 * np.cos(2 * moving_angle)
        rgb = hue * (falloff * intensity)[..., None]
        yield Image.fromarray(np.uint8(np.clip(rgb, 0, 255)))


def encode(frames, path, colors, duration, aperture=None):
    # Sample the whole loop. A fixed palette avoids hue jumps at frame boundaries.
    pixels = np.concatenate([np.asarray(f)[::2, ::2].reshape(-1, 3) for f in frames[::8]])
    maximum = pixels.max(axis=1)
    samples = [pixels[maximum < 8][::32], pixels[(maximum >= 8) & (maximum < 48)][::4],
               pixels[maximum >= 48]]
    pixels = np.concatenate(samples)
    sample = Image.fromarray(pixels[:len(pixels)//64*64].reshape(-1, 64, 3))
    palette = sample.quantize(colors=colors)
    indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    save_options = {}
    table = palette.getpalette()
    if aperture is not None:
        # Tone the shared halo palette, keeping frame indices and luma intact.
        rgb = np.asarray(table).reshape(-1, 3)
        luma = (rgb @ [.2126, .7152, .0722])[:, None]
        table = np.rint(luma + .68 * (rgb - luma)).astype(np.uint8).ravel().tolist()
        clear = 255
        table += [0] * (768 - len(table))
        table[-3:] = [1, 0, 1]  # A unique palette entry for the permanent clear center.
        for frame in indexed:
            frame.putpalette(table)
            frame.paste(clear, mask=aperture)
        save_options['transparency'] = clear
        save_options['palette'] = table
    indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=duration,
                    loop=0, disposal=1, optimize=True, **save_options)


def write_halo(path):
    aperture = Image.new('L', HALO_SIZE)
    w, h = HALO_SIZE
    margin = round(46 * w / SIZE[0])
    ImageDraw.Draw(aperture).rounded_rectangle((margin, margin, w-1-margin, h-1-margin),
                                               radius=margin//2, fill=255)
    frames = [f.resize(HALO_SIZE, Image.Resampling.LANCZOS) for f in render_frames()]
    encode(frames, path, 192, DURATION_MS, aperture)


def preview(output):
    output.mkdir(parents=True, exist_ok=True)
    halo = output / 'recording-halo.gif'
    write_halo(halo)
    folder = Path(__file__).resolve().parents[2] / 'src/DeltaTR.Hardware.Moza/Assets/DeltaTR KS Pro'
    dash = json.loads((folder / 'DeltaTR KS Pro.mzdash').read_text(encoding='utf-8'))
    wave = next(c for c in dash['children'][0]['children'] if c['name'] == 'Continuous wave')
    font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 30)
    frames = []
    mask = screen_mask()
    with Image.open(halo) as edge, Image.open(folder / 'Resource' / wave['image']['src']) as background:
        for i in range(FRAMES * 2):
            edge.seek(i // 2)
            frame = Image.new('RGB', SIZE)
            background.seek(i % background.n_frames)
            tile = background.convert('RGB')
            frame.paste(Image.blend(tile, Image.new('RGB', tile.size), 202 / 255),
                        (wave['general']['x'], wave['general']['y']))
            light = edge.convert('RGBA').resize(SIZE, Image.Resampling.BILINEAR)
            frame.paste(light, mask=light.getchannel('A'))
            ImageDraw.Draw(frame).text((390, 124), '当前四条轮胎的温度是多少？',
                                       font=font, anchor='mm', fill='#f2f8f7')
            frame.paste(mask, mask=mask.getchannel('A'))
            frames.append(frame)
    frames[10].save(output / 'listening-still.png')
    encode(frames, output / 'KS-Pro-Listening.gif', 256, DURATION_MS // 2)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    preview(parser.parse_args().output)
