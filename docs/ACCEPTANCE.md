# Technische Abnahme / Technical acceptance — 1.0.0

**BESTANDEN — Benutzer bestätigt:** Sämtliche vorgesehenen Funktionen von RC2
funktionieren im realen Betrieb wie erwartet. Bestätigung vom 7. November 2026.
Keine zusätzlichen Geräte-/OS-Versionen, Messdauern oder kausalen FFB-Nachweise
erfunden. Stable übernimmt RC2 unverändert, abgesehen von Kennzeichnung/RC-Balken.

Automatisierte Qualifikation: alle vorhandenen .NET-/JavaScript-Suites, native
ABI-/Replay-/Identitätsprüfungen, Live-Capture inklusive Null-/Fehl-/Altdaten,
isolierte SQLite/HTTP/DPAPI/Prozessprüfungen, Netzwerk/QR/Backup/Restore und DE/EN.
Der Build liefert TEST_RESULTS.json. Frisch entpackte EXEs und Manifest werden
separat mit test-release.ps1 geprüft. Konkrete Binärprüfprotokolle und Hashes
werden beim lokalen Paket abgelegt; keine echte weitere Fahrt durch den Agenten
behauptet. Eine vollständige Clean-Windows-/Assistive-Technik-Matrix ist nicht
nachgewiesen und keine rechtliche Freigabe Teil dieser technischen Abnahme.

Für wiederholte Tests Originale erhalten: konsistentes Bibliotheksbackup vor
regulärem Tray-Ende, private Kopien von Bibliothek und Einstellungen verwenden.
**AutoStart=false nur in der Kopie**, damit ein Test nicht den produktiven
Autostart umbiegt. Keine Originaldatenbank überschreiben. Bestehende Geräte-
Dateien sind an denselben Windows-Benutzer gebunden; Zugangsdaten nicht teilen.

English: The owner confirms RC2 real-operation acceptance. 1.0.0 preserves that
scope. Automated assertions and isolated package checks supplement this statement;
they do not imply an exhaustive OS/browser matrix or public/legal approval.
