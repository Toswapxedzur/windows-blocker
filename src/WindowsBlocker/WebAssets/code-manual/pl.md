# Podręcznik kodu Adamancia Vault dla Windows

[Podręcznik użytkownika](../manual/pl.md)

## Zasady działania reguły

Source: jedno wyrażenie funkcji `(on, v) => { ... }`. Obsługiwane są tylko synchroniczne JavaScript i poniższe API; bez timers, network, native system APIs i dostępu do stron browser. Reguły czasowe używają `ev.now` i events. Reguły browser używają podręcznika kodu rozszerzenia.

- Edycja zapisuje wersję roboczą; **Run** aktywuje ją i włącza grupę. Zamrożonych grup nie można uruchomić przyciskiem Run. Pusty source wyładowuje regułę.
- Udany Run zastępuje handlers/panels i czyści app-block set tej grupy, zachowując `v.state`. Błąd kompilacji/rejestracji pozostawia poprzednią regułę; timeout może ją zatrzymać. Restart rejestruje ponownie ostatni aktywowany source; closure variables i app-block sets są resetowane.
- Rejestracja może inicjować state, rejestrować handlers, wyświetlać panels i zapisywać log. Akcje app/file i emits należą do handlers; kolejka rejestracji jest odrzucana.
- Disable wstrzymuje handlers i usuwa panels/app blocks. Enable przywraca załadowaną regułę i zachowane panels/blocks. Delete usuwa handlers/state/effects. Wcześniej zamknięte aplikacje nie są otwierane ponownie; zapisy plików nie są cofane.
- Events nie są ograniczone zwykłymi celami grupy; wybieraj aplikacje w regule. Actions trafiają do kolejki i są stosowane po dispatch. Wyjątki zatrzymują handler bez cofania state/actions; kolejne handlers mogą nadal działać. Tylko file actions mają result events.

## Zarządzane timery i użycie w ruchomym oknie

`v.timer(id, {direction:"down", durationMs:300000})` tworzy lub zwraca nazwany licznik odliczający, początkowo wstrzymany. Użyj `{direction:"up"}` dla licznika zliczającego w górę. Uchwyt udostępnia `start()`, `pause()`, `resume()`, `reset()` i `snapshot()`. Start/resume są idempotentne; reset przywraca stan wstrzymany z zerowym czasem, który upłynął. Pola migawki obejmują `ready`, `error`, `elapsedMs`, `remainingMs` (null przy zliczaniu w górę), `running` i `completed`.

Uruchomione timery odmierzają czas zegarowy także wtedy, gdy aplikacja/przeglądarka jest zamknięta lub reguła wyłączona. Zakończone odliczanie dostarcza `timerEnded` z `{id, direction, durationMs, generation, incarnation}` raz na wcielenie i generację timera każdemu członkowi przy następnym wykonaniu jego reguły; Run nie powtarza tego zdarzenia. Dostarczenie zależy od zdarzeń hosta; nie jest to alarm czasu rzeczywistego. Umieść `{type:"timer", timerId:"focus", label:"Focus"}` w panelu, aby wyświetlić timer przez istniejącą kontrolkę tekstową.

`v.rolling(id, {windowMs:3600000, limitMs:600000})` tworzy lub zwraca nazwany licznik aktywnego użycia. `assign({websites:["youtube.com","reddit.com"], apps:[]})` zastępuje przypisania celów tego członka. Reguły natywne mogą zamiast tego przypisywać identyfikatory aplikacji danej platformy. Domeny obejmują subdomeny. Jeden licznik sumuje użycie swoich celów; inne identyfikatory mają niezależne limity. `snapshot()` zwraca `ready`, `error`, `usedMs`, `remainingMs`, `available` i `nextReturnAtMs` (najbliższą milisekundę, w której wygasa dowolne zapisane użycie). `available(target?)` sprawdza również przypisanie, jeśli podano cel. `reset()` czyści historię tego obiektu.

