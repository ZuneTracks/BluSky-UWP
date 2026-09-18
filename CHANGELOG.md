# Changelog

## 1.2.1 - 2026-09-18

### Fixed

- Removed the Microsoft Store-restricted `confirmAppClose` capability from Store packages.
- Replaced the old extended-splash logo with the BluSky UWP splash artwork.

### Changed

- Increased the application package version to `1.2.1.0` so it updates the existing `1.2.0.0` BluSky UWP installation.

## 1.1.1 - 2026-09-05

### Added

- Lists browsing: view the signed-in account's lists, open list details, browse list members, and open supported list deep links.
- Saved Feeds: view saved and suggested feed generators, add supported Bluesky feed links, remove saved feeds, and open feed deep links.

### Fixed

- Custom Bluesky handles now resolve their Personal Data Server before sign-in, allowing accounts hosted on custom domains to authenticate.
- Handle discovery now accepts server responses that omit the `https://` scheme.
- Sign-in identifier validation uses a Windows 10 Mobile-compatible check.
- Saved-feed loading handles absent or empty preferences and falls back to the Following timeline.
- Package display names use the literal `BluSky UWP` value instead of exposing `ms-resource` text on unsupported or fallback locales.

### Changed

- Updated the application package version to `1.1.1.0`.
- Retained Windows 10 Mobile compatibility with a minimum supported build of `15063`.
- Adopted the independent `ZuneTracks.BluSkyUWP` package identity and `bluskyuwp:` deep-link scheme. This is a separate app from prior UniSky packages and does not migrate their local data.

### Notes

- Lists support is read-only; creating, editing, deleting lists, and managing list members are not included.
- Feed discovery uses Bluesky suggestions and direct feed links. Feed creation, search, and custom pin ordering are not included.

## 1.0.243

- Previous published release baseline.
