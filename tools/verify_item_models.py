#!/usr/bin/env python3
"""Keep item model rows only when Wowhead serves an image for the display id."""
import sys
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

EXTENSIONS = ("jpg", "png")

def available(display_id):
    base = "https://wow.zamimg.com/modelviewer/classic/webthumbs/item/{}/{}".format(display_id % 256, display_id)
    for ext in EXTENSIONS:
        request = Request(base + "." + ext, headers={"User-Agent": "Mozilla/5.0 AzerothSurvivorsLauncher", "Range": "bytes=0-31"})
        try:
            with urlopen(request, timeout=15) as response:
                content_type = response.headers.get("Content-Type", "").lower()
                head = response.read(32)
                if response.status in (200, 206) and content_type.startswith("image/") and head:
                    return True
        except (HTTPError, URLError, TimeoutError, OSError):
            pass
    return False

for raw in open(sys.argv[1], encoding="utf-8-sig"):
    line = raw.strip()
    if not line or line.startswith("#"):
        continue
    fields = [part.strip() for part in line.split("|")]
    if len(fields) < 3:
        continue
    try:
        display_id = int(fields[2])
    except ValueError:
        continue
    if available(display_id):
        print("|".join(fields[:3]))
    else:
        print("No current Wowhead Classic model image for {} (display {}).".format(fields[0], display_id), file=sys.stderr)