Liczy się całe zaobserwowane użycie spełniające warunki, także ponad przyznany limit; sam licznik nie blokuje strony/aplikacji. Nakładające się przedziały widoczności stron liczą się raz, a aplikacje natywne zliczają zaobserwowane użycie na pierwszym planie. Wyłączone reguły nie dodają użycia; historia nadal wygasa na dokładnych granicach przedziałów. Dostępność wraca dopiero wtedy, gdy pozostała historia jest poniżej limitu. Próbki sygnału aktywności są ograniczone do pięciu sekund; przerwy wynikające z zamknięcia/wyłączenia nie tworzą aktywności. Zdarzenia emitowane przez użytkownika nie dodają użycia.

Te same identyfikatory obiektów w połączonych grupach niestandardowych współdzielą jeden lokalny natywny hub jako źródło rozstrzygające. Łączenie scala historię i przypisania liczników oraz zachowuje stan timera inicjującego członka; niezgodne konfiguracje uniemożliwiają połączenie. Każdy członek otrzymuje własne zdarzenie zakończenia. Utrata połączenia powoduje niedostępność wspólnych liczników i odrzucanie poleceń timera do czasu powrotu aktualnego źródła rozstrzygającego. Po rozłączeniu każdy członek zachowuje końcowe wspólne użycie i stan timera, przywraca własne cele i odrzuca polecenia ze starego połączenia. Hub przechowuje końcową migawkę, dopóki powracający członek jej nie zapisze i nie potwierdzi; oczekujące przekazania kończą się przed udostępnieniem nowego połączenia.

Nazwane obiekty przetrwają Run i ponowne uruchomienie w zarezerwowanym `v.state.$vaultTime`; nie edytuj tego pola bezpośrednio. Usuwaj je jawnie przez `v.removeTime(id)`. Identyfikatory mają 1–80 liter ASCII, cyfr, `_` lub `-`; zarezerwowane nazwy właściwości są odrzucane. Do 32 obiektów współdzieli limit stanu reguły wynoszący 64 KiB UTF-8. Czas trwania to całkowita liczba milisekund, maksymalnie 365 dni. Historia ruchomego okna jest ograniczona do 1024 koszyków minutowych i 2048 scalonych przedziałów. Błędy pojemności historii pozostawiają licznik niedostępny do jawnego zresetowania; nieprawidłowe konfiguracje i zbyt duże zapisy zgłaszają błędy bez usuwania zapisanych danych.

```js
(on, v) => {
  const timer = v.timer("focus", {durationMs: 300000});
  v.panel("focus", {controls: [
    {type: "timer", timerId: "focus", label: "Focus"},
    {id: "start", type: "button", label: "Start"},
    {id: "pause", type: "button", label: "Pause"},
    {id: "reset", type: "button", label: "Reset"}
  ]});
  on("panel", ev => {
    if (ev.data.eventName !== "click") return;
    if (ev.data.controlId === "start") timer.start();
    if (ev.data.controlId === "pause") timer.pause();
    if (ev.data.controlId === "reset") timer.reset();
  });
  on("timerEnded", ev => v.log("Finished", ev.data.id));
}
```

## API

