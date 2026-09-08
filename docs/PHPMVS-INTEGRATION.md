# phpVMS integration

phpVMS is a first-class backend behind IFlightOperationsBackend, not a plugin and not part of the simulator adapter.

## Authentication

The initial credential type is an API key sent through the backend's credential provider. The implementation may support bearer credentials later without distributing authentication logic across HTTP calls.

Credentials are protected with Windows DPAPI for the current user. They are never logged or included in support bundles. HTTPS is the default; HTTP requires an explicit developer setting and is restricted to localhost or another loopback address.

## Supported operations

The v0.1 target includes:

- status and version;
- current user;
- bids, flights, search, briefing, assigned aircraft, and fleet;
- PIREP prefile and update;
- ACARS position, event, and log batches;
- completed PIREP filing; and
- cancellation.

Request and response contracts follow the current phpVMS API behavior and are tested against recorded or mock fixtures, not a production virtual airline.

## PIREP lifecycle

Prefile includes the required airline, aircraft, flight number, departure, arrival, and a short source name. It also includes optional route, level, planned time/distance/fuel, simulator, fares, and related values when reliably known.

Before the prefile POST, the application persists a `Starting` session and a unique, 25-character request marker. That marker is sent in `source_name`. If the response is lost or the application stops before saving the returned PIREP ID, recovery queries the authenticated user's in-progress PIREPs and matches `source_name` exactly. A match attaches that PIREP ID to the original local session. Once a POST has been marked as dispatched, an empty or failed lookup leaves the start pending and never causes an automatic second POST; Resume retries the safe lookup, while Discard explains that phpVMS may require manual review.

Filing includes distance and flight time plus the reliable fuel, block, aircraft, route, landing-rate, timing, planned, and source values captured in the durable completion record. phpVMS fare counts are not inferred from scheduled capacity; they remain unset until a future workflow can collect an actual passenger or cargo count.

## Position and event delivery

Telemetry may be collected more frequently than it is reported. The default design enqueues a reportable position approximately every ten seconds, then sends bounded batches. Collection timestamps determine recency; array order must not be treated as authoritative.

Every position, event, and log has a durable local ID and attempt metadata. The sender handles:

- HTTP 429 and Retry-After;
- timeouts;
- 5xx responses;
- exponential backoff with jitter;
- duplicate or ambiguous retry outcomes; and
- application/network restarts.

The application never hammers a server or deletes an item before confirmed delivery.

## Offline behavior

An unreachable phpVMS server does not stop flight tracking. The dashboard reports that tracking is local and shows the queue depth. The SQLite outbox resumes automatically after connectivity returns.

## Contract-test cases

Tests cover valid and invalid credentials, flight and bid lists, prefile, position/event batches, interruption, duplicate retry, 429, 400, 401, 404, 500, filing, and cancellation. Where practical they compare request JSON exactly.

## Server support reports

Provide the base URL with sensitive path/query data removed, phpVMS version, response status, correlation identifier if available, and sanitized diagnostics. Never post an API key, Authorization header, cookie, or full personal flight history.
