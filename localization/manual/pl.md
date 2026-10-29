# Podręcznik użytkownika Windows Vault

Windows Vault ma trzy strony: **Vault** blokuje aplikacje natywne, **Classifier** taguje obsługiwane treści przeglądarkowe, a **Activity** pokazuje zarejestrowane użycie. Rozszerzenie przeglądarki zbiera obsługiwane treści i stosuje blokowanie w przeglądarce. Zainstaluj je i połącz w używanej przeglądarce.

## Szybki start

1. W **Vault** dodaj grupę blokowania i cel Apps, a potem wybierz aplikacje za pomocą selektora +.
2. Wybierz zachowanie blokowania grupy i ją włącz.
3. W **Classifier** utwórz grupę, wybierz platformy i dodaj tagi z opisami.
4. Wybierz lokalny poziom modelu i w razie potrzeby go pobierz. Włącz tagowanie w ustawieniach Classifier i wznów grupę.
5. Otwórz obsługiwane treści w połączonej przeglądarce. Skonfiguruj filtr tagów w przeglądarkowej grupie blokowania, jeśli tagi mają sterować blokowaniem.

## Grupy blokowania

**Grupa blokowania** stosuje zasady blokowania. **Grupa Classifier** przypisuje tagi do treści; sama niczego nie blokuje.

1. Dodaj grupę blokowania i nadaj jej nazwę.
2. Wybierz cele w sekcji **Dotyczy**.
3. Wybierz, kiedy blokowanie ma obowiązywać, a następnie ustaw harmonogram lub dozwolony czas.
4. Włącz grupę. Jej cele współdzielą zasady grupy.

Zwykłe zmiany są zapisywane automatycznie. Błąd oznacza, że zmiana nie została zaakceptowana; popraw pole i spróbuj ponownie. Wyłącz grupę, aby wstrzymać jej zasady, zachowując konfigurację. **Usuń grupę** usuwa ją. Przeciągaj grupy, aby zmienić ich kolejność. Do celu może pasować kilka grup; odroczenie jednej nie znosi blokady innej.

**Eksportuj** kopiuje konfigurację grupy. **Importuj** zastępuje konfigurację wybranej grupy po potwierdzeniu.

### Dozwolony czas i harmonogram

**Blokuj natychmiast** obowiązuje, gdy włączona grupa pasuje i jej harmonogram jest aktywny. **Blokuj po wykorzystaniu dozwolonego czasu** zezwala na pasujące użycie, dopóki limit czasu się nie wyczerpie.

Ustaw dozwolony czas w minutach, a interwał resetowania w godzinach. Limit kroczący zlicza użycie w poprzedzającym go oknie. Reset o północy rozpoczyna nowy okres o lokalnej północy, również dla limitu kroczącego.

Wybierz aktywne dni tygodnia i opcjonalne lokalne przedziały czasu, po jednym w wierszu, np. **09:00-12:00**. Pusta lista przedziałów oznacza cały wybrany dzień. Przedział musi kończyć się później niż zaczyna tego samego dnia; harmonogram nocny rozdziel na osobne dni.

### Odroczenie

Skonfiguruj odroczenie w każdej grupie blokowania. **Wstrzymaj blokowanie** zawiesza zasady grupy na określony czas. **Dodaj do dozwolonego czasu** dodaje dostępne minuty do grupy z limitem czasu. Jako czas odroczenia liczy się tylko wykorzystany dodatkowy limit. Niewykorzystany dodatkowy czas wygasa przy następnym resecie; dla limitu kroczącego po jednym oknie albo wcześniej o północy, jeśli ta opcja jest włączona.

**Opóźnienie aktywacji** odracza odroczenie, gdy blokowanie nadal działa. **Przerwa** to czas oczekiwania od zakończenia odroczenia do następnej prośby. **Wymagane potwierdzenia** ustawia liczbę etapów potwierdzania. Odroczenie jest dostępne dla zamrożonej grupy tylko wtedy, gdy zezwolono na nie przed zamrożeniem.