- `on(type, handler)` → boolean. Rejestruje `handler(ev)`; wiele handlers działa w kolejności rejestracji. False oznacza nieprawidłowe argumenty lub osiągnięcie limitu handlerów. `ev = { type: string, now: number, origin: "host" | "user" | "rule", data }`; `now` to milisekundy Unix. Rzeczywiste zdarzenia widocznych kontrolek mają `origin: "user"`; zdarzenia `v.emit` mają `origin: "rule"`.- `v.state`: modyfikowalny obiekt JSON, zapisywany po event dispatch. Inicjalizuj brakujące fields zamiast nadpisywać istniejący state. Przypisanie non-object lub tablicy resetuje go do `{}`; aktualizacje niepodlegające serializacji lub zbyt duże nie są zapisywane.
- `v.log(...values)`: jedyne źródło Log tej grupy. Logs/Clear niezależne dla grup. Błędy ładowania pojawiają się w statusie Run; diagnostyka handlerów nie trafia do Log.
- `v.emit(type, data)`: umieszcza kopię JSON `data` w kolejce handlers tej grupy po bieżącym evencie, ze świeżym `now`; to nie jest wywołanie synchroniczne.
- `v.panel(id, spec)`: zastępuje nazwany pływający panel grupy; null `spec` usuwa go. Zobacz Panels.
- `v.file(op, path, payload?)` → string request ID. Zobacz Files.
- `v.store(request)` zwraca identyfikator żądania; zobacz rekordy grupy.
- `v.block(appId, on)`: true utrzymuje blokadę aplikacji, false usuwa blokadę tej grupy. Blokady włączonych grup łączą się; to wywołanie nie odblokuje celu innej grupy. Blocking żąda zwykłego zamknięcia i ponawia je w odstępie z Settings; nie zapobiega uruchomieniu procesu ani nie gwarantuje akceptacji Quit.
- `v.quit(appId)`: jedno żądanie zwykłego zamknięcia, według tej samej ochrony i ponawiania; bez ciągłej blokady.
- `v.open(appId)`: prosi Windows o otwarcie zainstalowanej aplikacji; bez callbacku sukcesu.

Pozostałe wywołania zwracają `undefined` `com.valvesoftware.steam`. App IDs to pełne ścieżki executable lub application user model IDs dostępne w events i selektorze aplikacji. Block/Quit ignorują procesy systemowe Windows, browsers, Vault i jego helpery oraz puste IDs. Panel IDs/state należą do grupy, nie nazwy wyświetlanej.

## Events

Notacja payload opisuje typy, nie kod wykonywalny. `?` oznacza opcjonalne fields.

```text
tick (~1 second): { frontmost: App | null, running: App[] }
app: { kind: "launch" | "quit" | "focus" | "blur" | "hide" | "unhide",
       appId: string, name: string, previousAppId?: string | null }
snooze: {}
panel: { panelId: string, controlId: string, eventName: string,
         value: string, values: { [controlId: string]: string } }
file: see Files
record: { requestId: string, ok: boolean, op?: string, key?: string,
          record?: Record | null, entries?: Header[], next?: string | null,
          deleted?: number, error?: string }
App = { appId: string, name: string }
```

- `tick` jest przybliżony; używaj timestamps, nie liczby ticków. Running wymienia rozpoznane procesy aplikacji Windows. Frontmost może być null lub mieć puste app ID.
- `app` zgłasza zaobserwowane zmiany cyklu życia przed event `tick` danego ticka. Tylko focus zawiera `previousAppId` (null, gdy nieznany). Nazwy są wyświetlane, nie stabilne IDs.
- `snooze` oznacza naciśnięcie przycisku Snooze grupy. Samo nie powoduje pauzy.
- Odpowiedzi file trafiają do grupy żądającej. Dopasuj `requestId`, sprawdź `ok` i ustal deadline przy użyciu ticks: odpowiedzi mogą zaginąć po przeładowaniu/wyłączeniu reguły. Request IDs mogą powtarzać się po Run; oczekujące requests nie są trwałą pracą.

## Panels

```text
spec = { title?: string, description?: string, controls?: Control[],
         position?: "top-left" | "top-right" | "bottom-left" | "bottom-right" | "center",
         width?: "small" | "medium" | "large" | number }
Control = { id?: string, type?: string, label?: string, value?, disabled?: boolean,
            ...type-specific fields below }
```

Domyślnie: prawy dół, szerokość 300px; presety small/medium/large to 220/280/360px; szerokość liczbowa ograniczona do 180–520px, obsługuje pixel strings. Natywne panels/sections układają controls pionowo; browser layout, alignment, role, autofocus i control-dimension fields nie wpływają na native renderer.

IDs normalizują się do ASCII letters/digits/`_`/`-` (maks. 80); wybieraj unikalne, stabilne IDs. Pominięte control ID staje się `control-N`; pominięty/nieznany type staje się text. Pominięte text/lists są puste; disabled to false. Każde wywołanie zastępuje całą spec. Jawne `value` ma pierwszeństwo; bez niego używana jest ostatnia event value, a potem normalizacja typu. Native events dostarczają strings: sparsuj je do zadeklarowanego value type przed renderowaniem panelu. Nieznane fields są odrzucane; panel colors/fonts/CSS należą do Vault.

