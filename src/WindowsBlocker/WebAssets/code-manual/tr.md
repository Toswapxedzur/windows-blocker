# Windows Vault kod kılavuzu

[Kullanıcı kılavuzu](../manual/tr.md)

## Kural sözleşmesi

Source: tek bir function expression `(on, v) => { ... }`. Yalnızca eşzamanlı JavaScript ve aşağıdaki API desteklenir; timers, network, native system APIs veya browser sayfasına erişim yoktur. Zamana dayalı kurallar `ev.now` ve events kullanır. Tarayıcı kuralları tarayıcı uzantısının kod kılavuzunu kullanır.

- Düzenleme taslak kaydeder; **Run** kuralı etkinleştirip grubu açar. Dondurulmuş gruplar Run çalıştıramaz. Boş source kuralı kaldırır.
- Başarılı Run handlers/panels öğelerini değiştirir ve `v.state` değerini koruyarak bu grubun app-block set değerini temizler. Derleme/kayıt başarısızsa önceki kural korunur; timeout kuralı durdurabilir. Restart son etkinleştirilen source değerini yeniden kaydeder; closure variables ve app-block sets sıfırlanır.
- Kayıt state başlatabilir, handlers kaydedebilir, panels gösterebilir ve log yazabilir. Uygulama/dosya işlemleri ve emits handlers içinde olmalıdır; kayıt sırasındaki kuyruk atılır.
- Disable handlers değerlerini bastırır, panels/app blocks değerlerini kaldırır. Enable yüklü rule ile saklanan panels/blocks değerlerini geri yükler. Delete handlers/state/effects değerlerini kaldırır. Önceden kapatılan uygulamalar yeniden açılmaz; file writes geri alınmaz.
- Events normal grup targets değerleriyle sınırlı değildir; uygulamaları rule içinde seçin. Actions kuyruğa alınır ve dispatch sonrasında uygulanır. Exceptions, state/actions değerlerini geri almadan ilgili handler'ı durdurur; sonraki handlers çalışabilir. Yalnızca file actions result events içerir.

## Yönetilen zamanlayıcılar ve kayan kullanım

`v.timer(id, {direction:"down", durationMs:300000})` başlangıçta duraklatılmış, adlandırılmış bir geri sayım oluşturur veya mevcut olanı döndürür. İleri sayan bir zamanlayıcı için `{direction:"up"}` kullanın. Döndürülen nesne `start()`, `pause()`, `resume()`, `reset()` ve `snapshot()` yöntemlerini sunar. Start/resume idempotenttir; reset, geçen süreyi sıfırlayıp duraklatılmış duruma döndürür. Anlık görüntü alanları `ready`, `error`, `elapsedMs`, `remainingMs` (ileri sayımda null), `running` ve `completed` içerir.

Çalışan zamanlayıcılar, uygulama/tarayıcı kapalıyken veya kural devre dışıyken gerçek zamanla ilerler. Tamamlanan geri sayım, `timerEnded` olayını `{id, direction, durationMs, generation, incarnation}` verisiyle, her zamanlayıcı örneklenmesi ve nesli için her üyeye kuralı bir sonraki kez çalıştığında bir kez iletir; Run bunu yeniden oynatmaz. İletim, ana sistemin olaylarına bağlıdır; bu gerçek zamanlı bir alarm değildir. Mevcut metin denetimi üzerinden göstermek için bir panele `{type:"timer", timerId:"focus", label:"Focus"}` ekleyin.

`v.rolling(id, {windowMs:3600000, limitMs:600000})` adlandırılmış bir aktif kullanım izleyicisi oluşturur veya mevcut olanı döndürür. `assign({websites:["youtube.com","reddit.com"], apps:[]})` bu üyenin hedef atamalarını değiştirir. Yerel kurallar bunun yerine platforma ait uygulama kimliklerini atayabilir. Alan adları alt alan adlarını da kapsar. Bir izleyici hedeflerinin kullanımını ortak havuzda toplar; diğer kimliklerin bağımsız hakları vardır. `snapshot()`, `ready`, `error`, `usedMs`, `remainingMs`, `available` ve `nextReturnAtMs` (kaydedilmiş herhangi bir kullanımın geçerliliğini yitireceği en yakın milisaniye) döndürür. `available(target?)`, bir hedef verildiğinde atamayı da denetler. `reset()` bu nesnenin geçmişini temizler.

