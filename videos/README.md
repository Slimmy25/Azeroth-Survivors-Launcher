# Videos

The video lists used by the launcher's **Community** home panel and **MORE VIDEOS** catalog. Each folder has a
`videos.txt`, one video per line:
`address | title | channel | channel's address` (everything after the address is optional: a missing title and
channel are asked from YouTube when the video is first shown). Lines starting with `#` are notes.

- **big-streamers/**: videos used by the **Big Streamers** filter. They are embedded in the launcher at build time.
- **content-creators/**: videos used by the **Content Creators** filter. They are embedded in the launcher at build time.

On the Community home panel, **Skip Video** returns the current video to the back of the active filter's queue and
shows the next queued video. It can come around again after the other queued choices. The small **What's Next** preview
prefers videos the player has not watched; clicking it promotes that preview to the featured choice and returns the
previous one to the queue. Neither action starts playback. Playback begins only when the featured poster is clicked.

The Guide's **Gameplay → Build Guides** list is maintained separately in `assets/guide.txt`. The launcher groups
those pages under class headings when it can identify a class from the page title, description or text; pages that
do not identify one appear under **Uncategorized**. Build Guide videos also remain part of the Community video pool.
