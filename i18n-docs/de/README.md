# Windows-Tresor

Windows Vault ist das native Windows-Mitglied der Vault-Produktfamilie. Es handelt sich um eine .NET 8 WPF-Anwendung mit einem WebView2-Editor, nativem Anwendungsinventar, Durchsetzungs-Engine, Laufzeit für benutzerdefinierte Regeln und einem lokalen Web-App-Bridge-Hub.

Der Code ist der Produktvertrag. Das gepflegte In-App-Handbuch ist [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Aktuelle Fähigkeiten

- Standardgruppen für ausgewählte Windows-Anwendungen und benutzerdefinierte Gruppen für erweiterte Richtlinienregeln.
- Sofort-, Zeit- und Countdown-Modus; Zeitpläne; einfrieren; dösen; und Gruppenimport/-export.
- Windows-Anwendungsinventur und fensterbasierte Durchsetzungskomponenten.
- Ein WebView2-Editor, gehostet von `src/WindowsBlocker/WebAssets/`.
- Kontrollierte Ausführung benutzerdefinierter Regeln mit Syntaxprüfung und Protokoll-Feed.
– Ein Loopback-Bridge-Hub für explizit verknüpfte kompatible Gruppen.
- Native Timer-, Toast- und Panel-Overlay-Fenster.

## Bauen

Verwenden Sie die eingecheckte Lösung und das eingecheckte Projekt:

```powershell
dotnet build WindowsBlocker.sln
```

Das Anwendungsprojekt zielt auf `net8.0-windows` ab und verwendet WPF plus WebView2. Erstellen Sie es und führen Sie es unter Windows mit dem erforderlichen .NET SDK und der verfügbaren WebView2-Laufzeit aus.

## Projektkarte

| Bereich | Quellverzeichnis |
| --- | --- |
| Gruppenmodell und Richtlinienbewertung | `src/WindowsBlocker/Core/` |
| Native Durchsetzung | `src/WindowsBlocker/Enforcement/` |
| App-Inventar und WebView-Brücke | `src/WindowsBlocker/WebUI/` |
| Laufzeit benutzerdefinierter Regeln | `src/WindowsBlocker/Rules/` |
| Brückennabe | `src/WindowsBlocker/Bridge/` |
| WPF-Fenster und Overlays | `src/WindowsBlocker/` |

## Dokumentation und Übersetzungen

Englische Dokumente bleiben kanonisch. UI-Labels verwenden die vollständigen JSON-Kataloge in `src/WindowsBlocker/WebAssets/translation/`; Übersetzte Handbücher befinden sich neben `manual/en.md` und übersetzte Kopien der verbleibenden gepflegten Dokumente befinden sich unter `i18n-docs/<locale>/`.
