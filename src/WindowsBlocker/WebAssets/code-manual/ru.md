# Руководство по коду Windows Vault

[Руководство пользователя](../manual/ru.md)

## Контракт правила

Source: одно функциональное выражение `(on, v) => { ... }`. Поддерживаются только синхронный JavaScript и приведённый ниже API; timers, network, native system APIs и доступ к страницам browser не поддерживаются. Временные правила используют `ev.now` и events. Правила браузера используют руководство по коду расширения.

- Изменения сохраняются как черновик; **Run** активирует правило и включает группу. Замороженные группы не могут выполнить Run. Пустой source выгружает правило.
- Успешный Run заменяет handlers/panels и очищает app-block set этой группы, сохраняя `v.state`. Ошибка компиляции/регистрации сохраняет предыдущее правило; timeout может его остановить. Restart снова регистрирует последний активированный source; closure variables и app-block sets сбрасываются.
- Регистрация может инициализировать state, регистрировать handlers, показывать panels и записывать log. Действия с приложениями/файлами и emits должны быть в handlers; очередь регистрации отбрасывается.
- Disable приостанавливает handlers и удаляет panels/app blocks. Enable восстанавливает загруженное правило и сохранённые panels/blocks. Delete удаляет handlers/state/effects. Ранее закрытые приложения не открываются повторно; запись файлов не отменяется.
- Events не ограничены обычными целями группы; выбирайте приложения в правиле. Actions ставятся в очередь и применяются после dispatch. Исключение останавливает handler, не отменяя его state/actions; последующие handlers могут продолжить работу. Только file actions имеют result events.

## API

- `on(type, handler)` → boolean. Регистрирует `handler(ev)`; несколько handlers выполняются в порядке регистрации. False означает неверные аргументы или достижение лимита обработчиков. `ev = { type: string, now: number, data }`; `now` — Unix в миллисекундах.
- `v.state`: изменяемый JSON-объект, сохраняемый после event dispatch. Инициализируйте отсутствующие fields, не перезаписывая текущий state. Присваивание non-object или массива сбрасывает его в `{}`; несерилизуемые/слишком большие обновления не сохраняются.
- `v.log(...values)`: единственный источник Log группы. Logs/Clear независимы для каждой группы. Ошибки загрузки отображаются в Run status; диагностика handlers не попадает в Log.
- `v.emit(type, data)`: ставит JSON-копию `data` в очередь handlers группы после текущего event с новым `now`; это не синхронный вызов.
- `v.panel(id, spec)`: заменяет именованный плавающий panel группы; null `spec` удаляет его. См. Panels.
- `v.file(op, path, payload?)` → строка request ID. См. Files.
- `v.block(appId, on)`: true поддерживает блокировку приложения, false снимает блокировку группы. Блокировки включённых групп объединяются; вызов не может снять цель другой группы. Blocking запрашивает обычный выход и повторяет попытку с интервалом из Settings; не мешает запуску процесса и не гарантирует, что приложение примет Quit.
- `v.quit(appId)`: один обычный запрос на выход с той же защитой/повтором; без постоянной блокировки.
- `v.open(appId)`: просит Windows открыть установленное приложение; callback успеха нет.

Остальные вызовы возвращают `undefined`. App IDs — полные executable paths или application user model IDs, доступные в events и списке приложений. Block/Quit игнорируют системные процессы Windows, browsers, Vault и его помощники, а также пустые IDs. Panel IDs/state принадлежат группе, а не отображаемому имени.

## Events

Обозначения payload ниже описывают типы и не являются исполняемым кодом. `?` отмечает необязательные fields.

```text
tick (~1 second): { frontmost: App | null, running: App[] }
app: { kind: "launch" | "quit" | "focus" | "blur" | "hide" | "unhide",
       appId: string, name: string, previousAppId?: string | null }
snooze: {}
panel: { panelId: string, controlId: string, eventName: string,
         value: string, values: { [controlId: string]: string } }
file: see Files
App = { appId: string, name: string }
```

- `tick` приблизителен; используйте timestamps, а не подсчёт ticks. Running перечисляет распознанные процессы приложений Windows. Frontmost может быть null или иметь пустой app ID.
- `app` сообщает наблюдаемые изменения жизненного цикла перед event `tick` этого тика. Только focus содержит `previousAppId` (null, если неизвестен). Names — отображаемые, а не стабильные IDs.
- `snooze` означает, что нажата кнопка Snooze группы. Само по себе это не вызывает паузу.
- Ответы file направляются запросившей группе. Сопоставьте `requestId`, проверьте `ok` и установите deadline с ticks: ответы могут потеряться при перезагрузке/отключении правила. Request IDs могут повторяться после Run; ожидающие requests не являются долговременной работой.

## Panels

```text
spec = { title?: string, description?: string, controls?: Control[],
         position?: "top-left" | "top-right" | "bottom-left" | "bottom-right" | "center",
         width?: "small" | "medium" | "large" | number }
Control = { id?: string, type?: string, label?: string, value?, disabled?: boolean,
            ...type-specific fields below }
```

По умолчанию: внизу справа, ширина 300px; пресеты small/medium/large — 220/280/360px; числовая ширина ограничена 180–520px и принимает pixel strings. Native panels/sections располагают controls вертикально; browser layout, alignment, role, autofocus и control-dimension fields не влияют на native renderer.

