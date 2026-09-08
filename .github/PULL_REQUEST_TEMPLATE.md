## Summary

Describe the user-visible result and the architectural boundary changed.

## Verification

- [ ] Release build succeeds with zero warnings.
- [ ] Relevant unit, contract, integration, and UI tests pass.
- [ ] Documentation and CHANGELOG are updated for user-visible behavior.
- [ ] Keyboard, Narrator/UI Automation, High Contrast, and scaling impact was considered.
- [ ] Logs, diagnostics, fixtures, and screenshots contain no credentials or personal data.
- [ ] No Microsoft or aircraft-vendor binary, SDK file, or proprietary documentation is included.

## Simulator changes

Complete when applicable:

- [ ] The real adapter remains compiled in ordinary builds without an SDK.
- [ ] No managed SimConnect wrapper or SDK-path build conditional was added.
- [ ] MSFS 2020 and MSFS 2024 configuration/validation remain independent.
- [ ] Missing or invalid libraries preserve configuration, diagnostics, phpVMS setup, and replay.
- [ ] Automated native-boundary tests use a project-owned shim, not a Microsoft DLL.

## Aircraft-profile changes

- [ ] The profile is data-only, deterministic, read-only, and has universal fallback.
- [ ] Synthetic fixtures cover each added mapping.
- [ ] Upstream source, revision, author, and license metadata are preserved.

## Risk and recovery

Explain failure behavior, migrations, rollback, and how active flights/outbox data remain safe.
