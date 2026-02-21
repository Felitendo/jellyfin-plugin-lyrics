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

1. Make sure your Jellyfin server is updated to **version 10.11.0 or higher**
2. Add the plugin repository URL to Jellyfin:
   ```
   https://raw.githubusercontent.com/Felitendo/jellyfin-plugin-lyrics/master/manifest.json
   ```
3. Open the **Plugin Catalog** in your Jellyfin dashboard  
4. Look for **"Lyrics"** under the **Metadata** category and install it
5. Restart Jellyfin
6. Search for the Plugin "LrcLib" (is sometimes pre-installed) and uninstall it (if it's not installed then skip this step)
7. Restart Jellyfin again
8. Go to **Scheduled Tasks** and run **"Download and upgrade lyrics (new)"**
9. Go to **Libraries** and click on **Scan all Libraries**

---

## 🛠️ Troubleshooting

- **Plugin not appearing?**  
  → Double check if your Jellyfin version is **10.11.0 or higher**

- **Lyrics not showing?**  
  → Try to search for songs manually (right click on a song -> edit song text -> click on the search icon)
  → Try **refreshing metadata**

- **Missing lyrics for specific tracks?**  
  → Manually refresh metadata (see below)
  → Toggle the `"Use strict search."` option in plugin settings

- **Scheduled task takes too long?**  
  → Turn on `Skip repeated misses` (default on)
  → Turn on `Limit work per run` and reduce `Max songs to check each run`
  → Keep `Retry after days` on `1,3,7,30` unless you want faster/slower retries

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