### Zamrażanie i PIN

**Zamroź** uniemożliwia zwykłe zmiany. Odmrożenie wymaga dziesięciu potwierdzeń w odstępach pięciu sekund oraz skonfigurowanego czasu oczekiwania i sześciocyfrowego PIN-u. **Czekaj przed odmrożeniem** przyjmuje 0–72 godziny; 0 nie dodaje oczekiwania.

Podczas zamrożenia można wydłużyć czas oczekiwania i dodać PIN, jeśli go nie ma. Tych warunków nie można złagodzić przed odmrożeniem grupy. Usunięcie również wymaga odczekania pozostałego czasu i podania PIN-u.

### Połączone grupy

Użyj **Połącz**, aby połączyć jawnie wybrane grupy w innych programach Vault. Połączone grupy współdzielą nazwę, obsługiwane ustawienia zasad, cele, użycie i warunki zamrożenia. Każdy program edytuje i egzekwuje obsługiwane przez siebie typy celów; pozostałe wpisy celów są dostępne dla połączonych programów. Rozłączenie zachowuje każdą grupę i jej ustawienia.

Jeśli połączony członek jest offline, edycja może być niedostępna. Otwórz Windows Vault i połączoną przeglądarkę, aby połączyć się ponownie. Zapisane lokalnie zasady mogą nadal obowiązywać, gdy członek jest offline.

## Uzyskiwanie pomocy

Kliknij małe **i** obok pola, aby zobaczyć wyjaśnienie. Kliknij poza nim lub naciśnij Escape, aby zamknąć. Listy znajdują się w przewijanych polach; przewiń pole, aby zobaczyć więcej pozycji. Wyszukiwanie filtruje widoczną listę bez usuwania pozycji.

Reguły niestandardowe mają osobny [Podręcznik kodu](../code-manual/pl.md). Wyjaśnia on edytor, aktywację, dzienniki, dostęp do plików i obsługiwane API.

## Aplikacje natywne

Użyj selektora + celu Apps, aby wybrać zainstalowane aplikacje. **Blokuj wszystkie aplikacje poza tymi** zmienia listę w allowlistę. Aplikacje systemowe, przeglądarki i sam Vault są wyłączone z natywnego blokowania aplikacji.

Zablokowana aplikacja otrzymuje prośbę o zamknięcie. **Ponownie proś zablokowaną aplikację o zamknięcie co (minuty)** steruje ponawianiem. Przekierowania witryn, pauzy stron i ukrywanie feedów są egzekwowane przez rozszerzenie przeglądarki; nie stają się akcjami aplikacji natywnej.

## Oznaczanie treści

Grupa taguje treści z przypisanych platform przy użyciu własnego drzewa tagów i ustawień modelu. Każda platforma należy do jednej grupy; wybierz je przy tworzeniu, później nie można ich zmienić. Harmonogramy i filtry blokowania nie sterują tagowaniem. Włącz tagowanie w Ustawienia; używaj **Wstrzymaj tagowanie / Wznów tagowanie** osobno dla każdej grupy. Wyłączenie nagrywania feedu w **Aktywność → Nagrywanie** także zatrzymuje jego tagowanie.

### Tagi i ustawienia modelu

Twórz tagi, opisuj ich znaczenie i ustawiaj lub usuwaj rodziców w drzewie. Przeciąganie przenosi gałąź. Jasne opisy pomagają modelowi rozróżniać podobne tagi. Ustawienia grup są niezależne. **Minimalna pewność tagu** określa wymagany poziom dowodów 1–5; nowe grupy zaczynają od 4 (silne wsparcie). Nie ma docelowej liczby tagów; element może nie otrzymać żadnego. **Instrukcje tagowania** zapisuje preferencje grupy. Zwykłe zmiany zapisują się automatycznie; każda grupa używa jawnie wybranego połączenia, modelu i reasoning effort.

### Połączenia AI