Hak sınırını aşan kullanım dahil, gözlemlenen uygun kullanımın tamamı sayılır; izleyici kendi başına sayfa/uygulama engellemez. Görünür sayfaların çakışan aralıkları bir kez sayılır; yerel uygulamalarda gözlemlenen ön plan kullanımı sayılır. Devre dışı kurallar kullanım eklemez; geçmişin süresi yine aralıkların tam sınırlarında dolar. Kullanılabilirlik, yalnızca kalan geçmiş limitin altına düştüğünde geri gelir. Düzenli etkinlik sinyali örnekleri beş saniyeyle sınırlıdır; kapatma/devre dışı kalma boşlukları etkinlik üretmez. Kullanıcının yaydığı olaylar kullanım eklemez.

Bağlı özel gruplardaki aynı nesne kimlikleri, tek bir yerel merkez yetkili durum kaynağını paylaşır. Bağlama, izleyici geçmişini ve atamalarını birleştirir ve bağlantıyı başlatan üyenin zamanlayıcı durumunu korur; uyumsuz yapılandırmalarda bağlama reddedilir. Her üye kendi tamamlanma olayını alır. Bağlantının kopması, güncel yetkili kaynak geri gelene kadar paylaşılan izleyicileri kullanılamaz kılar ve zamanlayıcı denetimlerini reddeder. Bağ kaldırıldığında her üye son paylaşılan kullanımı ve zamanlayıcı durumunu korur, kendi hedeflerini geri yükler ve eski bağdan gelen komutları bırakır. Merkez, geri dönen üye son anlık görüntüyü kaydedip onaylayana kadar onu saklar; yeni bir bağlantı kullanılabilir olmadan önce bekleyen devirler tamamlanır.

Adlandırılmış nesneler, ayrılmış `v.state.$vaultTime` alanında Run ve yeniden başlatma sonrasında korunur; bu alanı doğrudan düzenlemeyin. Açıkça kaldırmak için `v.removeTime(id)` kullanın. Kimlikler 1–80 ASCII harfi, rakam, `_` veya `-` içerir; ayrılmış özellik adları reddedilir. En fazla 32 nesne, kuralın 64 KiB UTF-8 durum limitini paylaşır. Süreler, en fazla 365 gün olmak üzere tam sayı milisaniyelerle belirtilir. Kayan geçmiş 1024 dakikalık kova ve 2048 birleştirilmiş aralıkla sınırlıdır. Geçmiş kapasitesi hataları, açıkça sıfırlanana kadar izleyiciyi kullanılamaz durumda bırakır; geçersiz yapılandırmalar ve aşırı büyük kayıtlar, kayıtlı verileri silmeden hata bildirir.

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

- `on(type, handler)` → boolean. `handler(ev)` kaydeder; handlers kayıt sırasıyla çalışır. False, geçersiz arguments veya handler limitine ulaşıldığı anlamına gelir. `ev = { type: string, now: number, origin: "host" | "user" | "rule", data }`; `now` Unix milisaniyesidir. Gerçekten görüntülenen denetim olaylarında `origin: "user"`, `v.emit` olaylarında `origin: "rule"` bulunur.- `v.state`: event dispatch sonrasında kaydedilen değiştirilebilir JSON object. Mevcut state'i ezmek yerine eksik fields değerlerini başlatın. Non-object veya array atanması `{}` değerine sıfırlar; serileştirilemeyen/aşırı büyük güncellemeler kaydedilmez.
- `v.log(...values)`: bu grubun Log kaydını oluşturan tek kaynak. Logs/Clear her grup için ayrıdır. Yükleme hataları Run durumunda görünür; handler diagnostics Log'u doldurmaz.
- `v.emit(type, data)`: geçerli event sonrasında bu grubun handlers öğelerine `data` öğesinin JSON kopyasını yeni `now` ile kuyruğa alır; eşzamanlı çağrı değildir.
- `v.panel(id, spec)`: grubun adlandırılmış floating panel öğesini değiştirir; null `spec` paneli kaldırır. Panels'e bakın.
- `v.file(op, path, payload?)` → request ID string. Files'a bakın.
- `v.store(request)` istek kimliği döndürür; grup kayıtlarına bakın.
- `v.block(appId, on)`: true uygulama engelini sürdürür, false grubun engelini kaldırır. Etkin grupların engelleri birleşir; bu çağrı başka grubun hedefini engelden çıkaramaz. Blocking normal çıkış ister ve Settings aralığında yeniden dener; process launch değerini engellemez ve uygulamanın Quit'ı kabul edeceğini garanti etmez.
- `v.quit(appId)`: aynı koruma/yeniden deneme ilkesiyle tek normal kapatma isteği; sürekli engelleme değildir.
- `v.open(appId)`: Windows'tan yüklü bir uygulamayı açmasını ister; başarı callback'i yoktur.

