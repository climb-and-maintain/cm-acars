# PMDG 737 and 777

PMDG support in v0.1 does not make the proprietary PMDG SDK a core dependency.

## Base support

Use standard SimVars wherever possible. The attributed, pinned phpVMS profile adaptations read PMDG 737 and 777 light-switch LVars for beacon, landing, logo, navigation, strobe, taxi, and wing lights. Their compound landing/nav rules retain universal values when a required custom variable is missing. Generic tracking continues when a PMDG-specific light or system value is unavailable.

These read-only LVar mappings do not require a bundled PMDG SDK. The aircraft's installed documentation may describe an SDK/data-broadcast option for future enhanced telemetry. Follow the documentation shipped with the aircraft; Climb and Maintain ACARS does not redistribute it.

## Enhanced detection

The IPmdgEnhancedTelemetryProvider boundary may support a future licensed implementation. It does not block v0.1 and must not contaminate Core or the normal runtime-library workflow.

Direct use or redistribution of PMDG SDK material requires a separate licensing review.

## Troubleshooting

1. Confirm generic telemetry through the appropriate separately validated MSFS 2020 or MSFS 2024 library.
2. Check aircraft identity and the selected PMDG profile.
3. Check whether data broadcast is enabled according to the documentation in the aircraft installation.
4. Inspect unavailable values and export a sanitized diagnostic.

Do not upload PMDG binaries, configuration bundles, or SDK documentation to an issue.
