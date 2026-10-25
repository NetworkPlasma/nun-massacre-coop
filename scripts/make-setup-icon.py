"""The setup program's icon: the Nun standing over the barbed wire pit, from the game's barbed wire ending (a frame
captured in game, src/NunMassacreCoop.Setup/icon-source.png, 340x340, her face at about (170, 70)), with nothing over her. The large sizes show the
frame whole; the small ones are a closer crop of her face so it still reads.
Writes src/NunMassacreCoop.Setup/icon.ico with a separate image for every size.

Usage: python scripts/make-setup-icon.py
"""
import io, os, struct
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
FOLDER = os.path.join(HERE, '..', 'src', 'NunMassacreCoop.Setup')


def picture(source, size):
    if size >= 128:
        return source.resize((size, size), Image.LANCZOS).convert('RGBA')
    # Closer on her face, so a small icon is still her.
    face = source.crop((60, 15, 280, 235))
    return face.resize((size, size), Image.LANCZOS).convert('RGBA')


def write_ico(images, path):
    blobs = []
    for img in images:
        buffer = io.BytesIO()
        img.save(buffer, 'PNG')
        blobs.append(buffer.getvalue())
    offset = 6 + 16 * len(images)
    out = bytearray(struct.pack('<HHH', 0, 1, len(images)))
    for img, blob in zip(images, blobs):
        side = 0 if img.width >= 256 else img.width
        out += struct.pack('<BBBBHHII', side, side, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    for blob in blobs:
        out += blob
    with open(path, 'wb') as f:
        f.write(out)


if __name__ == '__main__':
    source = Image.open(os.path.join(FOLDER, 'icon-source.png')).convert('RGB')
    images = [picture(source, s) for s in (256, 128, 64, 48, 32, 24, 16)]
    write_ico(images, os.path.join(FOLDER, 'icon.ico'))
    preview = Image.new('RGBA', (256 + 128 + 64 + 48 + 32 + 24 + 16 + 70, 256), (40, 40, 40, 255))
    x = 0
    for img in images:
        preview.alpha_composite(img, (x, 256 - img.height))
        x += img.width + 10
    preview.save(os.path.join(FOLDER, '..', '..', 'artifacts', 'icon-preview.png'))
    print('wrote', os.path.normpath(os.path.join(FOLDER, 'icon.ico')))