Pola i wartości początkowe controls:

- `text`: string `text`; domyślnie label. `html`: string `html`, oczyszczony i wyświetlony jako zwykły tekst w Windows.
- `button`: `label`, opcjonalne `action: "submit" | "cancel" | "close"`. Click value to ciąg action lub pusty. Actions same niczego nie wysyłają/nie zamykają.
- `checkbox`, `toggle`: boolean `value` (domyślnie false); event value to `"true"`/`"false"`.
- `select`, `radio`: `options: (string | { value: string, label?: string })[]`; string value (domyślnie pusty). Puste option values są usuwane; labels domyślnie równe value. Aktualizuj typowaną wartość panelu po wyborze.
- `textInput`, `textarea`: string value (domyślnie pusty); textInput `placeholder`; textarea `rows` 1–12 (domyślnie 3). Native textarea ignoruje placeholder.
- `numberInput`, `range`: numeric value (domyślnie 0), `min`, `max`, dodatni `step`. Przy aktualizacji panelu values są ograniczane; nieokreślone granice normalizacji to −1000000…1000000. Native numberInput to pole tekstowe: sam sprawdź `Number(event.value)`; min/max/step nie ograniczają wpisywania. Native range domyślnie 0…100 ze step 1.
- `date`, `time`: tekst; formaty początkowe `YYYY-MM-DD`, `HH:MM`/`HH:MM:SS` (niepoprawne stają się puste). Sam sprawdzaj zmiany. `color`: `#RRGGBB` (domyślnie `#000000`).
- `pin`: ciąg cyfr; `length` 3–12 (domyślnie 6), `masked` domyślnie true, `autoSubmit` false. `section`: `text`, `controls`; child sections na depth 3 nie mają children (root controls depth 0).

Panel events: zwykłe wejścia wysyłają `change`; buttons tylko `click`; PIN wysyła `change` oraz `submit`, gdy autoSubmit zostanie wypełnione. Brak native mount/unmount/focus/key events. Values to strings, również dla numbers/booleans. `values` zawiera wartości wejściowe wyrenderowanej migawki i może nie uwzględniać wywołującej zmiany; `value` wskazuje tę zmianę. Zapisuj w `v.state` i renderuj typowane values dla niezawodnych formularzy. Non-click events są łączone w ciągu 100ms dla każdego control; nie licz ich jako naciśnięć klawiszy.

Limity tekstu: title/label 240; description/text 1000; HTML 20000; placeholder 500; input text 2000; inne value strings 512; option value/label 256. Nadmiar jest obcinany.

## Files

`op`: `"read"`, `"write"`, `"append"`, `"list"`, `"exists"`. Wymaga **Folderu reguł niestandardowych** w Settings i jego uprawnienia.

- `path` jest względny; `/` oddziela katalogi. Segmenty dopuszczają ASCII letters/digits, spacje i `_.,@()-`; bez początkowej kropki, `.`/`..`, absolute path lub URL. Rozszerzenia: `.txt`, `.csv`, `.json` (bez rozróżniania wielkości liter). List path to directory; `""` wyświetla wybrany root. Ścieżki poza wybrany folder, również przez symlinks, są odrzucane.
- Read zwraca tekst UTF-8. Write zastępuje/tworzy; append tworzy/dopisuje bez automatycznego newline. Katalogi nadrzędne tworzone są podczas zapisu. String payload zapisywany dosłownie; inne JSON payloads serializowane; null/pominięcie oznacza pusty tekst. Parsowanie JSON/CSV należy do reguły. Maksymalny rozmiar pliku: 1048576 UTF-8 bytes.
- List zwraca bezpośrednio widoczne podkatalogi i obsługiwane pliki. Entries: `{ name: string, path: string, kind: "directory" | "file", extension?: string }`; extension pliku zawiera kropkę. Exists zwraca boolean dla obsługiwanego file path.

