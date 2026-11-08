# Security and privacy

[Deutsch](SECURITY.de.md) · Scope: 1.0.0, persistent home access.

## Local by default

The Windows manager hosts its own web interface, normally on loopback port 5087.
Loopback is this PC, not an encrypted channel or a defense against other software
running as your Windows user. LAN is off by default. There is no product cloud,
account service, analytics upload, payment endpoint or automatic update service
in the inspected runtime code. Build-time NuGet/npm/GitHub access is separate.

The running app reads LMU shared memory and makes GET requests to LMU's local
REST service at `127.0.0.1:6397` for catalog and navigation evidence.
Garage evidence is available in the separate diagnostic CLI, not production polling.
It does not send setup changes to LMU. Browser requests stay on the selected
manager host; QR generation and translations run locally. Windows/browser
components and LMU have their own network behavior outside this application's
control. Optional diagnostic CLI arguments can point at a different REST address;
this is not the production manager's default behavior.

The local owner can read bounded in-memory channel/resource diagnostics at
`/api/manager/health`. Remote devices cannot access this endpoint. It contains no
REST bodies/URLs, credentials, personal names or paths and writes no tick log.
This is manager telemetry, not a measurement of game frametimes or force feedback.

## What data is processed

The library stores car/track/layout identifiers, five electronics values, setup
names, notes, profiles, favorites, immutable revisions, capture provenance and
database timestamps. It also has conservative lap-reference/diagnostic facilities;
there is no automatic race-history dashboard. User-entered notes can contain
personal information. LMU memory contains more than the five displayed values:
the native decoder also reads session/scoring/driver information, including driver
name and Steam ID where supplied. Normal library capture does not persist a
driver profile; separately enabled diagnostic JSONL can record raw native and REST
data, names/identifiers and other session participants. Never share raw recordings
without redaction. This is not a promise of anonymity.

Local files, by default:

| Data | Location |
| --- | --- |
| SQLite library | `%LOCALAPPDATA%\LmuFixedSetupManager\library\setups.sqlite` |
| Export/migration/restore backups | `backups` beside the actual database |
| Manager settings | `%LOCALAPPDATA%\LmuFixedSetupManager\manager\settings.json` |
| Device credentials | `devices.protected` in the manager directory |
| UI preference | Browser localStorage `lmu.ui.language` for that origin |
| Browser authentication | HttpOnly, SameSite=Strict access cookie |

Custom paths override these defaults. SQLite and downloaded backups are **not
encrypted by the application**. Device metadata/credential hashes are protected
with Windows DPAPI for the current user; this does not encrypt the library or
network and does not protect against compromised software running as that user.
Network settings contain interface/network identifiers and local addresses.
Recent remote contact is used for status, not proof of continuing reachability.

## Optional home-network access

Select exactly one eligible physical IPv4 interface, affirm that this specific
network is trusted, accept the HTTP risk and acknowledge the firewall notice.
There is no automatic selection, even with one adapter. A Public Windows profile
can remain unchanged after deliberate home-network approval; it is not evidence
that an unknown network is safe. VPN/tunnel, ambiguous or unsupported adapters
are rejected conservatively. Binding is limited to loopback plus the selected
address, with source-subnet checks and current adapter/network validation.
Changed/uncertain network properties block access. Unconfirmed discovery suspends
an existing approval; only the exact approved network can resume automatically.
A different identity remains blocked; the saved approval and requested switch are
retained. Returning to the exact approved network may resume access. Deliberate off
remains off after restart and retains devices/approval. Confirmed approval removal
is separate and retains devices too. Invitations are discarded on network/host
changes, not ordinary language changes.

**HTTP has no transport encryption.** An attacker on the network may read or
modify library data, pairing tokens and session credentials. Authentication,
DPAPI, QR codes, CSRF and Origin checks do not solve this. Only use a trusted
home network and trusted devices; no Internet exposure, router port forwarding,
guest/hotel/public networks or certificate-warning bypass. HTTPS is not configured.

The app does not modify firewall rules, router settings or Windows network
profiles. Its consented firewall instructions describe a manual exception limited
by executable, interface, local address, TCP port and source subnet. Review existing
broad/duplicate Allow rules: adding a narrow rule does not narrow another rule.
Moving the EXE can invalidate path-based rules. Binding successfully does not
prove access from another device; test there.

## Authentication and revocation

The tray opens local owner access with a random runtime key. Its browser cookie
is limited to eight hours; after restart reopen through the tray. The QR and
copyable link contain the same ten-minute, one-use invitation secret. Treat both
as credentials: never screenshot, post or forward them. Invitation state lives
in memory; unused invitations do not survive restart. Redemption creates an
individual random device grant, with at most 30-day validity, stored as a hash
inside the DPAPI-protected file. QR/link display is transient, not a saved QR file.

Authorized devices can read **and edit the entire library**, including backup and
restore paths exposed by the API. They cannot administer manager settings. Revoke
individual devices in Settings; switching LAN off retains device grants. Existing
device grants can survive restart if the same saved network approval remains valid.
Owner access requires loopback and the owner secret. API writes require CSRF;
foreign Origin requests are rejected, session pairing is rate limited, and browser
content has a restrictive CSP. These checks reduce specific attacks; they are not
a claim that hostile local software or hostile LAN traffic is safe.

## Backup, restore and deletion

Create a consistent SQLite backup in the app and download an independent copy.
Protect backups as carefully as the live database. Restore verifies the file,
previews/migrates a private copy, checks for changes since preview and requires
an exact typed phrase. The preview expires after 15 minutes. Before replacing the
whole library it creates a safety backup; restore is not an individual setup import.

Trash is **soft deletion**: all revisions remain. There is no per-setup permanent
purge UI or secure-erasure feature. To remove all local product data, first revoke
devices, disable LAN and Windows startup, quit the manager, and then deliberately
remove the actual library (including any WAL/SHM sidecars), its backup directory,
manager directory and any custom database/settings paths. Remove separately
downloaded backups, diagnostics and extracted program folders if no longer needed.
Clear cookies/site data on each browser/device; remove application-specific firewall
exceptions you created. This manual operation is irreversible; retain a backup if
needed. The app cannot erase other devices' copies or guarantee forensic erasure.

## Reporting a concern

Use [GitHub Private Vulnerability Reporting](https://github.com/Suedehead84/LMU-Setup-Manager/security/advisories/new). Enabled; Suedehead84 confirmed regular monitoring. Do not use public issues for vulnerabilities or personal data. Supported scope: 1.0.x, no guaranteed response deadline. Independent notification delivery testing remains unperformed. See [security policy](../SECURITY.md).
