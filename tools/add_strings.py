#!/usr/bin/env python3
"""Adds UI strings to the repo's .resx tables (English, zh-Hant, zh-Hans) and regenerates the designer class.

Usage: add_strings.py <entries.py>   where entries.py defines ENTRIES = [(key, en, hant, hans), ...]
The repo's .resx files are the source of truth; existing keys are left alone (an error if the text differs).
"""
import pathlib
import re
import runpy
import subprocess
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = pathlib.Path(__file__).resolve().parent.parent
RES = ROOT / 'src/DesktopIconGrid/Resources'
TABLES = [(RES / 'Strings.resx', 1), (RES / 'Strings.zh-Hant.resx', 2), (RES / 'Strings.zh-Hans.resx', 3)]
PLACEHOLDER = re.compile(r'\{(\d+)\}')


def existing(path):
    return {d.get('name'): d.findtext('value') or '' for d in ET.parse(path).getroot().findall('data')}


def main():
    entries = runpy.run_path(sys.argv[1])['ENTRIES']
    for key, en, hant, hans in entries:
        if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_]*', key):
            sys.exit(f'bad key {key}')
        for name, text in (('zh-Hant', hant), ('zh-Hans', hans)):
            if sorted(PLACEHOLDER.findall(text)) != sorted(PLACEHOLDER.findall(en)):
                sys.exit(f'placeholders differ for {key} ({name})')
    for path, index in TABLES:
        current = existing(path)
        text = path.read_text(encoding='utf-8')
        added = []
        for entry in entries:
            key, value = entry[0], entry[index]
            if key in current:
                if current[key] != value:
                    sys.exit(f'{path.name}: {key} exists with different text')
                continue
            added.append(f'  <data name="{key}" xml:space="preserve">\n    <value>{escape(value)}</value>\n  </data>\n')
        if added:
            cut = text.rindex('</root>')
            text = text[:cut] + ''.join(added) + text[cut:]
            path.write_text(text, encoding='utf-8')
        print(f'{path.name}: +{len(added)}')
    subprocess.run([sys.executable, '-I', str(pathlib.Path(__file__).with_name('gen_designer.py'))], check=True)


main()
