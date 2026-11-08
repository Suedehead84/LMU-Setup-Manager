# Windows and browser qualification / Kompatibilitätsmatrix

1.0.0, stable private technical release. The owner confirms RC2 intended functionality in real operation. The owner confirms all seven preview.10 home tests passed.
This does not certify every Safari/iPadOS or assistive-device version. No unrecorded OS/device versions or measurement duration are inferred from that approval. A runtime
target is not a tested product matrix. Categories:
**Automatisiert geprüft**, **Manuell geprüft** (historical user confirmation),
**Technisch erwartet, aber unbestätigt**, **Nicht unterstützt**, **Nicht getestet**.

| Environment/case | Category | Evidence and boundary |
| --- | --- | --- |
| Existing Windows x64 test host, OS build 10.0.26200.0 | Automatisiert geprüft | Real process, DPAPI, HTTP and SQLite tests; existing development installation, not a clean machine |
| Self-contained win-x64 package | Automatisiert geprüft | Four published components, native SQLite, local web/language assets; no separate user SDK/runtime installation intended |
| Tray lifecycle, single instance, restart and port release | Automatisiert geprüft | 17 Manager process assertions; no proof of every shell/desktop configuration |
| LMU identity/matching and ESC/session behavior | Manuell geprüft | Earlier user tests, plus replay/synthetic regressions; not a new RC drive or every car/layout |
| LAN, QR, iPad after narrow Public-profile firewall rule | Manuell geprüft | User confirms prior real access; OS/browser versions and full device matrix not recorded |
| Eight manual language cases, different device languages and tray | Manuell geprüft | User confirms completion; no fabricated per-case timestamps or device versions |
| Backup download/restore on prior user installation | Manuell geprüft | Earlier user confirmation, plus isolated automated restore regressions |
| In-app Chromium test browser, desktop/narrow synthetic UI | Automatisiert geprüft | Actual browser UI inspection/screenshots; not a physical phone; preview.10 fixed narrow DE navigation; RC metadata still needs its own check |
| Clean supported Windows x64 without SDK/prior installation | Nicht getestet | Must extract complete ZIP and verify runtime/native dependencies, browser launch, tray, DPAPI and data permissions |
| Other supported Windows 11 x64 releases/editions | Technisch erwartet, aber unbestätigt | Within runtime support boundary, not a blanket product qualification |
| Windows 10 editions/releases still supported by .NET 10 | Nicht getestet | Check exact edition/lifecycle against upstream matrix; no generic Windows 10 claim |
| Current Edge/Chrome/Firefox and other Safari/iPadOS combinations | Nicht getestet | No per-version qualification; earlier unspecified iPad success is not a complete Safari matrix |
| Windows ARM emulation | Nicht getestet | No native ARM publish and no emulation claim |
| Native Linux/macOS manager, Windows 7/8 | Nicht unterstützt | No supported product package/qualification for these targets |
| Internet-facing host/port forwarding | Nicht unterstützt | Deliberately outside the trusted-home-network security model |
| Unknown/new LMU ABI | Nicht unterstützt | Must be validated first; no guard removal |

The manager uses .NET 10 Windows Forms and an interactive Windows desktop, targets
`win-x64`, and bundles its runtime. It still needs all accompanying web assets,
native files and notices; a lone EXE is not the distribution. The
[upstream .NET OS matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
defines runtime lifecycle limits; no inferred Windows 7 support from target metadata.

## Unsigned ZIP and SmartScreen

The manager is not Authenticode-signed. A SHA-256 hash checks correspondence with
a trusted published hash; by itself it does not establish a publisher. Microsoft's
[SmartScreen documentation](https://learn.microsoft.com/en-us/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/)
describes reputation/signature checks and possible warnings. A local build without
Internet download markings does not exercise real first-download behavior.
Do not disable Defender/SmartScreen, lower policy globally or bypass browser
certificate warnings. If a warning blocks use, pause, verify source/hash with the
owner and investigate; controlled download qualification and a signing decision
remain open. No assurance that a future signed package will never warn.

## Clean-machine acceptance still required

Use an owner-controlled clean supported Windows x64 installation, not a personal
library: download from the proposed trusted location, retain normal Windows
security policy, verify hashes, extract everything, run as ordinary user, test
default-browser launch/tray/second launch/quit/restart, writable data paths,
manual catalog/library, backup/restore and DE/EN. Then test a documented update
and move of the EXE, re-checking autostart and path-dependent firewall exceptions.
Record actual OS edition/build, browser and LMU version. Do not mark this case
passed until performed; no additional drive is assumed for completing this local RC preparation.

Deutsch: Automatisierte Prüfungen auf dem vorhandenen PC und historische
Benutzerbelege ersetzen keinen frischen Windows-/Browser-Test. Unsigned bleibt
unsigned; Sicherheitsmechanismen nicht pauschal abschalten. Datenpfade und
Updateverhalten stehen in den Benutzeranleitungen.
