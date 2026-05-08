# 🎶 Jellyfin Lyrics Plugin

A plugin for **Jellyfin** that automatically downloads and displays lyrics for songs in your music library using [lrclib.net](https://lrclib.net).

Looking for **v10.10.7 support**? -> https://github.com/Felitendo/jellyfin-plugin-lyrics-v10.10.7

---

## ✨ Features

- 🔄 Automatically downloads lyrics for your entire library  
- 🎼 Seamlessly integrates with Jellyfin’s music player  
- 🌐 Fetches lyrics directly from [lrclib.net](https://lrclib.net)  
- 🕒 Real-time lyrics display during playback  
- ⚡ Smarter scheduled task that avoids retrying the same failed songs every day  

---

## 🚀 Installation

1. Make sure your Jellyfin server is updated to **version 10.11.6 or higher**
2. Add the plugin repository URL to Jellyfin:
   ```
   https://raw.githubusercontent.com/Felitendo/jellyfin-plugin-lyrics/master/manifest.json
   ```
3. Open the **Plugin Catalog** in your Jellyfin dashboard  
4. Look for **"Lyrics"** under the **Metadata** category and install it
5. Restart Jellyfin
6. Go to **Scheduled Tasks** and run **"Download and upgrade lyrics"**
7. Go to **Libraries** and click on **Scan all Libraries**

---

> [!WARNING]
> This plugin will **automatically uninstall** the official **"LrcLib Lyrics"** plugin (`jellyfin-plugin-lrclib`) if it is installed. Both plugins provide a lyrics provider for the same songs, and running them side-by-side causes conflicts. This plugin is a fork of LrcLib Lyrics that fixes critical issues and adds more features, so using this one is strongly recommended. You can always reinstall the LrcLib Lyrics plugin later if you want to switch back.

---

## 🛠️ Troubleshooting

- **Plugin not appearing?**  
  → Double check if your Jellyfin version is **10.11.6 or higher**

- **Lyrics not showing?**  
  → Try to search for songs manually (right click on a song -> edit song text -> click on the search icon)
  → Try **refreshing metadata**

- **Missing lyrics for specific tracks?**  
  → Manually refresh metadata (see below)
  → Toggle the `"Use strict search."` option in plugin settings
  → If a song with very long trailing silence or a remastered version is being skipped, increase `Duration tolerance (seconds)`

- **Wrong lyrics on instrumental / interlude tracks?**  
  → The plugin filters matches by artist and by duration. If you still see wrong matches, **lower** `Duration tolerance (seconds)` (e.g. `5`) so only very close-duration matches are accepted.
  → If legitimate songs are being skipped instead, **raise** the value (e.g. `30`).

- **Scheduled task takes too long?**  
  → Turn on `Skip repeated misses` (default on)
  → Turn on `Limit work per run` and reduce `Max songs to check each run`
  → Keep `Retry after days` on `1,3,7,30` unless you want faster/slower retries

### How match filtering works

- **Filter matches by song length** — default on  
  When on, the plugin compares your local song's length to the length of the lyrics it finds online and skips lyrics whose length is too different. This stops short tracks like intros and interludes from getting lyrics that belong to a completely different song with a similar title.  
  Turn this off if you want the plugin to accept any match regardless of length (not recommended — you'll get more wrong matches).

- **Duration tolerance (seconds)** — default `15`  
  Only used when the length filter is on. How close the song length has to be to a lyrics match for the match to count. If they differ by more than this many seconds, the lyrics are skipped.
  - **Lower** (e.g. `5`) — stricter. Better at catching wrong matches, but might skip correct lyrics if your file has long silence at the end or is a different version (remaster, vinyl rip).
  - **Higher** (e.g. `30`) — more forgiving. Accepts more correct matches, but lets more wrong ones through.
  - The artist always has to match too — this setting only controls the length check.

### How the speed settings work

- **Skip repeated misses**  
  When a song has no lyrics, the plugin does **not** retry it every day.  
  With the default `1,3,7,30` schedule it tries:
  - after 1 day
  - then after 3 days
  - then after 7 days
  - then every 30 days  
  This removes most repeated API calls for songs that likely have no lyrics online.

- **Limit work per run**  
  Caps how many songs are checked in one scheduled run.  
  Example: with `Max songs = 1000`, the task stops after ~1000 songs and continues next day, instead of running for many hours.

- **Good starting values**
  - Small library: `Max songs = 2000` (default)
  - Large library / slow server: `Max songs = 500-1000`

---

## 🔄 Manual Refresh

If lyrics aren't appearing for specific albums:

1. Navigate to the album  
2. Right-click the album  
3. Select **"Refresh metadata"**

---

## 🤝 Contributing

Contributions are welcome!  
Feel free to open a **Pull Request**, or suggest new features / report bugs via an **Issue**.

---

## 📬 Support

👉 [Create an Issue](https://github.com/Felitendo/jellyfin-plugin-lyrics/issues)
