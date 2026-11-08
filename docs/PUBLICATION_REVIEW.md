> Publication update, 2026-11-08: the owner explicitly approved proceeding with this local-use tool, confirmed monitoring of GitHub security reports, and decided to omit provider identification as a private hobby project. No independent legal exemption or formal SDK permission is claimed. The repository and private reporting are enabled. Historical pending-action statements below describe the earlier review, not a new authorization requirement. The remaining SDK-purpose interpretation and independent notification-delivery test are disclosed limitations. See [current release notes](../RELEASE_NOTES.md).

# Public snapshot rights and dependency review

Reviewed 2026-11-07. Preparation only: not legal clearance or permission to publish. Own code and own translations: MIT, copyright 2026 Suedehead84, owner-confirmed. The private history, productive data and previous releases are excluded.

## Excluded captured inputs

Redaction protects some privacy interests; it does not establish redistribution rights. These ten files are absent from the public snapshot. None is a runtime or build prerequisite.

| Exact internal relative path | Origin | Category | Rights question and disposition |
| --- | --- | --- | --- |
| `tests/Lmu.B24.Tests/Fixtures/monitor.real-redacted.json` | Passive native LMU 14200 session observation, 2026-11-05; reduced technical fields | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.B24.Tests/Fixtures/transitions.real-redacted.json` | Passive LMU session/ESC/menu transitions, 2026-11-05; reduced snapshots | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/gt3-cut-invalid.replay.json.gz` | Reduced replay of laps-fixed-cut-invalid.jsonl | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/gt3-fixed-clean.replay.json.gz` | Reduced replay of artifacts/win-x64/diagnostics/laps-fixed-clean.jsonl | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/quali-valid-invalid-clean.replay.json.gz` | Reduced replay of laps-quali-valid-invalid_sauber.jsonl | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/quali-valid-invalid.replay.json.gz` | Reduced replay of laps-quali-valid-invalid.jsonl | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/summary-curva.real-redacted.json` | LMU 14200, local GET /rest/garage/summary, WRT 69 / Monza Curva Grande, 2026-11-04 | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/summary.real-redacted.json` | LMU 14200, local GET /rest/garage/summary, WRT 32 / Monza, 2026-11-04 | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/tracks.real.json` | LMU 14200, local GET /rest/sessions/getTracksAll, 2026-11-04; 39 reduced entries | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |
| `tests/Lmu.Diagnostics.Tests/Fixtures/vehicles.real-redacted.json` | LMU 14200, local GET /rest/sessions/getAllVehicles, 2026-11-04; 584 reduced entries | Optional test input | No explicit redistribution grant established for captured game/catalog content; excluded. Original retained. |

Four private replay test classes (RecordedGt3Tests.cs, RecordedCutInvalidTests.cs, RecordedQualifyingTests.cs, ScoringAuditTests.cs, under tests/Lmu.Diagnostics.Tests) are excluded with their captured inputs. Independent fictional catalogs replace the real catalogs and monitor fixtures. SyntheticReplayTests.cs exercises offline gzip loading, types/zero values, timing/scoring boundaries, transient time suppression and missing identity. Existing synthetic lap, setup, identity, SQLite, HTTP and browser-code tests remain. Public test counts intentionally differ from the private recorded-data suite; no equivalence or new live-test evidence is claimed.

## SDK and interface findings

