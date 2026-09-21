"""Build overlap-free static Roboto faces for GDI+/LiveSplit.

Usage: python tools/build_livesplit_unlinked_pack.py SOURCE [--output DEST]
SOURCE can be an extracted Google Fonts Roboto folder or its static subfolder.
Requires fonttools and skia-pathops. Variable fonts are deliberately excluded:
changing their contours without rebuilding variation deltas corrupts them.
"""
from __future__ import annotations

import argparse
import shutil
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.ttLib.removeOverlaps import removeOverlaps

BUCKETS = {
    'Roboto_Condensed-': 'Roboto Cond LSFix',
    'Roboto_SemiCondensed-': 'Roboto SemiC LSFix',
    'Roboto-': 'Roboto LSFix',
}
WEIGHTS = {'Thin', 'ExtraLight', 'Light', 'Regular', 'Medium', 'SemiBold', 'Bold', 'ExtraBold', 'Black'}


def font_names(stem: str) -> tuple[str, str, str]:
    for prefix, bucket in BUCKETS.items():
        if stem.startswith(prefix):
            style = stem[len(prefix):]
            italic = style.endswith('Italic')
            weight = (style[:-6] if italic else style) or 'Regular'
            if weight not in WEIGHTS:
                raise ValueError(f'Unsupported static face: {stem}')
            family = f'{bucket} {weight}'
            subfamily = 'Italic' if italic else 'Regular'
            ps = family.replace(' ', '') + ('Italic' if italic else '')
            assert len(family) <= 31, family  # GDI face-name limit
            return family, subfamily, ps
    raise ValueError(f'Unexpected font: {stem}')


def process_font(src: Path, dst: Path) -> None:
    family, subfamily, ps = font_names(src.stem)
    with TTFont(src, recalcBBoxes=True, recalcTimestamp=False) as font:
        if 'fvar' in font:
            raise ValueError(f'Use static faces, not a variable font: {src}')
        # Fail on any unsuccessful glyph; never ship a silently partial repair.
        # Hint programs referencing changed point indices must not survive surgery.
        horizontal_metrics = font['hmtx'].metrics.copy()
        removeOverlaps(font, removeHinting=True, ignoreErrors=False)
        font['hmtx'].metrics = horizontal_metrics
        full = f'{family} {subfamily}'
        names = {1: family, 2: subfamily, 3: f'LiveSplitFix-v2;{ps}',
                 4: full, 6: ps, 16: family, 17: subfamily,
                 21: family, 22: subfamily}
        table = font['name']
        for record in list(table.names):
            if record.nameID in names:
                table.setName(names[record.nameID], record.nameID,
                              record.platformID, record.platEncID, record.langID)
        for name_id, value in names.items():
            table.setName(value, name_id, 3, 1, 0x409)
        italic = subfamily == 'Italic'
        # Each weight is its own Regular/Italic family, so Bold must stay clear.
        font['head'].macStyle = (font['head'].macStyle & ~3) | (2 if italic else 0)
        selection = font['OS/2'].fsSelection & ~((1 << 0) | (1 << 5) | (1 << 6) | (1 << 9))
        font['OS/2'].fsSelection = selection | (1 if italic else 1 << 6)
        font['maxp'].recalc(font)
        dst.parent.mkdir(parents=True, exist_ok=True)
        font.save(dst)
    # Verify serialized data, including cmap and positioning metrics.
    with TTFont(src) as original, TTFont(dst, checkChecksums=2) as fixed:
        assert fixed.getBestCmap() == original.getBestCmap()
        assert fixed['hmtx'].metrics == original['hmtx'].metrics
        assert fixed.getGlyphOrder() == original.getGlyphOrder()
        assert fixed['name'].getDebugName(1) == family


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    source = args.source.resolve()
    static = source / 'static' if (source / 'static').is_dir() else source
    output = args.output or source / 'Roboto_Original_FIXED_UNLINKED_FOR_LIVESPLIT'
    output = output.resolve()
    if output == static:
        parser.error('Output must differ from the source folder')
    files = sorted(static.glob('*.ttf'))
    if not files:
        parser.error(f'No static TTF files in {static}')
    for src in files:
        process_font(src, output / src.name)
        print('OK', src.name, flush=True)
    license_path = next((p for p in (source / 'OFL.txt', source.parent / 'OFL.txt') if p.exists()), None)
    if license_path:
        shutil.copy2(license_path, output / 'OFL.txt')
    (output / 'README.txt').write_text(
        'Roboto LSFix — overlap-free static fonts for LiveSplit\n\n'
        'Modified from the supplied Roboto font family; licensed under OFL.txt.\n'
        'Install the TTF files, restart LiveSplit, then select a Roboto LSFix,\n'
        'Roboto Cond LSFix, or Roboto SemiC LSFix family in the font picker.\n'
        'Each weight is a separate family with Regular and Italic styles.\n'
        'Select the weight in the family name; do not enable synthetic Bold.\n'
        'Contours were unioned, obsolete hinting removed, and names/style flags\n'
        'made consistent. Character mappings and horizontal metrics are preserved.\n'
        'Variable source fonts remain unchanged; this pack contains static faces.\n',
        encoding='utf-8')
    print(f'Done: {len(files)}/{len(files)} -> {output}')


if __name__ == '__main__':
    main()