Diğer çağrılar `undefined` `com.valvesoftware.steam` döndürür. App IDs tam executable paths veya application user model IDs olup events ve app picker içinde bulunur. Block/Quit Windows system processes, browsers, Vault ve yardımcılarını ve boş IDs değerlerini yok sayar. Panel IDs/state grubuna aittir, görünen ada değil.

## Events

Aşağıdaki payload gösterimi yürütülebilir kod değil, types bilgisi verir. `?` isteğe bağlı fields alanlarını belirtir.

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

- `tick` yaklaşık değerdir; tick sayısı yerine timestamps kullanın. Running, tanımlanmış Windows uygulama süreçlerini listeler. Frontmost null veya boş app ID olabilir.
- `app`, o tick'in `tick` event değerinden önce gözlemlenen yaşam döngüsü değişikliklerini bildirir. Yalnızca focus `previousAppId` içerir (bilinmiyorsa null). Names görünen adlardır, kararlı IDs değildir.
- `snooze`, grubun Snooze düğmesine basıldığı anlamına gelir. Tek başına duraklama uygulamaz.
- File replies isteği yapan gruba gider. `requestId` ile eşleştirin, `ok` değerini denetleyin ve ticks ile deadline belirleyin: rule yeniden yüklenirse/devre dışı bırakılırsa replies kaybolabilir. Request IDs Run sonrasında tekrarlanabilir; bekleyen requests kalıcı iş değildir.

## Panels

```text
spec = { title?: string, description?: string, controls?: Control[],
         position?: "top-left" | "top-right" | "bottom-left" | "bottom-right" | "center",
         width?: "small" | "medium" | "large" | number }
Control = { id?: string, type?: string, label?: string, value?, disabled?: boolean,
            ...type-specific fields below }
```

Varsayılan: sağ alt konum, genişlik 300px; small/medium/large presetleri 220/280/360px; sayısal genişlik 180–520px aralığıyla sınırlıdır ve pixel strings kabul eder. Native panels/sections controls öğelerini dikey dizer; browser layout, alignment, role, autofocus ve control-dimension fields native renderer değerini etkilemez.

IDs ASCII letters/digits/`_`/`-` biçimine normalize edilir (en fazla 80); benzersiz ve kararlı IDs seçin. Atlanan control ID `control-N`, atlanan/bilinmeyen type text olur. Atlanan text/lists boş, disabled false olur. Her çağrı tüm spec değerini değiştirir. Açık `value` önceliklidir; atlanırsa son event value kullanılır, ardından type normalization uygulanır. Native events strings sağlar: güncellenmiş paneli oluşturmadan önce belirtilen value type değerine dönüştürün. Bilinmeyen fields atılır; panel colors/fonts/CSS Vault'a aittir.

Control alanları ve başlangıç değerleri:

- `text`: string `text`; varsayılan label. `html`: string `html`, sanitize edilir ve Windows'ta düz metin olarak görüntülenir.
- `button`: `label`, isteğe bağlı `action: "submit" | "cancel" | "close"`. Click value, action string veya boş olur. Actions otomatik gönderme/kapatma yapmaz.
- `checkbox`, `toggle`: boolean `value` (varsayılan false); event value `"true"`/`"false"` olur.
- `select`, `radio`: `options: (string | { value: string, label?: string })[]`; string value (varsayılan boş). Boş option values kaldırılır; labels varsayılan olarak value olur. Seçim sonrasında panelin typed value değerini güncelleyin.
- `textInput`, `textarea`: string value (varsayılan boş); textInput `placeholder`; textarea `rows` 1–12 (varsayılan 3). Native textarea placeholder değerini yok sayar.
- `numberInput`, `range`: numeric value (varsayılan 0), `min`, `max`, pozitif `step`. Panel updates sırasında values sınırlar içinde tutulur; belirtilmeyen normalization sınırları −1000000…1000000. Native numberInput metin girişidir: `Number(event.value)` değerini kendiniz doğrulayın; min/max/step yazmayı sınırlamaz. Native range varsayılan 0…100, step 1 olur.
- `date`, `time`: metin girişi; başlangıç formatları `YYYY-MM-DD`, `HH:MM`/`HH:MM:SS` (geçersiz biçimler boş olur). Düzenlemeleri kendiniz doğrulayın. `color`: `#RRGGBB` (varsayılan `#000000`).
- `pin`: rakam string'i; `length` 3–12 (varsayılan 6), `masked` varsayılan true, `autoSubmit` false. `section`: `text`, `controls`; depth 3'te child sections children içermez (root controls depth 0).

