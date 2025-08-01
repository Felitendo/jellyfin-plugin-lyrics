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

3. Open the **Plugin Catalog** in your Jellyfin dashboard  
4. Look for **"Lyrics"** under the **Notifications** category and install it  
5. Go to **Scheduled Tasks** and run **"Download missing lyrics"**  
6. **Scan all libraries** to complete integration  

---

## 🛠️ Troubleshooting

- **Plugin not appearing?**  
  → Ensure your Jellyfin version is **10.9.11 or higher**

- **Lyrics not showing?**  
  → Double-check all **installation steps**  
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
Feel free to open a **Pull Request**, or start a discussion via an **Issue** if proposing a major change.

---

## 📬 Support

👉 [Create an Issue](https://github.com/Felitendo/jellyfin-lyrics-plugin/issues)
