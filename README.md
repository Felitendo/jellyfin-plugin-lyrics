# 🎶 Jellyfin Lyrics Plugin

A plugin for **Jellyfin** that automatically downloads and displays lyrics for songs in your music library using [lrclib.net](https://lrclib.net).

---

## ✨ Features

- 🔄 Automatically downloads lyrics for your entire library  
- 🎼 Seamlessly integrates with Jellyfin’s music player  
- 🌐 Fetches lyrics directly from [lrclib.net](https://lrclib.net)  
- 🕒 Real-time lyrics display during playback  

---

## 🚀 Installation

1. Make sure your Jellyfin server is updated to **version 10.9.11 or higher**
2. Add the plugin repository URL to Jellyfin:
   
       https://raw.githubusercontent.com/Felitendo/jellyfin-plugin-lyrics/master/manifest.json
4. Open the **Plugin Catalog** in your Jellyfin dashboard  
5. Look for **"Lyrics"** under the **Metadata** category and install it
6. Restart Jellyfin
7. Search for the Plugin "LrcLib" (is sometimes pre-installed) and uninstall it (if it's not installed then skip this step)
8. Restart Jellyfin again
9. Go to **Scheduled Tasks** and run **"Download missing lyrics (new)"**
10. Go to **Libraries** and click on **Scan all Libraries**

---

## 🛠️ Troubleshooting

- **Plugin not appearing?**  
  → Double check if your Jellyfin version is **10.9.11 or higher**

- **Lyrics not showing?**  
  → Try to search for songs manually (right click on a song -> edit song text -> click on the search icon)
  → Try **refreshing metadata**

- **Missing lyrics for specific tracks?**  
  → Manually refresh metadata (see below)
  → Toggle the `"Use strict search."` option in plugin settings

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
