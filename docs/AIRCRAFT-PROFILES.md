# Aircraft profiles

Aircraft profiles are declarative, read-only drivers that map simulator variables into normalized ACARS features.

## Precedence

Profiles load in this order:

1. bundled universal defaults;
2. bundled upstream phpVMS profiles;
3. Climb and Maintain maintained profiles;
4. local user profiles.

The highest valid priority wins for a defined feature. A matched profile overrides only what it defines; universal values remain for everything else.

## Shape

The format follows the phpVMS acars-config concept and contains:

- meta: stable ID, name, author, source, revision, license, and priority;
- match and matchGroups: simulator, title/configuration-path scope, plus flat AND or bounded OR-of-AND identity rules;
- mappings: named, read-only SimVar or LVar interpretation rules;
- compoundMappings: bounded any-of/all-of read-only conditions with Boolean or explicit fallback actions;
- flapLabels: a bounded index-to-label table;
- disabled: explicitly unsupported features; and
- features: declarative capability flags used by diagnostics and compatibility reporting.

Supported simulator identifiers include msfs, msfs20, and msfs24. Use a specific target when behavior differs. Do not assume a variable or profile verified in one simulator behaves identically in the other.

## Safety rules

Profiles must never contain:

- executable code or scripts;
- PowerShell or process commands;
- downloads or instruction URLs;
- RPN or another executable expression language;
- simulator writes; or
- embedded copyrighted aircraft or SDK content.

Parsing and evaluation are bounded, deterministic, and side-effect-free.

## Contribution requirements

Each profile needs a unique ID, valid simulator, valid priority, recognized variable kinds/operators, deterministic matches, synthetic test fixtures, and a universal fallback. Adapted profiles preserve upstream author, license, source URL/path, and exact revision.

For an unsupported aircraft:

1. Reproduce with universal telemetry.
2. Open Profile Inspector.
3. Export a sanitized aircraft diagnostic.
4. Build the smallest rules that correct verified features.
5. Add synthetic fixtures for each rule.
6. Submit through the aircraft compatibility issue and pull-request process.

Never upload Cockpit_Behavior.xml, proprietary SDK documentation, vendor binaries, or other aircraft installation files.

## Inspector

Diagnostics shows detected title, configuration path, simulator, matched profile/source/priority, every resolved feature, raw variable, interpreted value, fallback, and unavailable variables.
