#Archivio di Windows

Windows Vault è il membro Windows nativo della famiglia di prodotti Vault. Si tratta di un'applicazione .NET 8 WPF con un editor WebView2, inventario di applicazioni native, motore di applicazione, runtime di regole personalizzate e un hub bridge di app Web locale.

Il codice è il contratto del prodotto. Il manuale in-app gestito è [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Capacità attuali

- Gruppi predefiniti per applicazioni Windows selezionate e gruppi personalizzati per regole di policy avanzate.
- Modalità immediata, indennità e conto alla rovescia; orari; congelare; posticipare; e importazione/esportazione di gruppo.
- Inventario delle applicazioni Windows e componenti di applicazione basati su finestre.
- Un editor WebView2 ospitato da `src/WindowsBlocker/WebAssets/`.
- Esecuzione controllata di regole personalizzate con controllo della sintassi e feed di registro.
- Un hub bridge di loopback per gruppi compatibili esplicitamente collegati.
- Finestre native di timer, toast e sovrapposizione del pannello.

## Costruisci

Utilizzare la soluzione e il progetto archiviati:

```powershell
dotnet build WindowsBlocker.sln
```

Il progetto dell'applicazione è destinato a `net8.0-windows` e utilizza WPF più WebView2. Costruiscilo ed eseguilo su Windows con il runtime .NET SDK e WebView2 richiesto disponibile.

## Mappa del progetto

| Zona | Directory di origine |
| --- | --- |
| Modello di gruppo e valutazione delle politiche | `src/WindowsBlocker/Core/` |
| Applicazione nativa | `src/WindowsBlocker/Enforcement/` |
| Inventario delle app e bridge WebView | `src/WindowsBlocker/WebUI/` |
| Runtime con regole personalizzate | `src/WindowsBlocker/Rules/` |
| Mozzo del ponte | `src/WindowsBlocker/Bridge/` |
| Finestre e sovrapposizioni WPF | `src/WindowsBlocker/` |

## Documentazione e traduzioni

I documenti inglesi rimangono canonici. Le etichette dell'interfaccia utente utilizzano i cataloghi JSON completi in `src/WindowsBlocker/WebAssets/translation/`; i manuali tradotti si trovano accanto a `manual/en.md` e le copie tradotte dei restanti documenti conservati si trovano in `i18n-docs/<locale>/`.
