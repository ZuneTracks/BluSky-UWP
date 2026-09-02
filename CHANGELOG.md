# Changelog

## 1.0.243.0 - Chat prototype

### Added

- Direct-message inbox with message history, pagination, read-state updates, and text message sending.
- New-chat flow for a Bluesky handle, DID, or selected follower.
- Paged follower picker for starting one-to-one conversations.
- Persistent diagnostics at `LocalState\unisky-diagnostics.log`, including app startup, Chats navigation, message sending, and unhandled exception details.
- Login guidance explaining that Bluesky accounts using two-factor authentication require an App Password.

### Changed

- Upgraded FishyFlip to `4.3.0` across the application, services, background task, models, moderation, notifications, and tests.
- Added the chat service and view models to the app dependency-injection and project build configuration.
- Updated session serialization to use pooled buffers.
- Restored the mobile-safe minimum package version of Windows 10 build `15063`.
- Reworked Chats for phone-sized displays: selected conversations use the full width, the composer spans the bottom of the conversation, and a back action returns to the inbox. Wider screens retain the two-pane layout.
- Simplified conversation and follower list rendering for Windows 10 Mobile XAML compatibility.
- Made post-send UI updates run on the UI dispatcher and separated send failures from inbox-load errors.

### Fixed

- Prevented chat UI updates from accessing UI-bound state on the wrong thread.
- Prevented invalid, self, unsupported, or unavailable direct-message recipients from being bound as conversations.
- Fixed the missing 2FA/App Password guidance in custom themes and non-English resource configurations.
- Fixed Windows 10 Mobile Chats-page XAML parse crashes caused by item templates.

### Known limitations

- Chats currently support accepted one-to-one text conversations only.
- Conversation requests, groups, system messages, embeds, and join links are not supported.
- New-message notifications are not real-time and no chat-specific badge or toast notification is provided yet.
