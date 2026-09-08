# Accessibility

Accessibility is a v0.1 release criterion and part of feature design, not a final polish pass.

## Supported interaction

The complete application must remain usable with:

- keyboard only;
- Windows Narrator;
- Windows High Contrast;
- Windows display scaling at 100%, 150%, and 200%;
- increased text size; and
- mouse or touch where applicable.

## Implementation requirements

- Prefer native WPF controls and platform behavior.
- Give every interactive control an accessible name.
- Associate labels with inputs and expose useful UI Automation IDs.
- Use HelpText only when it adds information.
- Expose connection, validation, phase, queue, and selection states textually.
- Preserve logical tab order, visible focus, Enter/Space activation, and Escape behavior.
- Avoid keyboard traps and avoid status conveyed by color alone.
- Give necessary custom controls an appropriate AutomationPeer.
- Target at least 4.5:1 contrast for ordinary text.
- Keep live announcements important and concise.

## Simulator setup

The runtime library workflow must be independently operable by keyboard and Narrator. Target simulator, discovered candidates, full selected path, validation stage, error, troubleshooting action, and connection-test result need programmatic labels and selection/state announcements.

An absent library must be described as an actionable simulator state, not a blocking modal dialog.

## Automated checks

FlaUI smoke tests should verify critical names, navigation, focus, selection, and validation errors. Automation supplements rather than replaces manual assistive-technology testing.

## Manual release checklist

For the installed release candidate:

- [ ] Complete first run using keyboard only.
- [ ] Navigate every primary page and dialog in a logical order.
- [ ] Configure each simulator target and select a library with Narrator.
- [ ] Hear validation errors and live connection status without moving focus unpredictably.
- [ ] Select/start/recover/file a test flight.
- [ ] Verify System, Light, Dark, and High Contrast presentation.
- [ ] Verify 100%, 150%, and 200% display scaling with no clipping.
- [ ] Check increased text size and narrow window layouts.
- [ ] Run Accessibility Insights for Windows on critical workflows.

Record defects as release blockers unless a documented, narrowly scoped exception is accepted by the steward.
