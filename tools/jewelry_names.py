#!/usr/bin/env python3
"""Print ring, trinket and amulet names from assets/items.txt for model lookup."""
import sys

section = None
started = False
names = set()
with open(sys.argv[1] if len(sys.argv) > 1 else "assets/items.txt", encoding="utf-8-sig") as source:
    for raw in source:
        line = raw.strip()
        if line == "===":
            started = True
            continue
        if not started or not line or line.startswith("#"):
            continue
        if line.startswith("@"):
            section = line[1:].strip()
            continue
        if section != "jewelry":
            continue
        fields = [field.strip() for field in line.split("|")]
        if len(fields) >= 2 and fields[1]:
            names.add(fields[1])

for name in sorted(names, key=str.casefold):
    print(name)