IDs нормализуются до ASCII letters/digits/`_`/`-` (макс. 80); выбирайте уникальные стабильные IDs. Пропущенный control ID становится `control-N`; пропущенный/неизвестный type становится text. Пропущенные text/lists пусты; disabled равно false. Каждый вызов заменяет полную spec. Явный `value` имеет приоритет; если он пропущен, используется последний event value, затем нормализация type. Native events предоставляют strings: преобразуйте их в объявленный value type перед обновлением panel. Неизвестные fields отбрасываются; цвета/fonts/CSS panel принадлежат Vault.

Поля и начальные значения элементов управления:

- `text`: string `text`; по умолчанию label. `html`: string `html`, очищается и отображается как обычный текст в Windows.
- `button`: `label`, необязательный `action: "submit" | "cancel" | "close"`. Значение click — строка action или пустое. Actions сами ничего не отправляют/не закрывают.
- `checkbox`, `toggle`: boolean `value` (по умолчанию false); event value — `"true"`/`"false"`.
- `select`, `radio`: `options: (string | { value: string, label?: string })[]`; string value (по умолчанию пустой). Пустые option values удаляются; labels по умолчанию равны value. Обновляйте типизированное значение panel после выбора.
- `textInput`, `textarea`: string value (по умолчанию пустой); textInput `placeholder`; textarea `rows` 1–12 (по умолчанию 3). Native textarea игнорирует placeholder.
- `numberInput`, `range`: numeric value (по умолчанию 0), `min`, `max`, положительный `step`. При обновлении panel values ограничиваются; неуказанные границы нормализации — −1000000…1000000. Native numberInput — текстовый ввод: самостоятельно проверяйте `Number(event.value)`; min/max/step не ограничивают набор. Native range по умолчанию 0…100 с шагом 1.
- `date`, `time`: текстовый ввод; начальные форматы `YYYY-MM-DD`, `HH:MM`/`HH:MM:SS` (неверные форматы становятся пустыми). Самостоятельно проверяйте изменения. `color`: `#RRGGBB` (по умолчанию `#000000`).
- `pin`: строка цифр; `length` 3–12 (по умолчанию 6), `masked` по умолчанию true, `autoSubmit` false. `section`: `text`, `controls`; child sections на depth 3 не имеют children (root controls depth 0).

Panel events: обычные поля ввода отправляют `change`; buttons — только `click`; PIN отправляет `change` и `submit`, когда autoSubmit заполнен. Native mount/unmount/focus/key events отсутствуют. Values — strings, включая numbers/booleans. `values` содержит текущие значения ввода из отрисованного снимка и может отставать от инициировавшего изменения; `value` идентифицирует это изменение. Сохраняйте его в `v.state` и отображайте типизированные values для надёжных форм. Non-click events объединяются в течение 100ms на control; не считайте их нажатиями клавиш.

Ограничения текста: title/label 240; description/text 1000; HTML 20000; placeholder 500; input text 2000; другие value strings 512; option value/label 256. Превышение обрезается.

## Files

`op`: `"read"`, `"write"`, `"append"`, `"list"`, `"exists"`. Требуется **Папка пользовательских правил** в Settings и её разрешение.

- `path` относительный; `/` разделяет directories. Сегменты допускают ASCII letters/digits, пробелы и `_.,@()-`; без начальной точки, `.`/`..`, absolute path или URL. Расширения: `.txt`, `.csv`, `.json` (без учёта регистра). List path — каталог; `""` перечисляет выбранный root. Пути за пределы выбранной папки, в том числе через symlinks, отклоняются.
- Read возвращает текст UTF-8. Write заменяет/создаёт; append создаёт/добавляет без автоматического newline. При записи создаются родительские каталоги. String payload записывается буквально; остальные JSON payloads сериализуются; null/пропуск означает пустой текст. Разбор JSON/CSV — задача правила. Максимальный размер файла: 1048576 UTF-8 bytes.
- List возвращает непосредственно видимые подкаталоги и поддерживаемые файлы. Entries: `{ name: string, path: string, kind: "directory" | "file", extension?: string }`; для файлов extension включает точку. Exists возвращает boolean для поддерживаемого file path.

```text
file.data = { requestId: string, op: string, path: string, ok: boolean,
              text: string | null, entries: Entry[] | null,
              exists: boolean | null, error: string }
```

Неиспользуемые result fields равны null; при успехе error пустой. Ошибки включают invalid-path, unsupported-file-type, папка недоступна, файл отсутствует и file-too-large. Рассматривайте error как string, а не полный фиксированный enum. Transaction API нет; сериализуйте read-modify-write для каждого path.

## Ограничения

На event на group: 256 queued actions, 200 log calls, 64 emits; избыток отбрасывается. На rule: 1000 handlers, 24 panels; каждый control list имеет 32 entries, каждый choice — 64 options; избыток игнорируется/обрезается. Emit chains останавливаются после 16 поколений. Serialized state limit: 65536 JavaScript string characters. Удерживайте регистрацию и объединённые handlers каждого event менее 1 секунды; повторные превышения или hard timeout останавливают правило до Run. Log хранит 200 entries для каждой группы. Timing/replies работают best-effort, без гарантий реального времени.

## Полное правило

Steam заблокирован, кроме пятиминутной паузы, запущенной кнопкой Snooze или кнопкой её panel:

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
