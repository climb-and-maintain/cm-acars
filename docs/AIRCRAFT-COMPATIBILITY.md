# Aircraft compatibility

## Compatibility model

Support is layered:

1. **Universal SimVars** provide basic tracking for normally behaving aircraft.
2. **Matched profiles** replace only verified features.
3. **Specialist providers** are optional and reserved for cases that cannot be solved safely through the first two layers.

An unavailable enhanced switch must not prevent position, altitude, speed, time, distance, or landing tracking.

## Generic v0.1 target

- position;
- altitude MSL and AGL when exposed;
- indicated and ground speed;
- heading and vertical speed;
- on-ground state;
- flight time and distance;
- fuel and fuel flow when exposed;
- landing detection/rate from sampled data; and
- basic standard system states.

## Initial enhanced verification

- stock/default MSFS aircraft;
- FlyByWire A32NX;
- Fenix A319/A320/A321;
- PMDG 737;
- PMDG 777; and
- several iniBuilds/default MSFS 2024 aircraft where profiles or standard behavior are sufficient.

This list does not promise perfect detection of every switch. Published release notes identify what was actually verified.

## Simulator edition matters

Aircraft-profile target and native-library target are separate concepts, but both distinguish MSFS 2020 from MSFS 2024 where necessary. Do not transfer a compatibility claim between editions without evidence.

## Reporting a problem

Use the aircraft compatibility issue form and include simulator edition/version, aircraft/developer/version, profile selected, standard telemetry result, affected feature, and sanitized exported diagnostic. State whether connectivity itself passed with the separately validated library for that simulator.
