---
version: alpha
name: Quick Net Switcher
description: Switchboard. Light theme tokens are unsuffixed; dark theme tokens carry a -dark suffix. The accent is the user's choice; teal is the default and is the one recorded here.
colors:
  primary: "#006D77"
  on-primary: "#FFFFFF"
  surface: "#ECEEF1"
  card: "#FFFFFF"
  hover: "#F4F5F7"
  line: "#C4CAD1"
  control-outline: "#74808B"
  text: "#101418"
  text-secondary: "#535D68"
  status-connected: "#1B6E30"
  status-disconnected: "#8A5A00"
  error: "#A8261C"
  accent-green: "#1B6E30"
  accent-ink: "#101418"
  primary-dark: "#5CC8D0"
  on-primary-dark: "#23282D"
  surface-dark: "#16191C"
  card-dark: "#23282D"
  hover-dark: "#2A3036"
  line-dark: "#3D464F"
  control-outline-dark: "#8B96A1"
  text-dark: "#F2F4F6"
  text-secondary-dark: "#A3ADB7"
  status-connected-dark: "#6FD08A"
  status-disconnected-dark: "#E5B454"
  error-dark: "#FF8A80"
  accent-green-dark: "#6FD08A"
  accent-ink-dark: "#F2F4F6"
typography:
  name:
    fontFamily: Segoe UI Variable Text
    fontSize: 16px
    fontWeight: 600
  body:
    fontFamily: Segoe UI Variable Text
    fontSize: 13px
    fontWeight: 400
  caption:
    fontFamily: Segoe UI Variable Text
    fontSize: 12px
    fontWeight: 400
  data:
    fontFamily: Cascadia Mono
    fontSize: 12px
    fontWeight: 400
rounded:
  sm: 3px
  md: 6px
  full: 14px
spacing:
  xs: 4px
  sm: 7px
  md: 10px
  lg: 14px
components:
  row:
    backgroundColor: "{colors.card}"
    textColor: "{colors.text}"
    typography: "{typography.name}"
    rounded: "{rounded.md}"
  row-status:
    backgroundColor: "{colors.card}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.caption}"
  row-hover:
    backgroundColor: "{colors.hover}"
    textColor: "{colors.text-secondary}"
  row-dark:
    backgroundColor: "{colors.card-dark}"
    textColor: "{colors.text-dark}"
  row-status-dark:
    backgroundColor: "{colors.card-dark}"
    textColor: "{colors.text-secondary-dark}"
  row-hover-dark:
    backgroundColor: "{colors.hover-dark}"
    textColor: "{colors.text-secondary-dark}"
  switch-on:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.full}"
    width: 52px
    height: 28px
  switch-on-dark:
    backgroundColor: "{colors.primary-dark}"
    textColor: "{colors.on-primary-dark}"
  switch-on-green:
    backgroundColor: "{colors.accent-green}"
    textColor: "{colors.on-primary}"
  switch-on-green-dark:
    backgroundColor: "{colors.accent-green-dark}"
    textColor: "{colors.on-primary-dark}"
  switch-on-ink:
    backgroundColor: "{colors.accent-ink}"
    textColor: "{colors.on-primary}"
  switch-on-ink-dark:
    backgroundColor: "{colors.accent-ink-dark}"
    textColor: "{colors.on-primary-dark}"
  tab-selected:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.body}"
  tab:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
  tab-selected-dark:
    backgroundColor: "{colors.primary-dark}"
    textColor: "{colors.on-primary-dark}"
  tab-dark:
    backgroundColor: "{colors.surface-dark}"
    textColor: "{colors.text-secondary-dark}"
  link:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.primary}"
  link-dark:
    backgroundColor: "{colors.surface-dark}"
    textColor: "{colors.primary-dark}"
  button-outlined:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    padding: 7px
  button-outlined-dark:
    backgroundColor: "{colors.surface-dark}"
    textColor: "{colors.text-dark}"
  status-error:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.error}"
  status-error-dark:
    backgroundColor: "{colors.surface-dark}"
    textColor: "{colors.error-dark}"
