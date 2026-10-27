using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// The plugin id is used by Jellyfin to locate the plugin data folder and the configuration file.
[assembly: Guid("d8b0f3a2-6c4e-4f0a-9e1b-2c3d4e5f6a7b")]

// The unit tests exercise the internal helpers (NfoBuilder, HtmlText, ThreeCatClient helpers...).
[assembly: InternalsVisibleTo("Jellyfin.Plugin.ThreeCat.Tests")]