Panel events: sıradan girdiler `change`; buttons yalnızca `click`; PIN autoSubmit tamamlandığında `change` ve `submit` gönderir. Native mount/unmount/focus/key events yoktur. Values, numbers/booleans dâhil strings değerleridir. `values`, oluşturulan snapshot'ın giriş değerlerini içerir ve tetikleyen düzenlemenin gerisinde kalabilir; `value` bu düzenlemeyi belirtir. Güvenilir forms için `v.state` içine kaydedip typed values oluşturun. Click dışı events, control başına 100ms içinde birleştirilir; events değerlerini tuş vuruşu saymayın.

Metin sınırları: title/label 240; description/text 1000; HTML 20000; placeholder 500; input text 2000; diğer value strings 512; option value/label 256. Fazlası kısaltılır.

## Files

`op`: `"read"`, `"write"`, `"append"`, `"list"`, `"exists"`. Settings içinde **Özel kural klasörü** ve izin gerekir.

- `path` görecelidir; `/` directories öğelerini ayırır. Segmentler ASCII letters/digits, boşluk ve `_.,@()-` kabul eder; başında nokta, `.`/`..`, absolute path veya URL kabul etmez. Dosya uzantısı: `.txt`, `.csv`, `.json` (büyük/küçük harf duyarsız). List path directory'dir; `""` seçilen root'u listeler. Symlinks üzerinden olanlar dâhil seçilen klasörün dışına çıkan paths reddedilir.
- Read UTF-8 text döndürür. Write değiştirir/oluşturur; append otomatik newline olmadan oluşturur/ekler. Yazarken üst dizinler oluşturulur. String payload aynen yazılır; diğer JSON payloads serileştirilir; null/atlanmış değer boş text demektir. JSON/CSV parsing rule'a aittir. En büyük dosya boyutu: 1048576 UTF-8 bytes.
- List doğrudan görünen alt dizinleri ve desteklenen dosyaları döndürür. Entries: `{ name: string, path: string, kind: "directory" | "file", extension?: string }`; files içindeki extension noktayı içerir. Exists, desteklenen file path için boolean döndürür.

```text
file.data = { requestId: string, op: string, path: string, ok: boolean,
              text: string | null, entries: Entry[] | null,
              exists: boolean | null, error: string }
```

Kullanılmayan result fields null; başarıda error boş olur. Hatalara invalid-path, unsupported-file-type, klasör kullanılamıyor, dosya eksik ve file-too-large dahildir. error değerini sabit ve eksiksiz enum değil, string olarak ele alın. Transaction API yoktur; her path için read-modify-write işlemlerini serileştirin.

## Grup kayıtları

`v.store(request)` bir handler içinde asenkron JSON kayıt işlemi başlatır. Kayıtlar gruba özeldir, Run ve yeniden başlatma sonrasında kalır ve grupla silinir. Bağlı grupların store'ları ayrıdır. Key, value, version, cache invalidation ve feedback önceliğini kural belirler. Mac ve tarayıcı kuralları aynı API'yi kullanır ancak her app/tarayıcı store'u ayrıdır.

| Request | Result |
|---|---|
| `{op:"get", key}` | `record` veya `null` |
| `{op:"put", key, value, version?, ifRevision?}` | kaydedilen `record` |
| `{op:"delete", key, ifRevision?}` | `record: null` |
| `{op:"list", prefix?, after?, limit?}` | sıralı `entries` ve `next` |
| `{op:"clear", prefix?}` | `deleted` sayısı |

Her kayıt `{value, revision, version, updatedAt}` biçimindedir. Opaque revision gerçek yazmada değişir; `version` çağıranın metadata'sıdır. `list` değerler olmadan başlıkları döndürür; okumak için `get`, sonraki sayfa için `after: next` kullanın. `put`/`delete`, `ifRevision` yoksa put/delete koşulsuzdur; `null` anahtarın bulunmamasını, revision metni tam eşleşmeyi şart koşar. Uyuşmazlık `ok:false`, `error:"store-conflict"` ve güncel `record` döndürür; geciken otomatik yanıtın kullanıcı geri bildirimini ezmesini önler. `v.context.revision` kuralın sürümüne katabileceği opaque yapılandırma işaretidir; framework cache politikası dayatmaz.

