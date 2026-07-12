# Windows-kluis

Windows Vault is het oorspronkelijke Windows-lid van de Vault-productfamilie. Het is een .NET 8 WPF-applicatie met een WebView2-editor, native applicatie-inventaris, handhavingsengine, runtime met aangepaste regels en een lokale web-app-bridge-hub.

De code is het productcontract. De bijgehouden in-app-handleiding is [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Huidige mogelijkheden

- Standaardgroepen voor geselecteerde Windows-applicaties en aangepaste groepen voor geavanceerde beleidsregels.
- Onmiddellijke, toelage- en aftelmodi; schema's; bevriezen; dutten; en groepsimport/export.
- Inventarisatie van Windows-applicaties en op vensters gebaseerde handhavingscomponenten.
- Een WebView2-editor gehost door `src/WindowsBlocker/WebAssets/`.
- Gecontroleerde uitvoering van aangepaste regels met syntaxiscontrole en een logfeed.
- Een loopback-bridgehub voor expliciet gekoppelde compatibele groepen.
- Native timer-, toast- en paneeloverlay-vensters.

## Bouw

Gebruik de ingecheckte oplossing en project:

```powershell
dotnet build WindowsBlocker.sln
```

Het applicatieproject richt zich op `net8.0-windows` en maakt gebruik van WPF plus WebView2. Bouw het en voer het uit op Windows met de vereiste .NET SDK en WebView2 runtime beschikbaar.

## Projectkaart

| Gebied | Bronmap |
| --- | --- |
| Groepsmodel en beleidsevaluatie | `src/WindowsBlocker/Core/` |
| Native handhaving | `src/WindowsBlocker/Enforcement/` |
| App-inventaris en WebView-bridge | `src/WindowsBlocker/WebUI/` |
| Runtime met aangepaste regels | `src/WindowsBlocker/Rules/` |
| Brughub | `src/WindowsBlocker/Bridge/` |
| WPF-vensters en overlays | `src/WindowsBlocker/` |

## Documentatie en vertalingen

Engelse documenten blijven canoniek. UI-labels gebruiken de volledige JSON-catalogi in `src/WindowsBlocker/WebAssets/translation/`; Vertaalde handleidingen staan ​​naast `manual/en.md`, en vertaalde kopieën van de overige bijgehouden documenten staan ​​onder `i18n-docs/<locale>/`.
