# BluSky UWP Privacy Policy

**Last updated:** September 18, 2026

BluSky UWP is an independent Bluesky client maintained by ZuneTracks. This policy explains how the app handles information when you use it.

## Information the app handles

### Bluesky account and session data

To sign you in, BluSky UWP sends the identifier and app password or password you enter to the Personal Data Server (PDS) selected for your account. Custom Bluesky handles may be resolved to discover the account's PDS before sign-in.

The app stores the session information returned by the PDS, including your account identifier, access token, refresh token, and related session details, in Windows app-local storage so that you can remain signed in. The app does not intentionally store the password you enter.

### Content and account information

The app requests and displays Bluesky content and account information needed for features you use, such as profiles, posts, feeds, lists, messages, notifications, and moderation labels. This information is sent between the app and the relevant AT Protocol services over the network.

### App settings and device-local data

The app stores settings, including theme choices, notification preferences, language-related options, and a randomly generated installation identifier, in Windows app-local storage. It may also store local moderation-cache files, temporary media files while processing an upload, and diagnostic logs.

Diagnostic logs record technical events and errors. They remain on your device unless you choose to retrieve or share them with support.

### Push notifications

Push notifications are optional. If you enable them while signed in, BluSky UWP registers a Windows push channel and sends the following information to its current notification-registration endpoint:

- Your Bluesky DID
- The app installation identifier
- The Windows push-channel URI
- Your notification preferences
- Your language preferences

The current endpoint is `https://wamwoowam.co.uk/unisky/push/register`. Do not enable push notifications if you do not want this information sent to that service.

## How information is used

BluSky UWP uses this information to:

- Authenticate and maintain your Bluesky session.
- Display and interact with Bluesky content and account features you request.
- Remember your app settings.
- Deliver optional push notifications.
- Diagnose app failures when you choose to provide diagnostic information to support.

The app does not include advertising or analytics SDKs and does not sell personal information.

## Sharing and third-party services

Information is shared only as necessary to provide the app's features:

- **AT Protocol and Bluesky services:** Your selected PDS and other AT Protocol services receive the requests required to use Bluesky.
- **Microsoft:** Windows provides the operating-system push-notification service when notifications are enabled.
- **Notification-registration service:** The endpoint identified above receives the registration data described in the Push notifications section.

Those services have their own privacy practices. BluSky UWP does not control information handled by them after it is received.

## Data retention and your choices

Session information, settings, caches, and diagnostic logs are stored locally on your device until they are removed by signing out where available, clearing the app's data, or uninstalling the app. Temporary media files are used while processing uploads.

You can disable push notifications in the app's Settings. You can also remove the app and its local data through Windows to remove data stored by the app on your device. Removing local app data does not remove information held by Bluesky, your PDS, Microsoft, or the notification-registration service; contact the applicable service for requests concerning data it holds.

## Security

BluSky UWP uses the network services selected by your account and relies on their HTTPS connections for network communication. No method of transmission or storage is completely secure, and you should protect access to your device and account credentials.

## Children

BluSky UWP is not directed to children. Do not use the app where you are not permitted to use Bluesky or the applicable AT Protocol service.

## Changes to this policy

We may update this policy when the app's data practices change. The current version will be published in the BluSky UWP GitHub repository with an updated date.

## Contact

For privacy questions or requests, contact ZuneTracks at [app.support@ndtech.dev](mailto:app.support@ndtech.dev).