| Item / exact relevant path | Provenance and terms | Actual use / disposition |
| --- | --- | --- |
| SharedMemoryInterface.hpp | Installed game: Support/SharedMemoryInterface/SharedMemoryInterface.hpp; Studio397 BV / Motorsport Games, 2025. Purpose-limited plugin/extension development; header redistribution/modification expressly prohibited. | Local reference for measured ABI only; absent from source/package. No SDK binary linked; no header needed by .NET build. |
| InternalsPlugin.hpp | Same installed SDK directory, same purpose limitation and header redistribution prohibition. | Transitive local measurement reference for native telemetry/scoring; not compiled into application or redistributed. |
| PluginObjects.hpp | Same installed SDK directory; proprietary/confidential Studio397/Motorsport Games notice; no separate redistribution permission established. | Transitive include during private C++ ABI verification only; excluded. |
| docs/abi/14200-layout.tsv; 14200-headers.sha256.json | Own compiler-measured sizes/offsets/scalar types and SHA-256 identifiers; no header bodies. | Build-independent verification facts used by tests. Purpose question for standalone companion remains for scoped clarification; MIT does not license SDK materials. |
| src/Lmu.Diagnostics/Layout.cs; SharedMemorySource.cs; SessionMemory.cs | Own C# reader; constants/reader design adapted from lmu-pitwall, pinned a171b80a7c8c1e02a9d029e32fd717c834d26e71, MIT. Full Swizzjack notice retained in docs/lmu-pitwall-LICENSE.txt. | Required live shared-memory integration. Standard OS memory mapping/event primitives, not a redistributed SDK runtime. |
| src/Lmu.Diagnostics/RestProbe.cs; Identity.cs; src/Lmu.LiveCapture | Local HTTP interface on 127.0.0.1:6397; endpoint/property research included snipem/go-lmu-api af4292e899b285821b8bf9a3e4eaca492e3c5495. No source copied from that unlicensed checkout. | Required catalog/navigation evidence; diagnostic CLI additionally reads garage summary. No game credentials, Steam impersonation or hosted ranking/results service. Interface access is not itself a redistribution grant for captured responses. |
| LMU Engineer manual / Studio397 telemetry guide | Documentation read only. | No manual pages, screenshots, logos or code shipped. |

