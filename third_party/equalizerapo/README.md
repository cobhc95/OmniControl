# Equalizer APO installer (optional, not part of OmniControl's source)

Equalizer APO is made by Jonas Thedering and contributors (https://sourceforge.net/projects/equalizerapo/). It is free software
under the GNU GPL version 2 or later. OmniControl's "Fast" sound mode runs the master effects in an Equalizer APO that is already
installed: OmniControl writes its master chain to `OmniControl.txt` in Equalizer APO's config folder and adds one marked
`Include: OmniControl.txt` line to `config.txt`, which it takes out again when master effects stop or OmniControl exits.

When no Equalizer APO is installed, the card's **Install** button runs the official installer if it was placed here, otherwise
it opens the download page above.

## How the owner places the installer

1. Download the official 64-bit installer (`EqualizerAPO-x64-<version>.exe`, older releases `EqualizerAPO64-<version>.exe`) from
   https://sourceforge.net/projects/equalizerapo/files/ only.
2. Put it **unchanged** into this folder (`third_party\equalizerapo\`). Do not rename, re-sign or edit it.
3. Build OmniControl: the file is copied to `<app folder>\third_party\equalizerapo\`.

Do not commit the installer (it is git-ignored). Passing it on with OmniControl is allowed by the GPL, provided its licence
notice travels with it (NOTICE.txt credits it) and its exact source is offered (it is on the SourceForge page above).

What its installer does (from its `Setup.nsi`): installs into Program Files, lets the Users group write its `config` folder (so
OmniControl needs no administrator rights to add its line), sets `DisableProtectedAudioDG = 1` (needed to load an APO that
Microsoft did not sign; a few copy-protected players may then not play), and opens its Device Selector, where you tick the
playback devices to use. Remove it from Windows' Installed apps.
