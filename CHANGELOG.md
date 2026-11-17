# Changelog

## 2.9.0 (2026-11-16)

- Choose **Play → Compare with Friend → Match Loaded JSON** to back up the MO2 profile, enable available mods, disable extras and apply a friend's load order. The launcher compares again afterward and lists remaining differences.
- Co-op exports include MO2 version labels and launcher-recorded SHA-256 release checksums when available. Older exports still work for load-order comparisons.
- Matching can install or update supported MeeMaster add-ons from ModDB. When the export specifies a release, the launcher checks the download against its checksum or version before installing. Other missing mods and unavailable releases can be installed through MO2.
- **Verify → Purge unused mod files** previews disabled mod folders and their estimated size before deletion. Mods enabled in another profile are protected by default.
- Archive testing and extraction now report progress, including the last reported percentage during long waits.
