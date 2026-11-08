# Contributing / Mitwirken

Thank you for helping improve LMU Setup Manager. Report reproducible bugs, suggest
features, improve translations or send a focused pull request in the
[public repository](https://github.com/Suedehead84/LMU-Setup-Manager). Report vulnerabilities through [SECURITY](SECURITY.md).

Use Windows x64, SDK from global.json and Node 24. No LMU installation is needed
for automated tests. Run scripts/build.ps1 -Clean; check docs and translations.
Use isolated databases; never attach personal setups, raw logs, devices.protected,
access tokens, QR invitations, SDK headers or game assets. Describe tests clearly.

By intentionally submitting a contribution for inclusion, you offer it under the
project's MIT license, provided you own or can license the relevant rights. You
retain copyright; no exclusive transfer or separate CLA is required. Disclose
third-party sources and retain their original license/notices. Do not submit
material you cannot license under the stated terms. See [LICENSE](LICENSE) and
[third-party notices](THIRD_PARTY_NOTICES.md).

Changes to native identity, ABI, freshness, lap validity or database behavior need
appropriate evidence/regressions. Never invent valid laps or bypass unknown/stale
state. Keep changes focused and avoid new 1.0 features. Existing releases are
immutable; don't replace tags or packages. See [build guide](docs/BUILDING.md).

MIT permits commercial use and sublicensing subject to its terms. Acceptance does
not assign exclusive rights or erase contributor/upstream notices. A future Pro
variant must comply with all licenses, preserve required notices and review the
specific contribution/dependency rights. MIT-licensed versions remain available
under MIT; no promise to revoke those rights or exclusive relicensing claim.
