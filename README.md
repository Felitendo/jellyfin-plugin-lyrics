# Jellyfin Lyrics Plugin

A plugin for Jellyfin that automatically downloads and applies the lyrics for the songs in your music library from [lrclib.net](https://lrclib.net).

## 🎵 Features

- Automatic lyrics download for your entire music library
- Integrates seamlessly with Jellyfin's music player
- Downloads Lyrics directly from lrclib.net
- Real-time lyrics display during playback

## 🚀 Installation

1. Ensure your Jellyfin server is updated to version 10.9.11 or above
2. Add the plugin repository to Jellyfin:

   ```text
   https://raw.githubusercontent.com/Felitendo/jellyfin-plugin-lyrics/master/manifest.json
3. Navigate to the Plugin Catalog in your Jellyfin dashboard
4. Find "Lyrics" under the "Notifications" category and install it
5. Go to Scheduled Tasks and run "Download missing lyrics"
6. Scan all libraries to complete the integration

## 📝 Manual Refresh

If lyrics aren't appearing for specific albums:
1. Navigate to the album
2. Right-click
3. Select "Refresh metadata"

## 🔍 Troubleshooting

- **Plugin not appearing?**  
  → Ensure your Jellyfin version is **10.9.11 or higher**.

- **Lyrics not showing up?**  
  → Confirm all **installation steps are complete**, and try **refreshing metadata**.

- **Missing lyrics for specific songs?**  
  → Try **manually refreshing metadata** for those items and/or **toggle** the  
  `"Use strict search"` option in the plugin settings.

## 💡 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

## 📞 Support

- Create an [Issue](https://github.com/Felitendo/jellyfin-lyrics-plugin/issues)
- Write an [E-Mail](mailto:support@felo.gg)
- Join my [Discord Server](https://felo.gg/felocord)
