# Changelog

## 2.9.6 (2026-11-20)

- Install Teepo's Speed Fix from GAMMA RC3's bundled add-on resources when its MO2 mod folder is missing. No separate download is needed when these files are present.
- Support this recovery during install finalization, co-op setup, resumed installs, and profile repair; preserve existing installed copies.
- Explain both searched locations and recovery steps when the bundled files are unavailable. Check before creating reinstall snapshots.

## 2.9.5 (2026-11-20)

- Create a missing ModOrganizer.ini before selecting the ROMEO profile. Local GAMMA archive installs can now finish without first opening Mod Organizer.
- Configure Anomaly launch entries using the selected game folder while preserving existing MO2 configurations.
- Resume affected installs from their saved checkpoint. Required-mod validation, including Teepo's Speed Fix from GAMMA, remains enabled.

## 2.9.4 (2026-11-18)

- Start new GAMMA verifications with fresh progress; retain checkpoints when continuing the same verification.
- Check Anomaly before importing a local archive, using the archive only for repairs. Keep installed GAMMA base files unless Mod Organizer is missing.
- Reuse unchanged archive retention checks within a run and centralize the ROMEO release identifier.
- Extract into staging in one pass, checking extraction success before copying. Reduce per-file archive and copy logging and batch live journal updates.
- Validate enabled mod folders and priorities after restoring player settings and managed add-ons; report success only after final checks.

## 2.9.3 (2026-11-17)

- Resolve fresh ModDB mirror links for xrRazom and retry once when a mirror expires.
- When an automated xrRazom download fails, offer a browser download and ZIP picker to continue the installation. Completed stages remain saved, and the selected ZIP must pass checksum validation.
- Clarify recovery for stopped installs: select Settings → xrRazom → Local, choose xrRazom-1.5-Slim.zip, then Resume Install.
- Add a direct Launcher Support link to the ROMEO Discord support thread.
- Group full-repair settings in an expandable Verify options panel, with clearer descriptions and links to xrRazom settings and co-op add-ons.

## 2.9.0 (2026-11-16)

- Choose **Play → Compare with Friend → Match Loaded JSON** to back up the MO2 profile, enable available mods, disable extras and apply a friend's load order. The launcher compares again afterward and lists remaining differences.
- Co-op exports include MO2 version labels and launcher-recorded SHA-256 release checksums when available. Older exports still work for load-order comparisons.
- Matching can install or update supported MeeMaster add-ons from ModDB. When the export specifies a release, the launcher checks the download against its checksum or version before installing. Other missing mods and unavailable releases can be installed through MO2.
- **Verify → Purge unused mod files** previews disabled mod folders and their estimated size before deletion. Mods enabled in another profile are protected by default.
- Archive testing and extraction now report progress, including the last reported percentage during long waits.