Ustawienia zawiera ChatGPT subscription, OpenAI API, Gemini API i Grok API. Subskrypcja korzysta z logowania Vault i kwalifikującego się planu. Klucze API podlegają rozliczeniom i limitom wybranego dostawcy. Wybierz dostawcę i wpisz klucz pod wspólną listą; zmiany zapisują się automatycznie, wyczyszczenie pola usuwa klucz. **Pobierz listę modeli** pobiera modele, ale nie wybiera żadnego; wybierz go w grupie tagowania lub custom-rule.

**Poziom rozumowania** określa poziom rozumowania modelu: Model default, Low, Medium lub High, jeśli obsługiwane. Nowe wybory zaczynają od Medium; istniejące pozostają zapisane. Wyższy poziom może zużyć więcej czasu i tokenów. Web search jest zawsze włączone dla tagowania; model decyduje, kiedy go użyć. Wybierz model obsługujący wyszukiwanie i ustrukturyzowane tagowanie. Nieudane lub niedostępne żądania zachowują prawidłowe wyniki cache; nowe elementy pozostają nierozstrzygnięte do **Ponów klasyfikację w chmurze**. Vault nie przełącza dostawcy automatycznie; każde połączenie ma własny odstęp i limit prób. W rozszerzeniu popraw tagi przez **+ tag**, wyszukanie i wybór; usuń przyciskiem usuwania lub wybierz tag i naciśnij Delete raz. **Bez tagu** oznacza zakończenie bez tagów; **Tagowanie** oznacza oczekujący wynik. Twoja korekta jest wiążąca dla elementu.


## Aktywność

Aktywność lokalnie zapisuje użycie włączonych aplikacji, odwiedziny witryn i obsługiwane **Wyświetlone treści**. Wykresy pokazują zapisane dane; pusty obszar nie dowodzi bezczynności PCa. Wybierz zakres dat. **Oś czasu** pokazuje godziny, **Sumy** sumuje czas, **Przedział czasu** łączy użycie z przedziałów w pionowe bloki. **Kolory** to klikalna legenda; wybierz źródło, by skupić wykresy. Wybierz dzień, aby zobaczyć użycie od niego.

### Grupy Aktywność

Utwórz grupę, by pokazać wybrane aplikacje i witryny razem w Usage. **Merge** stosuje jedną nazwę i kolor dla członków w całym Aktywność. Grupa porządkuje zapisane użycie i jest odrębna od grup blokowania i tagowania. Zapisz edytor jawnie przyciskiem **Save**.

### Nagrywanie i przechowywanie

W **Nagrywanie** włączaj lub wyłączaj nagrywanie kategorii albo źródeł. **Zachowaj** określa czas przechowywania historii; **Zawsze** nie wygasa automatycznie. Ustawienia pojedyncze mogą dziedziczyć szersze. Wyłączenie nagrywania zatrzymuje nowe dane; usunięcie historii usuwa zapisane wpisy. Feedy platform zbierają treści wyświetlone na obsługiwanych stronach, otwartych lub nie. Feed **Tagowanie obsługiwane** może zasilać Tagowanie treści, gdy nagrywanie jest włączone. Jego retencja jest niezależna od użycia aplikacji i stron. Wstrzymanie grupy tagowania samo nie wyłącza nagrywania.

## Ustawienia

W Ustawienia wybierz język interfejsu. Objaśnienia pól są dostępne przez przyciski Info w tym języku.

## Zapisywanie i rozwiązywanie problemów

Zwykłe zmiany Vault i Classifier zapisują się automatycznie. Edycja grupy Activity wymaga użycia **Zapisz**. Dodawanie, usuwanie, pobieranie modelu, testowanie połączenia i pobieranie listy modeli są jawnymi czynnościami.

Jeśli brakuje tagów, sprawdź połączenie przeglądarki, globalny przełącznik tagowania, stan wstrzymania grupy, rejestrowanie platformy i pobranie modelu. Jeśli badania nie działają, sprawdź zgodę, wybór grupy, dane dostawcy, model badawczy i status badań. Jeśli nie można edytować połączonej grupy, połącz ponownie programy lub odmroź grupę zgodnie z komunikatem.
