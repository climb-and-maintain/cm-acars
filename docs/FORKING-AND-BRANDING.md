# Forking and branding

Forkability is an architectural requirement. A fork should be able to change visible identity without modifying ACARS core behavior.

## Central branding fields

Configure these in the project's branding source:

- ProductName
- ShortProductName
- Publisher
- OriginalAuthor
- ProductWebsite
- SourceRepository
- SupportURL
- DefaultAccent
- AppId

Also update icons, installer filename, default backend URL, bundled maintained profiles, and visible screenshots as appropriate.

## Attribution that remains

Apache-2.0 permits modified and commercial/non-commercial derivatives but requires preservation of relevant notices. Keep:

- LICENSE and applicable NOTICE content;
- the statement that Climb and Maintain, SPC is the original author;
- upstream authorship and BSD notice for adapted phpVMS profiles; and
- notices required by other bundled dependencies.

A fork may present its own product and publisher prominently while retaining historical/legal attribution in About, documentation, package metadata, and notices.

## Simulator policy in forks

The project does not bundle or download Microsoft SimConnect binaries and builds the complete native adapter without the SDK. Builds produced with this repository's release tooling retain that policy. An independently governed fork is solely responsible for any materially different distribution policy and must not present it as an official Climb and Maintain release.

Do not remove target-specific validation. MSFS 2020 and MSFS 2024 remain separate even under different branding.

## Backend and business rules

Core contains no Climb and Maintain-specific VA policy. Replace or add an IFlightOperationsBackend implementation rather than coupling a fork's rules to simulator acquisition or the generic flight engine.

## Release identity

Use a distinct application ID, package/installer name, update channel, source/support URLs, and signing identity to avoid collisions with official installations. Preserve semantic versioning and publish hashes and notices for the fork's artifacts.
