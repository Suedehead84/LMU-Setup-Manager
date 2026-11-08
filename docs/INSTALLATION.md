# Installation and update — 1.0.0

[Deutsch](INSTALLATION.de.md). **Stable technical release, private delivery.** Official public download still awaits owner approval. MIT rights apply as stated in LICENSE. Use the complete
release ZIP supplied privately, not a single executable from it.

## Requirements

Windows x64 supported by the bundled .NET 10 runtime, a current browser, and LMU
on this PC for live features. No SDK/runtime install is needed for the ZIP. Manual
library use works without LMU. Current native guards cover ABI 14000/14200; an
unknown ABI remains unavailable. The full OS/browser matrix is not certified.

## First start

1. Extract the complete ZIP into a new local folder. Keep its runtime files together.
2. Start **LMU Setup Manager.exe**. It runs in the Windows tray without a console.
3. Open the web interface from the tray. Closing a browser tab does not quit it.
4. Quit through the tray before starting another version. The default installation
   is single-instance per Windows user/settings directory.
5. Confirm sidebar version 1.0.0. There is no new version banner, activation, paywall or limit.

## Existing installations

Export a consistent SQLite backup from the old version first. Quit it through the
tray, extract new version into a different folder and retain user data outside the program
folder. Default data locations are `%LOCALAPPDATA%\LmuFixedSetupManager\library`
and `%LOCALAPPDATA%\LmuFixedSetupManager\manager`. Do not delete or reset either.
The database remains schema 4; no new version schema migration or data reset is introduced.
Do not promise downgrade after arbitrary future migrations.

For the final evaluation use copies as described in [manual acceptance](ACCEPTANCE.md).
In that private copy, turn Windows autostart off before launch so the evaluation
cannot repoint the production startup entry. The agent's automated tests use
isolated files and synthetic interfaces; they never start against your live data.

## Home network and devices

Home access defaults off. First setup has one visible informed safety confirmation;
then one persistent on/off switch. Approved network identity and requested on state
survive restart. Uncertain/foreign networks pause access without forgetting them.
Only the exact approved network can resume automatically; a private IP/subnet alone
is insufficient. Off remains off across restart. Removing approval is separate.
Both off and approval removal retain devices; revoke a device separately.

Invitations are single-use and expire after ten minutes. Paired device grants and
their cookies last at most 30 days, not indefinitely. Restart does not renew expiry.
Use the same host and browser storage; refreshing after a manager restart obtains
the new CSRF token. No new QR pairing solely for an application update.

**HTTP is unencrypted.** Use only your trusted home network/devices. No Internet
exposure or router forwarding. Windows may remain Public. The app changes neither
profile nor firewall. An EXE-path-scoped manual rule may need review for this new
folder. Only an actual second-device connection proves reachability.

## Return to the previous installation

Quit new version, then start the previous version with the original database/settings. If
new version was evaluated on copies, leave the original data and startup entry untouched.
Do not restore an entire library just to change executable version. Keep backups
and the evaluation copies private; they may contain personal notes and grants.

No automatic updates, licensing service or telemetry upload is introduced. The
longer real LMU freeze/FFB test remains outstanding. See [security](SECURITY.md).
