---
name: windows-lob-xaml
description: "Design, build, and review data-dense line-of-business apps in WinUI 3 and the Windows App SDK. Use for tables, dashboards, forms, validation, task workflows, navigation, responsive layout, theming, High Contrast, accessibility, data binding, MVVM, virtualization, and WPF or UWP migration."
---

# Windows LOB XAML guidance

Design, implement, and review line-of-business (LOB) interfaces in WinUI 3 and
the Windows App SDK. Favor clear workflows, consistent behavior, accessible
interaction, and WinUI 3 APIs over WPF or UWP conventions.

## Workflow

1. Identify whether the request is to create, implement, explain, or review.
1. Identify the surface: data list, table, dashboard, form, task workflow,
   record details, settings, or app shell.
1. Inspect the project resources, view models, and existing patterns before
   introducing new controls or styles.
1. Make the smallest complete change that satisfies the workflow.
1. Build and test visual, keyboard, accessibility, loading, empty, error, and
   narrow-window states.

Do not invent resource keys, APIs, measurements, package support, or test
results. If a screenshot, requirement, or source file is unclear, identify the
missing information instead of guessing.

## WinUI 3 platform guardrails

- Use \`Microsoft.UI.Xaml\` APIs. Do not use \`System.Windows\` or
  \`Windows.UI.Xaml\`.
- Use \`{x:Bind}\` where possible and set the binding mode explicitly. In a
  \`DataTemplate\`, declare the bound type with \`x:DataType\`.
- Use \`{ThemeResource}\` for theme-reactive resources.
- Use \`VisualStateManager\`, adaptive triggers, or named view-model state
  instead of WPF style triggers.
- Use \`ContentDialog\` instead of \`MessageBox\`.
- Use \`DispatcherQueue\` instead of WPF dispatcher APIs.
- Use \`Visibility="Collapsed"\`; WinUI 3 has no \`Hidden\` visibility state.
- Verify that third-party controls and packages explicitly support WinUI 3 and
  the project's Windows App SDK version.
- Treat WPF or UWP APIs in WinUI 3 code as correctness issues, not style
  preferences.

## Clarity and consistency

- Make every interaction discoverable. Show a reorder affordance, sortable
  header, filter control, or other visible cue instead of relying on a hidden
  gesture.
- Secondary row commands can appear on pointer hover or keyboard focus, but
  reserve their layout space and provide a tooltip and automation name.
- Use one status vocabulary across dashboards, tables, tasks, and forms.
  Pair status color with text, a glyph, or shape.
- Keep the same action label, glyph, and placement throughout the app.
- Use a \`ProgressRing\` for blocking waits and an \`InfoBar\` for recoverable
  results or errors.
- Confirm irreversible actions. A reversible soft-delete can skip a modal when
  the item moves to a visible Deleted view with Restore or Undo.
- Make dashboard summaries, search results, and related-record chips navigate
  to the corresponding page or record.

## Data lists and tables

- Use \`ListView\` for read-mostly vertical records.
- Use \`GridView\` for a card or tile collection that reflows by width.
- Use \`ItemsRepeater\` when the layout or realization behavior must be custom.
- Use a maintained WinUI 3 table control for spreadsheet-style editing,
  sorting, and resizing. Do not recommend the archived UWP Community Toolkit
  \`DataGrid\` for a new WinUI 3 app.
- Use a \`Grid\` at the root of an item template when columns, trimming, or
  alignment matter. Linear subregions can use \`StackPanel\`.
- Preserve virtualization. Do not place a virtualizing list in a parent that
  gives it infinite height.
- Search, filters, sort, and grouping must operate on one collection view.
- Show the result count and a Clear all action when filters are active.
- Distinguish no data from no matching filtered results.
- Align text left and numeric or date values right unless the data requires a
  different, consistently applied alignment.
- Include loading, empty, error, and populated states.

## Dashboards and cards

- Build a prioritized workflow surface, not a wall of identical cards.
- Give the most important business state the strongest visual prominence.
- Keep attention lists short and provide a View all action.
- Use uniform grids for compact metrics and heterogeneous layouts for larger
  insight or work-list cards.
- A 3:2 grid can pair a primary insight with a secondary work list. Stack the
  pair when either side falls below its minimum readable width.
- Derive breakpoints from content width, localization, and text scaling rather
  than copying a device label or unexplained pixel value.
- If a user hides one widget, let the remaining widget expand instead of
  leaving an empty column.
- Use a shared card style based on system card brushes and theme-aware borders.
  Start with 20 pixels of padding for standard cards and 12 pixels for dense
  rows, then adjust for demonstrated content needs.
- Do not set a fixed card height that can clip scaled or localized text.
- Charts need an accessible name and a text summary or data alternative.

## Task workflows

- Group task creation into one card with the title, due date, primary Add
  action, helper text, and inline validation feedback.
- Use \`AutoSuggestBox\` when task text can reference related records. Render
  references as wrapping, keyboard-accessible links or chips.
- Use \`SelectorBar\` for a small set of mutually exclusive views such as Open,
  Completed, and Deleted.
- Offer workflow-oriented sorts such as manual order, due date, related-record
  type, or attachment state.
- Give each view a specific empty state and announce changes with a polite live
  region.
- Provide a dedicated reorder handle with a move cursor, tooltip, automation
  help text, and Up and Down key support.
- Prefer reversible deletion for lightweight task workflows. Confirm permanent
  or bulk deletion.

## Forms and validation

- Use \`Grid\` for aligned rows and columns. Use \`StackPanel\` for genuine
  one-dimensional flow such as page sections or label-description groups.
- Reflow multi-column forms into one column before fields or messages become
  cramped.
- Use the built-in input that matches the data: \`NumberBox\`, \`DatePicker\`,
  \`CalendarDatePicker\`, \`ComboBox\`, \`ToggleSwitch\`, or \`AutoSuggestBox\`.
- Use a control's \`Header\` instead of a separate label when the control
  supports it.
- Validate fields on focus loss or submission, not on every keystroke.
- Put field-specific errors next to the field. Put cross-field and business-rule
  errors in an \`InfoBar\`.
- On submission, validate the form, focus the first invalid field, and summarize
  the number of fields requiring attention.
- Track dirty state. Enable Save only when the form is dirty and valid.
- Warn before navigating away from unsaved changes.
- Keep optional fields optional and validate them only when the user supplies a
  value.

## App shell, settings, and record details

- Use \`NavigationView\` for an app with multiple related work areas. Do not add
  global navigation to inflate a single-purpose app.
- For a desktop app, consider an integrated title bar with Back, Forward, and
  cross-record search when those commands support the workflow.
- Give frequent shell actions keyboard accelerators and include the shortcut in
  the tooltip.
- Keep Settings in the standard \`NavigationView\` settings destination.
- Group settings in shared card containers. Put a label and explanation on the
  left and the control on the right; stack them when localization or width
  makes the row cramped.
- Keep an unavailable capability visible only when the explanation helps the
  user understand product state. Disable it and state why it is unavailable.
- Start a record page with identity, then summary metrics, semantic status or
  category chips, narrative summary, and structured fields.
- Reduce metric columns or stack them before labels and values clip.

## Theming and accessibility

- Define Light, Dark, and High Contrast resources in the same change.
- Use semantic system brushes instead of hardcoded colors.
- In High Contrast, use system color brushes and increase card, dialog, and
  flyout borders when needed for separation.
- Never communicate status or validation through color alone.
- Set \`AutomationProperties.Name\` on icon-only controls.
- Set semantic heading levels on page and section headings.
- Use polite live regions for empty-state and successful in-place updates.
  Reserve assertive announcements for errors that need immediate attention.
- Verify keyboard navigation, focus indicators, screen-reader names,
  selection, hover, pressed, disabled, and validation states.
- Test at 100, 150, 200, and 250 percent scaling with long localized strings.
- Test the initial, narrow, and portrait-like window sizes.
- Keep one clear scroll owner and avoid nested scrolling dead zones.

## Data binding and implementation

- Keep business state in the view model with named properties such as
  \`HasItems\`, \`IsLoading\`, \`CanSubmit\`, and \`HasActiveFilters\`.
- Prefer \`x:Bind\` functions over complex converter chains.
- Keep styles and colors in XAML resources.
- Prefer visual states for fixed responsive transitions. Presentation-only
  code-behind or a custom panel is acceptable when layout depends on measured
  width, widget visibility, or last-row distribution.
- Keep business rules out of layout event handlers.

## Review checklist

- Uses WinUI 3 APIs and supported dependencies.
- Uses the right collection control and preserves virtualization.
- Provides loading, empty, error, success, and filtered-empty states.
- Keeps status and command behavior consistent.
- Exposes every interaction to keyboard and assistive technology.
- Uses shared system resources for cards, text, state, and High Contrast.
- Reorganizes content at narrow widths instead of only shrinking it.
- Keeps forms reachable, validated at the correct time, and safe from data
  loss.
- Makes dashboard summaries and related records actionable.
- Builds successfully and verifies the affected workflow at runtime.