```text
file.data = { requestId: string, op: string, path: string, ok: boolean,
              text: string | null, entries: Entry[] | null,
              exists: boolean | null, error: string }
```

Nieużywane result fields są null; sukces ma pusty error. Błędy obejmują invalid-path, unsupported-file-type, folder unavailable, missing file i file-too-large. Traktuj error jako string, nie stały pełny enum. Brak transaction API; serializuj operacje read-modify-write dla każdego path.

## Rekordy grupy

`v.store(request)` rozpoczyna asynchroniczną operację rekordu JSON wewnątrz handlera. Rekordy są prywatne dla grupy, pozostają po Run i ponownym uruchomieniu, a usuwane są wraz z grupą. Połączone grupy mają osobne magazyny. Reguła wybiera klucze, wartości, wersje, unieważnianie cache i priorytet informacji zwrotnej. Reguły Mac i przeglądarki używają tego samego API, ale każda aplikacja/przeglądarka ma oddzielny magazyn.

| Request | Result |
|---|---|
| `{op:"get", key}` | `record` lub `null` |
| `{op:"put", key, value, version?, ifRevision?}` | zapisany `record` |
| `{op:"delete", key, ifRevision?}` | `record: null` |
| `{op:"list", prefix?, after?, limit?}` | posortowane `entries` i `next` |
| `{op:"clear", prefix?}` | liczba `deleted` |

Rekord ma postać `{value, revision, version, updatedAt}`. Nieprzezroczysta revision zmienia się przy rzeczywistym zapisie; `version` to metadane wywołującego. `list` zwraca nagłówki bez wartości; `get` odczytuje rekord, a `after: next` pobiera następną stronę. Bez `put`/`delete`, `ifRevision` put/delete są bezwarunkowe; `null` wymaga nieobecnego klucza, ciąg revision dokładnej zgodności. Konflikt zwraca `ok:false`, `error:"store-conflict"` i bieżący `record`, co zapobiega nadpisaniu informacji użytkownika przez spóźnioną automatyczną odpowiedź. `v.context.revision` to nieprzezroczysty znacznik konfiguracji, który reguła może uwzględnić w swojej wersji. Framework nie narzuca polityki cache.

Limity: klucz 1–2048 znaków, wersja ≤128, wartość JSON ≤64KiB, głębokość ≤32, 10000 kluczy na grupę, cały dokument16MiB. `list` zwraca maksymalnie100 nagłówków i96KiB na stronę. Maks.32 żądania na grupę i128 rozpoczęć/sekundę łącznie; przeciążenie zgłasza `store-busy` raz na grupę na sekundę i odrzuca dalsze żądania. Błędy magazynu zwracają `ok:false`; przyszły/uszkodzony dokument pozostaje zachowany i zgłasza `store-unavailable`. Brak automatycznych ponowień i usuwania. Używaj ticks do terminów.

## Limity

Na event na group: 256 queued actions, 200 log calls, 64 emits; nadmiar jest odrzucany. Na rule: 1000 handlers, 24 panels; każda control list ma 32 entries, a każdy choice 64 options; nadmiar ignorowany/obcinany. Emit chains zatrzymują się po 16 generacjach. Serialized state limit: 65536 JavaScript string characters. Utrzymuj registration i łączne handlers każdego event poniżej 1 sekundy; powtarzające się przekroczenia lub hard timeout zatrzymują regułę do Run. Log przechowuje 200 entries na grupę. Timing/replies są best-effort, bez gwarancji czasu rzeczywistego.

## Pełna reguła

Steam jest blokowany, z wyjątkiem pięciominutowej pauzy uruchomionej przez Snooze lub jego panel button:

