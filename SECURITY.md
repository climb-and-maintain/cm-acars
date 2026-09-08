# Security policy

## Supported versions

Before the first public beta, only the current default branch receives security fixes. After releases begin, the latest published minor line will be supported unless its release notes state otherwise.

## Report a vulnerability privately

Use GitHub's **Security** tab and choose **Report a vulnerability** to open a private vulnerability report. Do not open a public issue for credential exposure, unsafe native-library loading, path traversal, support-bundle leaks, or another exploitable defect.

Include the affected version or commit, impact, reproduction steps, and any suggested mitigation. Remove phpVMS API keys, authorization headers, cookies, Microsoft or aircraft-vendor binaries, proprietary SDK content, and personal flight data before attaching evidence.

If GitHub private vulnerability reporting is unavailable, contact the repository owner through its private organizational contact channel and state that the message is a security report. Do not send secrets until a secure response channel is established.

## Response process

Maintainers will acknowledge a report, assess severity and affected versions, coordinate a fix and disclosure timeline, and credit the reporter if requested and safe. Exact response times are not guaranteed for this public project, but reports involving active credential or code-execution risk receive priority.

## Security boundaries

### User-supplied native libraries

A native DLL executes code in the application's process. Only choose a SimConnect library obtained from a Microsoft-provided source you are entitled to use. Automatic discovery must be bounded and non-executing; the app should load a candidate only after explicit selection and validation. Use absolute paths and safe Windows DLL-search behavior.

The application does not attest that an arbitrary DLL is trustworthy. Structural validation and a successful connection test establish compatibility, not provenance or safety.

### Profiles

Aircraft profiles are data only. Scripts, executable expressions, arbitrary process invocation, downloads, and simulator writes are forbidden. Profile parsing must be bounded and treat all community files as untrusted input.

### Credentials and diagnostics

phpVMS credentials are stored with Windows DPAPI for the current user. API keys, authorization headers, cookies, and secrets must not enter logs, telemetry exports, screenshots, crash reports, or support bundles. HTTP is allowed only through an explicit local/developer override with a warning.

Dependencies are monitored by Dependabot and CodeQL. Never solve a simulator integration problem by committing a proprietary DLL.
