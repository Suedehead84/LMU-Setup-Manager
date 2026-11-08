# Sicherheit und Datenschutz

[English](SECURITY.md) · Geltungsbereich: 1.0.0, dauerhafter Heimnetzzugriff.

## Standardmäßig lokal

Der Windows-Manager betreibt seine eigene Weboberfläche, normalerweise auf
Loopback-Port 5087. Loopback bedeutet dieser PC, nicht Verschlüsselung oder Schutz
vor anderer Software unter demselben Windows-Benutzer. LAN ist standardmäßig aus.
Im geprüften Laufzeitcode gibt es keine Produkt-Cloud, Kontodienste, Analyseuploads,
Zahlungsendpunkte oder automatische Updates. NuGet/npm/GitHub beim Bauen sind
davon zu unterscheiden.

Die Anwendung liest LMU-Shared-Memory und fragt die lokale LMU-REST-Schnittstelle
unter `127.0.0.1:6397` per GET nach Katalog- und Navigationsbelegen ab.
Garagenabfragen gehören zur separaten Diagnose-CLI, nicht zum Produktionspolling.
Sie schreibt keine Setups ins Spiel. Browseranfragen bleiben beim gewählten
Manager-Host; QR und Übersetzungen entstehen lokal. Windows, Browser und LMU können
eigene externe Verbindungen haben, die der Manager nicht kontrolliert. Die optionale
Diagnose-CLI kann mit einem anderen REST-Ziel gestartet werden; das ist nicht der
Standard des produktiven Managers.

Der lokale Besitzer kann begrenzte Kanal-/Lastdiagnosen im Speicher über
`/api/manager/health` lesen. Remote-Geräte dürfen diesen Endpunkt nicht lesen.
Keine REST-Bodies/URLs, Zugangsdaten, persönlichen Namen oder Pfade; kein Tick-Log.
Diese Werte messen den Manager, nicht Spiel-Frametimes oder Force Feedback.

## Verarbeitete Daten

Die Bibliothek speichert Fahrzeug-/Strecken-/Layout-IDs, fünf Elektronikwerte,
Setup-Namen, Notizen, Profile, Favoriten, unveränderliche Revisionen,
Herkunftsbelege und Zeitstempel. Es gibt konservative Rundenreferenz-/Diagnose-
Bausteine, aber keine automatische Rennhistorienansicht. Eigene Notizen können
personenbezogene Angaben enthalten. Der native Decoder liest zusätzlich Session-,
Scoring- und Fahrerinformationen, darunter Fahrername und gegebenenfalls Steam-ID.
Die normale Bibliotheksübernahme speichert kein Fahrerprofil; separat aktivierte
JSONL-Diagnosen können native Rohwerte, REST-Inhalte, Namen/IDs und Angaben anderer
Teilnehmer enthalten. Rohaufzeichnungen vor einer Weitergabe redigieren. Eine
Anonymitätsgarantie gibt es nicht.

Standardpfade:

| Daten | Speicherort |
| --- | --- |
| SQLite-Bibliothek | `%LOCALAPPDATA%\LmuFixedSetupManager\library\setups.sqlite` |
| Export-/Migrations-/Restore-Backups | `backups` neben der verwendeten Datenbank |
| Windows-Einstellungen | `%LOCALAPPDATA%\LmuFixedSetupManager\manager\settings.json` |
| Geräteberechtigungen | `devices.protected` im Manager-Verzeichnis |
| Websprache | Browser-localStorage `lmu.ui.language` je Origin |
| Browseranmeldung | Zugriffscookie mit HttpOnly und SameSite=Strict |

Eigene Pfade ersetzen diese Vorgaben. Datenbank und heruntergeladene Backups sind
**nicht durch die Anwendung verschlüsselt**. Geräte-Metadaten und Zugangshashes
liegen per Windows-DPAPI für den aktuellen Benutzer geschützt vor; das
verschlüsselt weder Bibliothek noch Netzverkehr und schützt nicht vor kompromittierter
Software unter diesem Benutzer. Netzwerkeinstellungen enthalten Schnittstellen-/
Netzkennungen und lokale Adressen. Der letzte Gerätekontakt ist eine Statusangabe,
kein dauerhafter Erreichbarkeitsnachweis.

## Optionaler Heimnetzzugriff

Genau eine geeignete physische IPv4-Schnittstelle auswählen, dieses konkrete Netz
als vertrauenswürdig bestätigen, HTTP-Risiko akzeptieren und Firewall-Hinweis lesen.
Es gibt keine automatische Adapterwahl. Das Windows-Profil darf nach bewusster
Heimnetzbestätigung öffentlich bleiben; das macht unbekannte Netze nicht sicher.
VPN/Tunnel, mehrdeutige oder nicht unterstützte Adapter werden vorsichtig abgelehnt.
Die Bindung umfasst Loopback und die gewählte Adresse; Quellnetz sowie aktuelle
Adapter-/Netzmerkmale werden geprüft. Geänderte oder unklare Merkmale sperren
den Zugriff. Unbestätigte Erkennung setzt eine bestehende Freigabe aus; nur das
exakt bestätigte Netz kann automatisch wieder aktiv werden. Positiv geänderte
Netzidentität bleibt gesperrt, ohne Wunsch/Genehmigung zu löschen. Rückkehr zum
exakt genehmigten Netz kann automatisch freigeben. Bewusst aus bleibt nach Neustarts
aus und erhält Genehmigung/Geräte. Genehmigung entfernen ist separat bestätigt und
erhält Geräte ebenfalls. Netz-/Hostwechsel verwerfen offene Einladungen; reine
Sprachänderungen nicht.