```javascript
(on, v) => {
  v.state.pauseUntil ??= 0;
  const pause = ev => { v.state.pauseUntil = ev.now + 300000; };
  v.panel("pause", { controls: [{ id: "pause", type: "button", label: "Pause 5 min" }] });
  on("snooze", pause);
  on("panel", ev => {
    if (ev.data.panelId === "pause" && ev.data.controlId === "pause" && ev.data.eventName === "click") pause(ev);
  });
  on("tick", ev => v.block("C:\\Program Files (x86)\\Steam\\steam.exe", ev.now >= v.state.pauseUntil));
}
```


## Wywołania AI z działającej reguły

Każde wywołanie `v.ai` wskazuje połączenie i model. Opcjonalny zapisany prompt i kontekst są dostępne w handlerach przez `v.context.ai.prompt` i `v.context.ai.context`; Vault nie wysyła ich automatycznie. Reguła wybiera zawartość każdego wywołania `v.ai`. Zmiana ustawień unieważnia odpowiedzi starej konfiguracji.

Opcjonalna nazwa `conversation` zapisuje lokalnie pełny zapis użytkownika i asystenta; następne wywołanie dołącza do16KB najnowszych wiadomości. Model można zmienić. Rozmowy pozostają po usunięciu lub odłączeniu grupy. Zaimportowane stare historie o tej samej nazwie są rozdzielane i zgłaszane w `preservedConversations`. Użyj `v.ai({op:"delete", conversation:"name"})`, aby usunąć wspólną rozmowę; odpowiedź przychodzi w zdarzeniu `ai`. Aktywne wywołanie o tej samej nazwie zwraca `ai-conversation-busy`.

`v.ai({ model, reasoningEffort?, conversation?, prompt, input? })` od razu zwraca ID żądania. Wywołuj je wewnątrz handlera. `model` to dostępne ID; effort to obsługiwane `default`, `low`, `medium` lub `high`. `input` przyjmuje dowolny JSON; prompt i input mają limit20KB każdy. Replies arrive as `ai` events: `{requestId, ok, text, citations,
error, preservedConversations?}`. Success text is at most48KB; citations are `{url,title}`. Preserve visible,
clickable source links when displaying answers based on web search. Search is always
available to the model. Generation requests have no fixed app deadline. Several
named conversations can run at once; calls using the same name run in order.
Start another call from an `ai` event for a sequence. Vault nie planuje jeszcze opóźnionych wywołań. Możliwości i limity dostawcy mogą kolejkować lub odrzucać żądania; brak zastępczego dostawcy i automatycznych ponowień. Odpowiedzi to dane, nie JavaScript; działania wybiera reguła.

Przykład panelu z przyciskiem `ask` i polem `note`:

```javascript
(on, v) => {
  let pending = null;
  on("panel", ({ data }) => {
    if (data.controlId !== "ask" || pending) return;
    pending = v.ai({ model: "chatgpt:MODEL_ID", conversation: "notes", prompt: v.context.ai.prompt || "Summarize this text.", input: {context:v.context.ai.context, note:data.values?.note ?? ""} });
  });
  on("ai", ({ data }) => {
    if (data.requestId !== pending) return;
    pending = null;
    if (!data.ok) { v.log("AI request failed:", data.error); return; }
    v.log(data.text, data.citations);
  });
}
```

Oczekujące wywołania nie są ponawiane po restarcie. Run, wyłączenie/ponowne włączenie i usunięcie odrzucają stare odpowiedzi; odłączona przeglądarka zgłasza `ai-unavailable`. Inne błędy: `ai-not-configured`, `ai-invalid-input`, `ai-daily-limit`, `ai-request-failed`, `ai-usage-storage-unavailable`, `ai-conversation-storage-unavailable`. Błędy nie nadpisują zachowanego wyniku; limit state to64KB.

Opcje bez poświadczeń: Windows `rule_ai_models` lub przeglądarka `settings-rule-ai-models`; `ruleAI` skonfiguruj przez istniejącą ścieżkę set-group. Pola: `dailyRequestLimit` (null lub1–100000), `prompt` i `context` (opcjonalne ciągi do16KB). Model i effort wybiera się przy każdym wywołaniu. Użyj zwróconej opcji; nie umieszczaj klucza ani URL dostawcy w regule.
