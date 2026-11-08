# Installation und Update — 1.0.0

[English](INSTALLATION.md). **Technisch stabile Version zur privaten Bereitstellung.** Öffentlicher Download steht noch zur Freigabe. MIT-Lizenz gemäß LICENSE.
Immer das vollständige ZIP verwenden, nicht nur eine einzelne EXE daraus.

## Voraussetzungen und Start

Windows-x64-Desktop im Supportbereich der mitgelieferten .NET-10-Laufzeit und ein
aktueller Browser. Für Live-Funktionen muss LMU auf demselben PC laufen; manuelle
Bibliotheksnutzung funktioniert ohne LMU. Kein SDK-/Runtime-Install nötig. Die
nativen Guards gelten für ABI 14000/14200; unbekannte ABI bleibt nicht verfügbar.
Eine vollständige OS-/Browser-Zertifizierung ist damit nicht verbunden.

1. ZIP vollständig in einen neuen lokalen Ordner entpacken. Laufzeitdateien zusammenlassen.
2. **LMU Setup Manager.exe** starten. Die App läuft ohne Konsole im Windows-Tray.
3. Oberfläche über das Tray öffnen. Ein geschlossener Browser beendet die App nicht.
4. Alte Version vor einem Wechsel über das Tray beenden. Einzelinstanz gilt pro
   Windows-Benutzer und Einstellungsverzeichnis.
5. Versionsanzeige 1.0.0 prüfen; kein neue Version-Balken. Keine Aktivierung, Bezahlschranken oder Limits.

## Bestandsinstallation sicher übernehmen

Zunächst ein konsistentes SQLite-Backup aus der alten Version exportieren. Alte
App über Tray beenden, neue Version in einen anderen Ordner entpacken. Nutzerdaten liegen
standardmäßig unter `%LOCALAPPDATA%\LmuFixedSetupManager\library` und
`%LOCALAPPDATA%\LmuFixedSetupManager\manager`; beide Verzeichnisse nicht löschen
oder zurücksetzen. Schema bleibt 4, ohne neue neue Version-Migration oder Datenreset.
Keine allgemeine Downgrade-Garantie für beliebige spätere Datenbankschemata.

Für die Abnahme mit privaten Kopien arbeiten: [sicherer Abnahmeplan](ACCEPTANCE.md).
In der kopierten Konfiguration Autostart vor dem Test ausschalten, damit der Test
keinen produktiven Windows-Starteintrag umstellt. Automatisierte Tests des Agenten
verwenden isolierte Daten und synthetische Schnittstellen, keine Produktivdaten.

## Heimnetz und gespeicherte Geräte

Standardmäßig aus. Einmal Verbindung und sichtbaren Sicherheitshinweis mit einer
Checkbox bestätigen; anschließend ein dauerhafter Ein/Aus-Schalter. Genehmigung
und Ein-Wunsch überstehen Neustarts. Fremdes/unklares Netz pausiert den Zugriff,
ohne sie zu löschen. Nur das exakt genehmigte Netz kann automatisch wieder aktiv
werden; private IP/Subnetz allein genügen nicht. Bewusst aus bleibt nach Neustart aus.
Entfernen der Genehmigung ist separat. Beides erhält Geräte; einzeln widerrufen.

QR-Einladungen gelten zehn Minuten und einmalig. Geräteberechtigung und Cookie
gelten höchstens 30 Tage, nicht unbegrenzt; Neustarts verlängern das nicht.
Gleichen Host und Browserspeicher nutzen. Nach Manager-Neustart die Seite neu laden,
damit sie den neuen CSRF-Token erhält. Kein neues Pairing allein wegen des Updates.

**HTTP ist unverschlüsselt.** Nur eigenes vertrauenswürdiges Heimnetz/Geräte,
kein Internetzugriff oder Router-Port-Forwarding. Windows darf öffentlich bleiben;
die App verändert weder Profil noch Firewall. Eine auf den alten EXE-Pfad begrenzte
manuelle Ausnahme kann für den neuen Ordner eine Prüfung benötigen. Erst der
Verbindungstest vom zweiten Gerät belegt Erreichbarkeit.

## Zur alten Version zurück

neue Version über Tray beenden, alte Version mit ursprünglichen Daten/Einstellungen starten.
Bei Test mit Kopien bleiben Originaldaten und produktiver Starteintrag unberührt.
Kein komplettes Restore allein wegen eines EXE-Wechsels durchführen. Backups und
Testkopien nicht veröffentlichen; sie können Notizen und Berechtigungen enthalten.

Keine automatischen Updates, Lizenzdienste oder Analyseuploads. Längerer echter
LMU-Freeze-/FFB-Test weiterhin offen. Mehr: [Sicherheit](SECURITY.de.md).
