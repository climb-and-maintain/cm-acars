# Privacy

Climb and Maintain ACARS does not include application analytics in v0.1.

## Data processed locally

The application stores configuration, simulator-library paths and validation metadata, aircraft profiles, logs, active-flight recovery data, flight summaries, and queued phpVMS reports under the current user's local application-data directory.

Selecting a native library records its path and compatibility metadata. The application does not upload or redistribute the library.

## Data sent to phpVMS

When a user configures a phpVMS server and tracks a flight, the application sends the operational data required by that server, which may include pilot/account requests, flight details, positions, altitude, speed, heading, phase, fuel, system states, events, logs, simulator time, and collection time.

The configured phpVMS operator controls its server and privacy practices. Review that virtual airline's policy before connecting.

## Credentials

API credentials are protected for the current Windows user through DPAPI. They are not stored in plaintext settings and must not enter logs, diagnostics, crash reports, screenshots, or support bundles.

## Diagnostics and support

Support bundles are sanitized automatically, but users should review them before sharing. Aircraft identity, application/simulator versions, profile selection, error messages, and native-library metadata may remain because they are needed to diagnose compatibility.

Never attach Microsoft or aircraft-vendor DLLs, SDK documents, API keys, authorization headers, cookies, or proprietary aircraft files.

## Retention and deletion

Logs roll over within configured size/history limits. Delivered outbox items and completed session records follow the application's retention policy. Uninstall offers to remove local application data; keeping it preserves settings and recovery records.

Data already submitted to phpVMS must be managed through that server's operator.
