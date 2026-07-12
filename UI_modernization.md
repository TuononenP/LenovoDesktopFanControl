# UI Modernization

## Completed

- [x] Cohesive dark color system and typography
- [x] Modern buttons, checkboxes, toggles, selectors, sliders, and progress bars
- [x] Redesigned main dashboard and fan cards
- [x] Redesigned fan-curve editor
- [x] Dark native Windows title bar and rounded Windows 11 corners
- [x] Matching generated window, taskbar, and tray icon
- [x] English and Finnish localization for the new UI

## Completed Work Details

### High Priority

- [x] Perform runtime visual testing
  - [x] Test at 100% display scaling.
  - [x] Test WPF rendering at 125%, 150%, and 200% scaling (120, 144, and 192 DPI).
  - [x] Test the minimum window size and multiple fan counts (0, 1, and 3).
  - [x] Check English and Finnish for clipped or overflowing text.
  - [x] Verify the curve editor and all custom control templates interact correctly.
  - Runtime fan-count scenarios can be enabled safely with the
    `LENOVO_FAN_CONTROL_VISUAL_TEST_FANS` environment variable.

- [x] Improve responsive layout
  - [x] Allow the control panel to wrap or reorganize at narrower widths.
  - [x] Avoid fixed card widths producing excessive empty space.
  - [x] Ensure longer translations do not overlap buttons or options.

- [x] Improve accessibility
  - [x] Add `AutomationProperties.Name` and descriptions where labels are insufficient.
  - [x] Verify complete keyboard navigation and visible focus states.
  - [x] Add high-contrast theme support.
  - [x] Confirm text and control contrast meets WCAG AA guidance.

- [x] Add an embedded application icon
  - [x] Create a multi-resolution `.ico` file.
  - [x] Configure it as `ApplicationIcon` in the project file.
  - [x] Verify branding in Explorer, shortcuts, the taskbar, and UAC prompts.

### Medium Priority

- [x] Modernize the tray context menu
  - [x] Apply dark colors and consistent typography to the WinForms menu.
  - [x] Verify hover, disabled, and keyboard-selection states.

- [x] Add semantic status presentation
  - [x] Use distinct connected, warning, busy, unsupported, and error colors.
  - [x] Ensure the status indicator reflects actual state instead of remaining blue.
  - [x] Use the compact status bar as the notification surface for completed and failed actions.

- [x] Add confirmed Lenovo Vantage conflict shutdown
  - [x] Stop detected LenovoVantage and VantageService instances only after confirmation.
  - [x] Refresh the conflict warning and report complete or partial results.

- [x] Add contextual help
  - [x] Add tooltips for fan modes, full-speed mode, custom curves, and disabled controls.
  - [x] Explain why full-speed mode may be unavailable.
  - [x] Clearly communicate safety implications of manual fan control.

- [x] Organize theme resources
  - [x] Move colors, typography, and control templates out of `App.xaml`.
  - [x] Create dedicated `Colors.xaml`, `Controls.xaml`, and `Typography.xaml` dictionaries.
  - [x] Keep component-specific styles close to their controls.

### Optional Polish

- [x] Add restrained transitions for loading, status changes, and card updates.
- [x] Respect the Windows client-area-animation preference as reduced-motion behavior.
- [x] Evaluate Windows 11 Mica; retain the opaque backdrop for predictable readability and High Contrast fallback.
- [x] Consolidate repeated fan icon geometry into a reusable component.

## Definition of Done

- The UI remains usable without clipping at supported window sizes and DPI levels.
- English and Finnish layouts render correctly.
- Every action is accessible by keyboard and exposed meaningfully to screen readers.
- The executable, taskbar, title bar, and tray use consistent branding.
- Connected, warning, busy, unsupported, and error states are visually distinct.
- Debug and Release builds complete without warnings or errors.