The [Studio397 statement of 2026-11-04](https://lemansultimate.com/community-update-november-2026/) addresses token/login-based ranking/results aggregation and backend load. It is not evidence of a blanket ban on all local shared-memory companions. Conversely, it does not expressly authorize this standalone tool. Minimal remaining action: obtain a scoped clarification on this read-only, local, standalone MIT companion under the installed SDK purpose terms, or qualified review of the concrete use. No SDK headers should be published to seek that review.

## Runtime, build and optional dependencies

| Component / source or package | Classification | License / evidence | Included notices |
| --- | --- | --- | --- |
| .NET runtime 10.0.14 | Required self-contained runtime | MIT plus upstream transitive notices; pinned NuGet package | NET-runtime-MIT.txt and NET-runtime-THIRD-PARTY-NOTICES.txt |
| ASP.NET Core runtime 10.0.14 | Required web runtime | MIT plus upstream transitive notices; pinned NuGet package | ASP.NET-Core-MIT.txt and ASP.NET-Core-THIRD-PARTY-NOTICES.txt |
| Windows Desktop runtime 10.0.14 (Windows Forms profile) | Required tray runtime | MIT; pinned Microsoft.WindowsDesktop.App runtime. Package has LICENSE but no aggregate THIRD-PARTY-NOTICES file. | WindowsDesktop-runtime-MIT.txt, verified WinForms v10.0.14 THIRD-PARTY-NOTICES. WPF v10.0.14 notices additionally retained conservatively; WPF is not an application feature. |
| Microsoft.Data.Sqlite / Core 10.0.14 | Required database runtime | MIT in pinned NuGet metadata | Microsoft.Data.Sqlite-MIT.txt |
| SQLitePCLRaw.core, bundle_e_sqlite3, provider.e_sqlite3, lib.e_sqlite3 2.1.13 | Required database managed/native runtime | Apache-2.0 in all four package declarations; SQLite engine upstream public-domain dedication | SQLitePCLRaw-Apache-2.0.txt plus engine reference in THIRD_PARTY_NOTICES.md. No extra NOTICE file found in these packages. |
| src/Lmu.Web/wwwroot/vendor/qrcode-2.0.4.js | Required local QR runtime | qrcode-generator 2.0.4, MIT; pinned npm integrity in QR_DEPENDENCIES.json | qrcode-generator-MIT.txt |
| tests/vendor/jsQR-1.4.0.js | Optional test-only decoder | Apache-2.0, pinned npm integrity | jsQR-Apache-2.0.txt; not in Windows app |
| lmu-pitwall adapted reader | Required application source | MIT, pinned commit | Full original copyright and MIT in lmu-pitwall-LICENSE.txt, source and package |
| .NET SDK 10.0.403 / reference, host and targeting packs; Microsoft.NET.ILLink.Tasks 10.0.14 | Necessary external build dependencies | Microsoft .NET distribution terms; ILLink MIT metadata + notices | SDK not redistributed; ILLink-build-THIRD-PARTY-NOTICES.txt retained for build provenance |
| Node.js, PowerShell, GitHub Actions checkout/setup tools | External build/CI tooling | Not vendored or placed in Windows ZIP; their upstream terms apply to tooling installations | No tool binary redistributed |
| Fictional JSON examples / original SVG and manager icon / own translations | Own project build/test assets | Owner-confirmed own work under MIT | Root LICENSE; no manufacturer/game logo |

All full texts are under docs/licenses (Windows package: licenses). Existing .NET and ASP.NET aggregate notices cover their transitive components without relabeling them MIT. Missing WinForms notice (including Ookii attribution/BSD conditions), WPF notice and build-only ILLink notices were added from versioned official sources. This is an evidence-based notice review of the locked dependency set, not a warranty of legal completeness.

## Unofficial branding

English/German README identify the project as independent and unofficial and deny affiliation/endorsement by Studio397, Motorsport Games and game rights holders. The original geometric artwork is retained; no official logo or game screenshot is included. Product/game names identify compatibility; the MIT grant does not grant trademark rights. No specific conflicting asset remains identified in this snapshot.

## Provider information: Germany, private hobby

Owner confirmed Germany and purely private hobby use, free of charge. That alone does not establish the exclusively personal/family exception. [Section 18(1) MStV](https://www.gesetze-bayern.de/Content/Document/MStV-18) requires name/address for offers outside that exception. A public community software offer needs that concrete applicability decision. [Section 5 DDG](https://www.gesetze-im-internet.de/ddg/BJNR0950B0024.html) separately concerns business-like services normally supplied for remuneration, with additional contact requirements where applicable; no commercial status is inferred just from hosting code. No journalistic/editorial activity is evidenced here. No registry/VAT/professional data should be invented.

Smallest action before launch: owner establishes applicability, obtaining qualified advice if uncertain, and supplies actual legally sufficient provider identification/contact where required. The copyright alias alone is not demonstrated sufficient provider identification. No personal address is requested in public issues, inserted in this snapshot or inferred from Git metadata. A lawfully usable service address requires an actual arrangement.

## Confidential security reporting

Owner selected GitHub Private Vulnerability Reporting. Not activated: the target public repository does not exist. Follow the owner checklist in SECURITY.md / SECURITY.de.md; enable, monitor and test a private report after repository creation, before announcing downloads. No mailbox or response-time commitment is invented.

## Concrete remaining publication blockers

| Blocker | Concrete reason | Smallest required measure |
| --- | --- | --- |
| Scope of SDK/interface development purpose | Local headers permit plugins/extensions, prohibit header redistribution; they do not expressly classify this standalone companion. Headers already excluded. | Scoped Studio397 clarification or qualified assessment of this precise read-only local use; document result. |
| Provider identification decision | Germany/private hobby confirmed, but public community distribution is not established as exclusively personal/family under MStV section 18. Required actual data not supplied. | Decide applicability and provide legally sufficient real provider information if required. |
| Confidential channel activation/verification | Owner chose PVR; no public repository exists, so it cannot yet receive reports. | Owner enables PVR, subscribes to notifications, confirms private test-report delivery in eventual public repository before launch. |

Excluded real recordings are no longer a blocker for this clean snapshot. They remain excluded unless separately cleared. No repository creation, upload, tag or release is authorized by this review.
