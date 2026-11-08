# Build from source — 1.0.0

Windows x64, SDK 10.0.403 from global.json, Node 24, Windows PowerShell 5.1 and Git.
The Windows app bundles .NET 10.0.14 and native SQLite; end users need no SDK.
Use your own clean clone or extracted source snapshot. NuGet metadata access is
needed for restore/audit. No LMU installation, SDK headers or user database is
required for automated tests. Never disable audit or freshness/security guards.

Run powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Clean -Publish
then powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-release.ps1.
Full output is artifacts/releases/1.0.0 with win-x64, ZIP, TEST_RESULTS.json and
SHA256SUMS.txt. Existing version folders cannot be overwritten. Use a fresh source
working directory for another same-version reproduction; do not remove old releases.

Source and runtime licenses/notices are included. Tests cover .NET/JS, isolated real
SQLite/HTTP/DPAPI/processes, fresh ZIP/EXE extraction, translations and privacy.
Intentional failures may emit errors; final assertion and exit status determine
success. No live drive or clean Windows qualification is implied. Unsigned software
can trigger SmartScreen; do not bypass OS protection.

A new directory below artifacts/opensource-review may be passed as -ReviewDirectory
for same-version prepublication packages. It never overwrites existing releases
and changes no product behavior. Legal/publication approval remains separate.
