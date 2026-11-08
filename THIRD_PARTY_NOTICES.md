# Third-party notices

Original application code is MIT under root LICENSE; the following upstream
works retain their own licenses, copyrights and attribution requirements.
No SDK/game trademark or comprehensive legal clearance is implied.

B2.1 includes the Microsoft ASP.NET Core runtime 10.0.14 (MIT and bundled
third-party notices). See docs/licenses/ASP.NET-Core-MIT.txt and
docs/licenses/ASP.NET-Core-THIRD-PARTY-NOTICES.txt (release: licenses/).
https://github.com/dotnet/aspnetcore

Phase B1 embeds Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core 10.0.14,
copyright (c) .NET Foundation and Contributors, MIT license.
Full license: docs/licenses/Microsoft.Data.Sqlite-MIT.txt (release: licenses/).
Source: https://github.com/dotnet/efcore/tree/v10.0.14/src/Microsoft.Data.Sqlite.Core

SQLitePCLRaw.core, bundle_e_sqlite3, provider.e_sqlite3 and lib.e_sqlite3 2.1.13:
Copyright 2014-2024 SourceGear, LLC; Apache License 2.0.
Full license: docs/licenses/SQLitePCLRaw-Apache-2.0.txt (release: licenses/).
Source: https://github.com/ericsink/SQLitePCL.raw
These upstream components are used without source modifications.
The SQLite engine is dedicated to the public domain: https://www.sqlite.org/copyright.html
The self-contained release also includes the Microsoft .NET runtime and its upstream notices.

The byte-layout constants and LMU_Data reader design are adapted from
Swizzjack/lmu-pitwall, commit a171b80a7c8c1e02a9d029e32fd717c834d26e71 (MIT).
The full required notice is in docs/lmu-pitwall-LICENSE.txt and must accompany binaries.
https://github.com/Swizzjack/lmu-pitwall

snipem/go-lmu-api commit af4292e899b285821b8bf9a3e4eaca492e3c5495 was inspected
only to identify REST paths and JSON property names. No client/model code copied.
No root LICENSE was present in that checkout; no permission to copy it is assumed.
https://github.com/snipem/go-lmu-api

LMU Engineer manual and Studio 397 telemetry guide were read as documentation only.
No code or assets copied. This project does not grant rights to LMU SDK headers.

B2.5: Windows Forms / Windows Desktop runtime is redistributed under the existing
Microsoft .NET notices (docs/licenses/NET-runtime-MIT.txt) and the separate full
Windows Desktop license at docs/licenses/WindowsDesktop-runtime-MIT.txt. The geometric manager
icon and SVG are original project artwork; no game or manufacturer logo was used.

B2.5.2: qrcode-generator 2.0.4 by Kazuhiko Arase, MIT, is vendored unchanged at
src/Lmu.Web/wwwroot/vendor/qrcode-2.0.4.js and included locally in the browser.
https://github.com/kazuhikoarase/qrcode-generator
Complete notice: docs/licenses/qrcode-generator-MIT.txt (also in release licenses/).
SHA256: 79EC86F82856005B1C887905CFCCFCFBEC3821CA61C7FD5A952FAA5F778F791C.

jsQR 1.4.0 by Cosmo Wolfe and contributors, Apache-2.0, is vendored unchanged at
tests/vendor/jsQR-1.4.0.js for independent offline decoding tests only.
https://github.com/cozmo/jsQR
Full license: docs/licenses/jsQR-Apache-2.0.txt. Decoder code is not shipped in the app.
SHA256: BC40C8A15196236B2314DB0856F72CA0B49980CD5413B8C852A7349F5FEE0859.
Pinned package provenance and verified tarball SHA512: docs/QR_DEPENDENCIES.json.

Notice completion for 10.0.14: full Windows Forms third-party notice at docs/licenses/WindowsForms-THIRD-PARTY-NOTICES.txt (including Ookii.Dialogs BSD notice), from https://raw.githubusercontent.com/dotnet/winforms/v10.0.14/THIRD-PARTY-NOTICES.TXT . The Windows Desktop NuGet package contains LICENSE but no aggregate notice. WPF upstream notices are retained conservatively in docs/licenses/WPF-THIRD-PARTY-NOTICES.txt from https://raw.githubusercontent.com/dotnet/wpf/v10.0.14/THIRD-PARTY-NOTICES.TXT ; application uses Windows Forms, not WPF. The build-only ILLink package notice is in docs/licenses/ILLink-build-THIRD-PARTY-NOTICES.txt. Source and binary package retain these full texts (binary path: licenses/). See docs/PUBLICATION_REVIEW.md for dependency scope and unresolved publication conditions.
