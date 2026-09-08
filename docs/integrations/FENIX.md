# Fenix A319/A320/A321

Fenix support is profile-driven and read-only.

## Base behavior

Universal SimVars remain the fallback for position, motion, engines, fuel, and standard systems. The bundled adaptation uses direct read-only SimConnect LVars for beacon, logo/navigation, landing, strobe, taxi, wing, and emergency lights; seat belts; battery and external power; APU; packs; cabin exits; and anti-ice. It also labels the six Fenix flap detents.

The upstream OR-of-AND identity rules are retained as declarative match groups for the A319, A320, A321, and the pinned CFM configuration path. An unrelated product containing only the word “Fenix” is not claimed by this profile.

Compound rules use a bounded any-of/all-of condition format with Boolean or explicit universal-fallback actions. They cannot execute code or write simulator variables. A missing custom value and the Fenix strobe `AUTO` position retain the standard SimVar value instead of manufacturing an aircraft-specific result. Cabin exit state is exposed as a combined read-only “Cabin exits” diagnostic.

Prefer a standard SimVar whenever live evidence shows it is more reliable than a Fenix-specific value.

## Sources and attribution

Adapted phpVMS acars-config profiles retain their BSD notice, original author metadata, source path, and revision. Public Fenix documentation may identify variable names, but this project does not redistribute proprietary Fenix files.

Do not automatically parse Cockpit_Behavior.xml during normal operation. An aircraft owner may inspect their local installation manually when developing a profile, but must not upload that file.

## Diagnostics

If an enhanced value is unavailable:

1. confirm the correct simulator edition and its separately validated native library;
2. verify aircraft identity and matched profile;
3. compare universal fallback and raw LVar results in Profile Inspector; and
4. export a sanitized aircraft diagnostic.

Generic flight tracking remains available when an enhanced system state cannot be resolved.
