# Skarbiec Windows

Windows Vault jest natywnym członkiem rodziny produktów Vault dla systemu Windows. Jest to aplikacja WPF .NET 8 z edytorem WebView2, natywnym spisem aplikacji, mechanizmem egzekwowania, środowiskiem wykonawczym reguł niestandardowych i lokalnym centrum pomostowym aplikacji internetowych.

Kod jest umową dotyczącą produktu. Obsługiwany podręcznik w aplikacji to [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Aktualne możliwości

- Domyślne grupy dla wybranych aplikacji Windows i grupy niestandardowe dla zaawansowanych reguł polityki.
- Tryby natychmiastowe, zasiłkowe i odliczania; harmonogramy; zamrażać; drzemka; oraz grupowy import/eksport.
- Spis aplikacji Windows i komponenty wymuszające oparte na oknach.
- Edytor WebView2 hostowany przez `src/WindowsBlocker/WebAssets/`.
- Kontrolowane wykonywanie reguł niestandardowych ze sprawdzaniem składni i kanałem dziennika.
- Koncentrator mostu pętli zwrotnej dla jawnie połączonych kompatybilnych grup.
- Natywny timer, tosty i okna nakładek panelu.

## Kompiluj

Skorzystaj z sprawdzonego rozwiązania i projektu:

```powershell
dotnet build WindowsBlocker.sln
```

Projekt aplikacji jest przeznaczony dla `net8.0-windows` i wykorzystuje WPF oraz WebView2. Zbuduj i uruchom go w systemie Windows z wymaganym zestawem SDK .NET i dostępnym środowiskiem wykonawczym WebView2.

## Mapa projektu

| Powierzchnia | Katalog źródłowy |
| --- | --- |
| Model grupowy i ocena polityki | `src/WindowsBlocker/Core/` |
| Natywne egzekwowanie | `src/WindowsBlocker/Enforcement/` |
| Spis aplikacji i mostek WebView | `src/WindowsBlocker/WebUI/` |
| Środowisko wykonawcze reguł niestandardowych | `src/WindowsBlocker/Rules/` |
| Węzeł mostowy | `src/WindowsBlocker/Bridge/` |
| Okna i nakładki WPF | `src/WindowsBlocker/` |

## Dokumentacja i tłumaczenia

Dokumenty angielskie pozostają kanoniczne. Etykiety interfejsu użytkownika korzystają z pełnych katalogów JSON w `src/WindowsBlocker/WebAssets/translation/`; przetłumaczone podręczniki znajdują się obok `manual/en.md`, a przetłumaczone kopie pozostałych utrzymywanych dokumentów znajdują się pod `i18n-docs/<locale>/`.
