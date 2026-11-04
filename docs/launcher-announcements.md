# Launcher whispers

Edit `assets/launcher-announcements.txt` to add announcements. Each non-comment line is:

```
2026-11-03T21:11:41-05:00 | Slimjim | Your announcement or launcher patch note.
```

Use the actual posting time with an explicit UTC offset. The renderer sorts newest first, preserves each recorded timestamp and displays it in the user's local timezone before `Whisper From [Slimjim]: Message`. The supplied ghost avatar appears in the boxed announcement area above the game news, styled with WoW whisper purple. The whole news area remains scrollable; creator game patch notes remain intact below it.

Keep older lines for history. A message may contain additional `|` characters because only the first two delimiters are separators. Blank lines and lines beginning with `#` are ignored. Invalid timestamps are skipped. The file and avatar are embedded in the EXE: publish a rebuilt launcher to distribute new posts. There is no Discord account connection or message scraping.
