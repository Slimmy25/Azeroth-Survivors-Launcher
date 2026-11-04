# Launcher whispers

Edit `assets/launcher-announcements.txt` on the GitHub `launcher-announcements` branch to add announcements. Keep all earlier posts in that file. Launchers with this feature load the history at startup and show the newest posts first. Each non-comment line is:

```
2026-11-03T21:11:41-05:00 | Slimjim | Your announcement or launcher patch note.
```

Use the actual posting time with an explicit UTC offset. The renderer sorts newest first, preserves each recorded timestamp and displays it in the user's local timezone before `Whisper From [Slimjim]: Message`. The supplied ghost avatar appears in the boxed announcement area above the game news, styled with WoW whisper purple. The box sits in the upper right above News. Scroll over the box to read longer posts or earlier announcements. Game patch notes scroll separately.

Keep older lines for history. A message may contain additional `|` characters because only the first two delimiters are separators. Blank lines and lines beginning with `#` are ignored. Invalid timestamps are skipped. The EXE includes a snapshot of the history for offline use. The feedback box keeps the draft in the launcher and offers a Copy message button for posting it in Discord.