**HTTP ist ohne Transportverschlüsselung.** Andere Netzteilnehmer können
Bibliotheksdaten, Einladungs- und Zugangstokens mitlesen oder verändern.
Anmeldung, DPAPI, QR, CSRF und Origin-Prüfung beheben das nicht. Nur eigenes
vertrauenswürdiges Heimnetz und vertrauenswürdige Geräte verwenden. Kein
Internetzugriff, Router-Port-Forwarding, Gastnetz, Hotel oder Hotspot; keine
Zertifikatswarnungen umgehen. HTTPS ist nicht eingerichtet.

Die App ändert weder Firewall noch Router oder Windows-Profil. Die ausdrücklich
angeforderte Anleitung beschreibt eine manuelle Ausnahme für EXE, Schnittstelle,
lokale Adresse, TCP-Port und Quellnetz. Vorhandene breite/doppelte Freigaben prüfen:
Eine enge neue Regel begrenzt keine andere Regel. Ein anderer EXE-Pfad kann eine
Regelanpassung verlangen. Erfolgreiches Binden beweist keine Erreichbarkeit;
am zweiten Gerät prüfen.

## Anmeldung und Widerruf

Das Tray öffnet Besitzerzugriff mit einem zufälligen Laufzeitschlüssel. Das lokale
Browsercookie gilt acht Stunden; nach Neustart gegebenenfalls über das Tray neu
öffnen. QR und kopierbarer Link enthalten dasselbe zehn Minuten gültige,
einmalige Einladungsgeheimnis. Beides wie Zugangsdaten behandeln: nicht fotografieren,
veröffentlichen oder weiterleiten. Einladungen liegen nur im Speicher und überleben
keinen Neustart. Einlösen erzeugt eine individuelle Geräteberechtigung für höchstens
30 Tage; der Zugang wird gehasht in der DPAPI-Datei gespeichert. QR/Link werden
nicht als dauerhafte QR-Datei abgelegt.

Freigegebene Geräte können die **gesamte Bibliothek lesen und bearbeiten**, auch
die über die API bereitgestellten Backup-/Restore-Funktionen. Lokale Manager-
Einstellungen dürfen sie nicht verwalten. Geräte einzeln in Einstellungen widerrufen;
LAN-Aus erhält die Gerätefreigaben. Bestehende Berechtigungen können Neustarts
überleben, wenn die gespeicherte Netzfreigabe weiterhin passt. Besitzerverwaltung
verlangt Loopback und Besitzerschlüssel. API-Schreibzugriffe benötigen CSRF,
fremde Origins werden abgewiesen, Anmeldung ist ratenbegrenzt und eine restriktive
CSP begrenzt Browserinhalte. Das sind gezielte Schutzmaßnahmen, keine Garantie
gegen bösartige lokale Software oder manipulierten HTTP-Netzverkehr.

## Backup, Wiederherstellung und Löschen

Ein konsistentes SQLite-Backup erstellen und zusätzlich herunterladen. Kopien
genauso schützen wie die aktive Bibliothek. Restore prüft die Datei und eine
gegebenenfalls migrierte private Kopie, zeigt eine Vorschau und verlangt eine
exakte Bestätigungsphrase. Vorschauen gelten 15 Minuten; zwischenzeitliche Änderungen
verhindern das Ersetzen. Vor dem Austausch der ganzen Bibliothek entsteht ein
Vorab-Backup. Restore ist kein Import eines einzelnen Setups.

Papierkorb bedeutet **Soft Delete**; Revisionen bleiben erhalten. Es gibt keine
UI zum endgültigen Löschen einzelner Setups und keine sichere Datenträgerlöschung.
Für die vollständige manuelle Entfernung zuerst Geräte widerrufen, LAN und
Autostart ausschalten und den Manager beenden. Danach die tatsächlich verwendete
Datenbank samt gegebenenfalls verbliebenen WAL-/SHM-Dateien, Backup- und Manager-
Verzeichnis sowie eigene Daten-/Einstellungspfade bewusst entfernen. Auch Downloads,
Diagnosen und nicht mehr benötigte Programmordner berücksichtigen. Cookies und
Websitedaten auf jedem Gerät löschen, selbst angelegte Firewall-Ausnahmen entfernen.
Das ist nicht rückgängig zu machen; nötige Sicherungen vorher behalten. Kopien
auf anderen Geräten oder forensische Wiederherstellung kann die App nicht beseitigen.

## Sicherheitsprobleme melden

Vertrauliche Sicherheitsmeldungen: [GitHub Private Vulnerability Reporting](https://github.com/Suedehead84/LMU-Setup-Manager/security/advisories/new). Die Funktion ist aktiviert; Suedehead84 bestätigt regelmäßige Überwachung. Keine Sicherheitsdetails oder persönlichen Daten in öffentliche Issues stellen. Unterstützt: 1.0.x, keine feste Reaktionsfrist. Ein unabhängiger Zustellungstest ist noch nicht erfolgt. Siehe [Sicherheitsrichtlinie](../SECURITY.md).
