# Technical notes

This document contains the implementation details intentionally left out of the
main setup guide.

## Audio path

Windows Spatial Bridge is a normal Windows audio application. It does not
modify game files, inject code, hook game processes, read game memory, or
interact with anti-cheat software.

```text
Game 7.1 mix
    → VB-CABLE multichannel playback endpoint
    → WASAPI loopback capture
    → Windows Spatial Audio static objects
    → Windows Sonic / Dolby Atmos / DTS Headphone:X
    → physical stereo headphones
```

The game-facing VB-CABLE endpoint exists because a normal Windows application
cannot create a persistent system audio endpoint. That requires an audio
driver. VB-CABLE supplies the signed driver; the bridge handles the spatial
rendering.

The bridge captures the game-facing playback endpoint directly. It does not use
VB-CABLE's recording endpoint, normally named **CABLE Output**, and therefore
bypasses VB-CABLE's buffered Input-to-Output transfer path.

## Channel mapping

The bridge captures the standard 7.1 channel order:

```text
FL  FR  FC  LFE  BL  BR  SL  SR
```

Each channel is submitted to the matching Windows Spatial Audio static object.
The selected Windows spatial renderer then produces the binaural stereo signal
for the headphones. This is not a stereo upmixer; the game must already support
a 5.1 or 7.1 speaker mix.

## Format and latency

The VB-CABLE Line Out endpoint is configured as 8-channel, 24-bit PCM at
48 kHz—the format shown in Windows Settings. Windows then exposes an
8-channel, 32-bit float shared mix to the bridge. Changing the endpoint format
requires a one-time elevated setup action.

The bridge keeps one complete 480-frame audio packet as a safety queue. At
48 kHz this adds approximately **10 ms of latency**. A smaller queue caused lost
frames because Windows supplies and requests audio in complete 480-frame
blocks. The game, spatial renderer, audio driver, and headphones may add their
own latency outside the bridge.

## Device detection

VB-CABLE endpoints are identified by stable endpoint, driver identity, and form
factor—not by their displayed names. The bridge uses VB-CABLE's multichannel
Line Out pin and ignores its separate Speakers pin. An installed Line Out pin
is still recognized when disabled or configured for fewer channels, allowing
the elevated setup action to enable and repair it. Users may rename endpoints
safely.

The app validates:

- whether the signed VB-CABLE driver is present;
- whether the correct multichannel playback endpoint is available;
- whether Windows exposes its shared mix as 8-channel, 48 kHz, 32-bit float;
- whether the selected physical output supports Windows Spatial Audio; and
- whether the game-facing endpoint is the default playback device.

## Status detection

Green means game audio is reaching the bridge and being submitted to Windows
Spatial Audio. Amber means the route is ready but no game audio is currently
flowing. Red reports a configuration problem. Off means the route is valid but
the game-facing endpoint is not the default playback device.

Windows does not expose a dependable public API for naming the selected spatial
provider. The app validates the spatial endpoint rather than guessing whether
the active renderer is Sonic, Atmos, or DTS.

## Voice chat

Voice chat does not need to use the 7.1 route. Select the physical headphones
or DAC as the output inside Discord or the game's voice-chat settings. Chat then
travels directly to that normal stereo endpoint while game audio uses the
bridge. The Windows default communications device can also remain on the
physical headset.

## Games with native spatial audio

Games that correctly use the Windows Spatial Audio API should target the
physical headphones directly and bypass this bridge. Native-spatial games can
submit dynamic objects; passing them through the bridge would reduce the output
to a fixed 7.1 spatial bed.

## Build from source

The project requires the .NET 10 SDK. WiX Toolset v4 is also required to build
the x64 MSI.

```powershell
dotnet build SpatialBridge.csproj -c Release
dotnet build Tray\SpatialBridge.Tray.csproj -c Release
```

The tray and bridge executables must be installed side by side. MSI authoring
is in [`../Installer/Package.wxs`](../Installer/Package.wxs).

## Tests

The Windows test suite covers endpoint selection, installed-versus-missing
VB-CABLE detection, audio-setup evaluation, all status-light states, tray/UI
presentation consistency, and the hidden-window action-button regression.

```powershell
dotnet test tests\SpatialBridge.Tests\SpatialBridge.Tests.csproj -c Release
```

The same suite runs on Windows for every push and pull request through GitHub
Actions.

## Current limitations

- VB-CABLE must be installed separately.
- The MSI and executables are not code-signed, so Windows may display an
  unknown-publisher warning.
- The bridge accepts a fixed 7.1 channel bed rather than native dynamic spatial
  objects.