---

# Quick Net Switcher

## Overview

A switchboard for the person who opens it from the tray to flip one network
adapter or firewall profile and close it again. The switch is the largest
control, state is readable without reading, and details wait until asked for.

## Colors

Ink and greys do the work; colour means something wherever it appears.

- **Accent:** fills a switch that is on, the selected tab, a ticked box and
  Apply; colours links, the focus ring, the drop bar and the tray icon. The
  user picks it in Settings: Teal (default), Match Windows, Green, None (ink).
- **Status:** `status-connected` and `status-disconnected` colour only the 8px
  dot beside the status word. Disabled is an empty ring, never red.
- **Error:** footer messages for a failed action, and an invalid metric.
- `control-outline` holds 3:1 on both surfaces; `line` is decorative.
- A Windows accent under 3:1 against `card` is replaced by `text`; one under
  4.5:1 is not used for link text (`ThemeService.ApplyAccent`).

Tokens live in `Themes/Light.xaml` and `Themes/Dark.xaml` under identical keys.

## Typography

Segoe UI Variable Text (Segoe UI on Windows 10) for everything read as words.
Cascadia Mono (Consolas fallback) for anything compared digit by digit: IP,
gateway, metric, MAC, speed, the route grid. Three sizes: 16px semibold for an
adapter or profile name, 13px for controls, 12px for status, details, filters
and the footer. Icons come from Segoe Fluent Icons (Segoe MDL2 Assets fallback).

## Layout

Window 620×580, minimum 500×450, margin 14px sides and 10px top and bottom.
Top to bottom: segmented tabs, the tab's filters (10px above and below), the
list, the footer. Rows are 54px minimum with 4px between them; row padding 7px
vertical, 14px right. An adapter row reads handle (26px), name over status,
address, switch. Its details open below a 1px `line`, wrapping. The footer
holds the status message at left; GitHub, Release notes, Refresh, Settings at
right. Route grid cells 10px × 6px, metric right-aligned.

## Elevation & Depth

None. An enabled row is `card` with a solid 1px `line`; a disabled row is the
bare `surface` with a dashed outline (4 on, 3 off). Menus and the dialog are
`card` with a `control-outline` edge. No shadows, blurs or gradients.

## Shapes

6px for rows, panes, buttons, inputs, menus and the tab control. 3px for
checkbox boxes. Switches are 52×28 and fully round, with an 18px thumb.

## Components

- **Switch:** off is a 1.5px `control-outline` ring with a `text-secondary`
  thumb; on is filled with the accent. 50% opacity while Windows applies it.
- **Row:** click or Enter/Space opens its details; hover is `hover`; keyboard
  focus is a 2px accent outline. Dragging is by the handle only: the row
  follows at 60% opacity and a 2px accent bar marks where it lands.
- **Tabs:** one control across the window; selected segment filled.
- **Buttons:** outlined by default; one filled button (Apply); underlined
  links; 28px icon buttons. Hover and pressed lay an 8% and 16% wash over the
  button; nothing fades.
- **Checkbox:** 16px, for filters and menu items.
- **Route grid:** horizontal lines only; selected row is `hover`.
- **Footer status:** `text-secondary`; `error` when the action failed.

## Do's and Don'ts

- Do take every colour from the theme dictionaries with `DynamicResource`,
  including in Windows Forms and GDI+ code (`ThemeColor`).
- Do add each new key to both `Light.xaml` and `Dark.xaml`.
- Do say state in words beside any colour or switch that shows it.
- Don't add a fourth font size, a second radius for containers, or a shadow.
- Don't use the accent for status, or a status colour for decoration.
- Don't repeat the window title or the tab label inside the window.
- Don't fill a second button; if two actions compete, one is outlined.
- Don't fade a control to show hover; fading lowers its label's contrast.
