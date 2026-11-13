"""The text of a release on GitHub (.github/workflows/github-release.yml): the update notes of that version, from the release's own updates.json
(Client/assets/changelog/updates.json as it was released), those of the versions since the release before it on GitHub in a fold, and which file
is for whom.

    python3 Tools/release/github-notes.py 2.0.21 out/updates.json on-github.txt > notes.md

on-github.txt: the tags of the releases on GitHub already, one a line (gh release list).
"""
import json
import os
import re
import sys
from datetime import date

# the repository's address (a release's text has no relative links): GitHub Actions says it
REPO = f"{os.environ.get('GITHUB_SERVER_URL', 'https://github.com')}/{os.environ.get('GITHUB_REPOSITORY', 'Exelter/atreia-app')}"


def key(v):
    """1.21.0 < 2.0.0-beta < 2.0.0 < 2.0.1: a version with a suffix comes before the same one without."""
    m = re.match(r'^v?(\d+)\.(\d+)\.(\d+)(?:-(.+))?$', v.strip())
    if not m:
        return None
    return (int(m[1]), int(m[2]), int(m[3]), 0 if m[4] else 1, m[4] or '')


def day(d):
    try:
        x = date.fromisoformat(d)
        return f'{x.day} {x.strftime("%B")} {x.year}'
    except (TypeError, ValueError):
        return ''


def main(version, notes_file, listed_file):
    with open(notes_file, encoding='utf-8') as f:
        versions = json.load(f).get('versions', [])
    with open(listed_file, encoding='utf-8') as f:
        listed = [k for k in (key(t) for t in f.read().split()) if k]
    me = key(version)
    # the newest release on GitHub before this one: the notes of every version after it go in the fold
    before = max((k for k in listed if k < me), default=None)
    this = next((v for v in versions if key(v.get('version', '')) == me), None)
    since = [v for v in versions if key(v.get('version', '')) and key(v['version']) < me and (before is None or key(v['version']) > before)]
    since.sort(key=lambda v: key(v['version']), reverse=True)

    out = []
    if this:
        out.append(f'## What\'s new in {version}')
        if day(this.get('date')):
            out.append(f'*{day(this["date"])}*')
        out.append('')
        for it in this.get('items', []):
            label = ' · '.join(x for x in (str(it.get('kind', '')).capitalize(), it.get('area', '')) if x)
            out.append(f'**{label}: {it.get("title", "").strip()}**' if label else f'**{it.get("title", "").strip()}**')
            if it.get('text'):
                out.append(it['text'].strip())
            out.append('')
    if since:
        out.append(f'<details><summary>The updates before it, since the last release here ({len(since)} versions)</summary>')
        out.append('')
        for v in since:
            out.append(f'**{v["version"]}**' + (f' ({day(v.get("date"))})' if day(v.get('date')) else ''))
            for it in v.get('items', []):
                label = ' · '.join(x for x in (str(it.get('kind', '')).capitalize(), it.get('area', '')) if x)
                out.append(f'- {label + ": " if label else ""}{it.get("title", "").strip()}')
            out.append('')
        out.append('</details>')
        out.append('')
    out += [
        '## Downloads',
        '',
        '| | |',
        '|---|---|',
        '| **Windows 10 and 11** | `Atreia-App-Setup.exe`: the installer (install [Npcap](https://npcap.com) first). After that the app updates itself. |',
        f'| **Linux (x64)** | `Atreia-App-Linux-x64.tar.gz`: unpack it and run `./install.sh` in the unpacked folder (see [Linux/README.md]({REPO}/blob/main/Linux/README.md)). |',
        '',
        'The other files are what the installed app reads to update itself (`*.nupkg`, `releases.stable.json`, `assets.stable.json`, `updates.json`, '
        '`linux.json`); `SHA256SUMS.txt` has the checksums of the Windows files. The same downloads are on [atreia.app](https://atreia.app).',
    ]
    sys.stdout.write('\n'.join(out) + '\n')


if __name__ == '__main__':
    main(*sys.argv[1:4])
