# VB-CABLE (optional, not part of OmniControl's source)

VB-CABLE Virtual Audio Device is made by VB-Audio Software (www.vb-cable.com). **VB-CABLE is a donationware, all participations
are welcome.** It is proprietary: it is never committed to this repository and is not covered by OmniControl's GPL licence.

OmniControl's "Full control" sound mode uses it. Its **Install** button runs VB-Audio's own installer when the official
package was placed here, otherwise it opens https://vb-audio.com/Cable/.

VB-Audio's licensing page (https://vb-audio.com/Services/licensing.htm, read on 13 Nov 2026) allows this:
"We allow the distribution of the VB-CABLE package AS IS together with your application (freeware or commercial software)",
provided users can identify VB-CABLE as a VB-Audio application and can donate (mention www.vb-cable.com and that it is
donationware). It does **not** allow this for VB-CABLE A+B or C+D, and professional (company) use needs paid licences.

## How the owner places the package

1. Download the standard VB-CABLE package (`VBCABLE_Driver_Pack*.zip`) from https://vb-audio.com/Cable/ only.
2. Unzip it **unchanged** into this folder (`third_party\vbcable\`), so that `VBCABLE_Setup_x64.exe` sits next to this README
   together with every other file of the package (driver files, `.inf`, `.cat`, readme). Do not rename, re-sign or edit anything.
3. Build OmniControl: the files are copied to `<app folder>\third_party\vbcable\`.

Do not commit the package (it is git-ignored). Removing VB-CABLE: run the same `VBCABLE_Setup_x64.exe` and press "Remove Driver"
(OmniControl's Remove button opens it), then restart Windows.
