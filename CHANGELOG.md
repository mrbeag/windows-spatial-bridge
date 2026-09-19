# Changelog

## 0.1.16 — 2026-09-19

Initial public release.

- Routes a game's conventional 7.1 mix into Windows Spatial Audio static
  objects for headphone rendering.
- Captures the game-facing VB-CABLE endpoint directly with event-driven WASAPI
  loopback, bypassing VB-CABLE's buffered Input-to-Output transfer path.
- Keeps one complete 10 ms packet as the smallest stable safety queue on the
  tested path.
- Detects the correct multichannel VB-CABLE endpoint even if it is renamed.
- Configures VB-CABLE's multichannel Line Out pin as 8-channel, 24-bit PCM at
  48 kHz; Windows exposes the corresponding 8-channel, 32-bit float shared mix.
- Uses the required 2-byte Windows audio-structure packing and raw-channel mask,
  matching the format written by Windows Settings.
- Provides physical-headphone selection, automatic bridge startup, live tray
  status, and a compact status window.
- Shows the release version in the status-window title and tray menu.
- Keeps the status window and tray indicator synchronized across Off, setup,
  ready, active, and error states.
- Recognizes the correct VB-CABLE Line Out endpoint even when renamed, disabled,
  or not yet configured, without mistaking the separate Speakers pin for it.
- Includes automated regression coverage for device selection, setup
  inspection, status presentation, and compact-window actions.
- Includes a validated x64 installer and Windows app icon.
- Confirmed working with Destiny 2, Helldivers 2, and Jump Space.
