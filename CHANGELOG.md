# Changelog

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

### Notes

- Lists support is read-only; creating, editing, deleting lists, and managing list members are not included.
- Feed discovery uses Bluesky suggestions and direct feed links. Feed creation, search, and custom pin ordering are not included.

## 1.0.243

- Previous published release baseline.