Sınırlar: key 1–2048 karakter, version ≤128, JSON value ≤64KiB, depth ≤32, grup başına10000 key, tüm belge16MiB. `list` sayfa başına en çok100 başlık ve96KiB döndürür. Grup başına32 request, toplam saniyede128 başlangıç. Aşırı yükte grup başına saniyede bir `store-busy` hatası bildirilir ve fazlası atılır. Storage hatası `ok:false` döndürür; gelecekteki/bozuk belge korunup `store-unavailable` bildirilir. Otomatik retry/eviction yoktur. Deadline için ticks kullanın.

## Sınırlar

Her event/grup için: 256 queued actions, 200 log calls, 64 emits; fazlası atılır. Her rule için: 1000 handlers, 24 panels; her control list 32 entries ve her choice 64 options içerir; fazlası yok sayılır/kısaltılır. Emit chains 16 generation sonrasında durur. Serialized state limit: 65536 JavaScript string characters. Kayıt işlemi ve her event'in birleşik handlers süresini 1 saniyenin altında tutun; tekrarlanan aşımlar veya hard timeout rule'u Run'a kadar durdurur. Log grup başına 200 entries tutar. Timing/replies best-effort çalışır; gerçek zamanlılık garantisi değildir.

## Tam kural

Steam, Snooze veya panel düğmesiyle başlatılan beş dakikalık duraklama dışında engellenir:

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


## Çalışan kuraldan AI çağrıları

Her custom group **AI calls** içinde bir connection/model ve reasoning effort seçer. Mac ve tarayıcı kuralları aynı API'yi kullanır; tarayıcı istekleri bağlı Windows Vault gerektirir ve provider kimlik bilgileri orada kalır. Bağlı gruplar seçimi ve isteğe bağlı günlük sınırı paylaşır. Boş değer grup sınırı olmadığı anlamına gelir; hesap limitleri geçerlidir. Sınır UTC gününe göre hesaplanır, restart'ta korunur ve başarısızlar dahil gönderilen denemeleri sayar. Link mevcut kullanımı birleştirir; unlink önceki kullanımı saklar.

`v.ai({ prompt, input? })` hemen request ID döndürür. Kuralı kaydederken değil, handler içinde çağırın. `input` herhangi bir JSON olabilir; prompt/input ayrı ayrı20KB ile sınırlıdır. Yanıtlar `ai` event'leriyle gelir: `{requestId, ok, text, citations,
error}`. Success text is at most48KB; citations are `{url,title}`. Web arama yanıtlarında kaynak bağlantılarını görünür ve tıklanabilir tutun. Search modele daima açıktır. Sabit app deadline yoktur, concurrency sınırlıdır; provider fallback veya otomatik retry yoktur. Yanıtlar veridir, JavaScript olarak çalıştırılmaz; mevcut eylemleri kural seçer.

`ask` düğmesi ve `note` alanı olan bir panel için:

```javascript
(on, v) => {
  let pending = null;
  on("panel", ({ data }) => {
    if (data.controlId !== "ask" || pending) return;
    pending = v.ai({ prompt: "Summarize this text.", input: data.values?.note ?? "" });
  });
  on("ai", ({ data }) => {
    if (data.requestId !== pending) return;
    pending = null;
    if (!data.ok) { v.log("AI request failed:", data.error); return; }
    v.log(data.text, data.citations);
  });
}
```

Bekleyen çağrılar restart sonrasında yeniden oynatılmaz. Run, devre dışı bırakıp yeniden etkinleştirme ve silme eski yanıtları atar; bağlantısız tarayıcı `ai-unavailable` bildirir. Diğer hatalar: `ai-not-configured`, `ai-invalid-input`, `ai-busy`, `ai-daily-limit`, `ai-request-failed`, `ai-usage-storage-unavailable`. Başarısız çağrı kuralın koruduğu önceki sonucu ezmez. Kural state sınırı64KB olarak kalır.

Kimlik bilgisi gerektirmeyen seçimleri Mac'te `rule_ai_models`, tarayıcıda `settings-rule-ai-models` ile keşfedin; mevcut set-group yolu üzerinden `ruleAI` yapılandırın. Alanlar: `providerModel`, `accountID`, `reasoningEffort` (`default`, `low`, `medium`, `high`) ve `dailyRequestLimit` (null veya1–100000). Dönen seçimi kullanın; kural kaynağına key ya da provider URL koymayın.
