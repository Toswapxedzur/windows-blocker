# คู่มือโค้ด Adamancia Vault สำหรับ Windows

[คู่มือผู้ใช้](../manual/th.md)

## ข้อกำหนดของกฎ

แหล่งที่มา: function expression หนึ่งรายการ `(on, v) => { ... }` รองรับเฉพาะ synchronous JavaScript และ API ด้านล่าง ไม่มี timers, network, native system APIs หรือ browser-page access กฎที่อิงเวลาใช้ `ev.now` และ events กฎของเบราว์เซอร์ใช้ code manual ของ browser extension

- การแก้ไขจะบันทึกเป็น draft; **Run** จะเปิดใช้และเปิดใช้กลุ่ม กลุ่มที่ตรึงไว้กด Run ไม่ได้ source ว่างจะ unload กฎ
- Run ที่สำเร็จจะแทนที่ handlers/panels และล้าง app-block set ของกลุ่มนี้ โดยเก็บ `v.state` ไว้ หาก compilation/registration ล้มเหลวจะคงกฎก่อนหน้าไว้ แต่ timeout อาจหยุดได้ Restart จะ register source ที่เปิดใช้ล่าสุดอีกครั้ง; closure variables และ app-block sets จะ reset
- ระหว่าง registration อาจเริ่มต้น state, register handlers, แสดง panels และเขียน log ได้ การกระทำกับแอป/ไฟล์และ emits ต้องอยู่ใน handlers; สิ่งที่ queue ไว้ตอน registration จะถูกทิ้ง
- Disable จะระงับ handlers และนำ panels/app blocks ออก Enable จะคืน rule ที่โหลดและ panels/blocks ที่เก็บไว้ Delete จะนำ handlers/state/effects ออก แอปที่ quit ไปแล้วจะไม่เปิดใหม่; file writes จะไม่ถูกย้อนกลับ
- Events ไม่จำกัดตาม targets ทั่วไปของกลุ่ม ให้เลือกแอปในกฎ Actions จะถูก queue แล้วนำไปใช้หลัง dispatch หากเกิด exception handler นั้นหยุดโดยไม่ย้อน state/actions; handlers ถัดไปอาจยังทำงาน มีเพียง file actions ที่มี result events

## ตัวจับเวลาที่จัดการโดยระบบและการใช้งานในช่วงเวลาเลื่อน

`v.timer(id, {direction:"down", durationMs:300000})` สร้างหรือคืน countdown แบบมีชื่อ ซึ่งเริ่มต้นในสถานะ paused ใช้ `{direction:"up"}` สำหรับ timer แบบนับขึ้น Handle มี `start()`, `pause()`, `resume()`, `reset()` และ `snapshot()` การเรียก start/resume ซ้ำไม่มีผลเพิ่มเติม ส่วน reset จะกลับสู่สถานะ paused โดยมีเวลาที่ผ่านไปเป็นศูนย์ Fields ใน snapshot มี `ready`, `error`, `elapsedMs`, `remainingMs` (เป็น null สำหรับการนับขึ้น), `running` และ `completed`

Timer ที่กำลังทำงานจะเดินตามเวลานาฬิกาจริงขณะ app/browser ปิดอยู่หรือ rule ถูกปิดใช้งาน Countdown ที่ครบเวลาจะส่ง `timerEnded` พร้อม `{id, direction, durationMs, generation, incarnation}` หนึ่งครั้งต่อ incarnation และ generation ของ timer ให้สมาชิกแต่ละรายเมื่อ rule ของรายนั้นทำงานครั้งถัดไป Run จะไม่ส่งซ้ำ การส่งขึ้นอยู่กับ host events จึงไม่ใช่นาฬิกาปลุกแบบ real-time ใส่ `{type:"timer", timerId:"focus", label:"Focus"}` ใน panel เพื่อแสดงผ่าน text control ที่มีอยู่

`v.rolling(id, {windowMs:3600000, limitMs:600000})` สร้างหรือคืน tracker การใช้งานจริงแบบมีชื่อ `assign({websites:["youtube.com","reddit.com"], apps:[]})` แทนที่การกำหนด targets ของสมาชิกรายนี้ Native rules สามารถกำหนด app IDs ของแพลตฟอร์มแทนได้ Domains รวม subdomains ด้วย Tracker หนึ่งตัวรวมการใช้งานของ targets ทั้งหมด ส่วน IDs อื่นมีโควตาแยกกัน `snapshot()` คืน `ready`, `error`, `usedMs`, `remainingMs`, `available` และ `nextReturnAtMs` (millisecond ถัดไปที่การใช้งานที่บันทึกไว้รายการใดรายการหนึ่งหมดอายุ) `available(target?)` ตรวจสอบการกำหนด target ด้วยเมื่อระบุ target ส่วน `reset()` ล้าง history ของออบเจ็กต์นี้

การใช้งานที่เข้าเกณฑ์และตรวจพบทั้งหมดจะถูกนับ รวมถึงส่วนที่เกินโควตา แต่ tracker ไม่ได้บล็อก page/app เอง ช่วงเวลาของหน้าที่มองเห็นซึ่งซ้อนทับกันจะนับเพียงครั้งเดียว และ native apps จะนับการใช้งาน foreground ที่ตรวจพบ Rules ที่ปิดใช้งานจะไม่เพิ่มการใช้งาน แต่ history ยังคงหมดอายุตามขอบเขต interval ที่แน่นอน Availability จะกลับมาเมื่อการใช้งานใน history ที่เหลือต่ำกว่า limit เท่านั้น Heartbeat samples จำกัดไว้ไม่เกินห้าวินาที ช่วงที่ปิดโปรแกรมหรือปิดใช้งานจะไม่สร้าง activity สมมติ Events ที่ผู้ใช้ emit เองจะไม่เพิ่มการใช้งาน

Object IDs เดียวกันใน custom groups ที่เชื่อมโยงกันใช้ native hub ภายในเครื่องเป็นแหล่งข้อมูลหลักร่วมกัน การเชื่อมโยงจะรวม history และการกำหนด targets ของ tracker และเก็บ timer state ของฝ่ายที่เริ่มเชื่อมโยง หาก configurations ไม่เข้ากันจะปฏิเสธการเชื่อมโยง สมาชิกแต่ละรายได้รับ completion event ของตนเอง เมื่อขาดการเชื่อมต่อ shared trackers จะ unavailable และปฏิเสธ timer controls จนกว่าจะได้รับข้อมูลล่าสุดจากแหล่งข้อมูลหลัก เมื่อ unlink สมาชิกแต่ละรายเก็บ shared usage และ timer state สุดท้าย คืน targets ของตนเอง และทิ้ง commands จาก link เดิม Hub เก็บ snapshot สุดท้ายจนกว่าสมาชิกที่กลับมาจะบันทึกและยืนยันการรับ โดยต้องดำเนินการ handover ที่ค้างอยู่ให้เสร็จก่อนจึงจะเชื่อมโยงใหม่ได้

ออบเจ็กต์แบบมีชื่อคงอยู่หลัง Run และ restart ใน field ที่สงวนไว้ `v.state.$vaultTime` อย่าแก้ไข field นี้โดยตรง ลบอย่างชัดเจนด้วย `v.removeTime(id)` IDs มีความยาว 1–80 ตัว ประกอบด้วยตัวอักษร ASCII ตัวเลข `_` หรือ `-` และไม่ยอมรับชื่อ properties ที่สงวนไว้ ออบเจ็กต์ได้สูงสุด 32 ตัวใช้ state limit ขนาด 64KiB UTF-8 ของ rule ร่วมกัน Durations เป็นจำนวนเต็ม millisecond สูงสุด 365 วัน Rolling history จำกัดไว้ที่ 1024 minute buckets และ 2048 merged intervals ข้อผิดพลาดความจุ history ทำให้ tracker unavailable จนกว่าจะ reset อย่างชัดเจน Configurations ที่ไม่ถูกต้องและการบันทึกที่ใหญ่เกินไปจะรายงานข้อผิดพลาดโดยไม่ลบข้อมูลที่บันทึกไว้

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

- `on(type, handler)` → boolean ลงทะเบียน `handler(ev)`; หลาย handlers ทำงานตามลำดับที่ลงทะเบียน False หมายถึง arguments ไม่ถูกต้องหรือถึงขีดจำกัด handlers `ev = { type: string, now: number, origin: "host" | "user" | "rule", data }`; `now` คือ Unix milliseconds เหตุการณ์ของตัวควบคุมที่แสดงจริงมี `origin: "user"`; เหตุการณ์ `v.emit` มี `origin: "rule"`.- `v.state`: JSON object ที่แก้ไขได้และบันทึกหลัง event dispatch ให้เริ่มเฉพาะ fields ที่หายไปแทนการเขียนทับ state เดิม การกำหนด non-object หรือ array จะ reset เป็น `{}`; การอัปเดตที่ serialize ไม่ได้หรือใหญ่เกินไปจะไม่ถูกบันทึก
- `v.log(...values)`: วิธีเดียวที่สร้าง Log ของกลุ่มนี้ Logs/Clear แยกกันแต่ละกลุ่ม Load errors ปรากฏใน Run status; handler diagnostics ไม่ลงใน Log
- `v.emit(type, data)`: queue สำเนา JSON ของ `data` ให้ handlers กลุ่มนี้หลัง event ปัจจุบัน พร้อม `now` ใหม่; ไม่ใช่ synchronous call
- `v.panel(id, spec)`: แทนที่ floating panel ที่ตั้งชื่อของกลุ่มนี้; หาก `spec` เป็น null จะนำออก ดู Panels
- `v.file(op, path, payload?)` → request ID string ดู Files
- `v.store(request)` ส่งคืน ID คำขอ ดูบันทึกของกลุ่ม
- `v.block(appId, on)`: true คงการบล็อกแอปไว้; false นำ block ของกลุ่มนี้ออก Blocks จากกลุ่มที่เปิดใช้อยู่จะรวมกัน; call นี้ปลด block เป้าหมายของกลุ่มอื่นไม่ได้ การบล็อกจะขอให้ปิดตามปกติและลองใหม่ตาม interval ใน Settings; ไม่ขัดขวางการเปิด process และไม่รับประกันว่าแอปจะยอม Quit
- `v.quit(appId)`: ขอปิดตามปกติหนึ่งครั้ง ภายใต้นโยบาย protection/retry เดียวกัน; ไม่ใช่ block ต่อเนื่อง
- `v.open(appId)`: ขอให้ Windows เปิดแอปที่ติดตั้งไว้; ไม่มี success callback

Calls อื่นคืน `undefined` `com.valvesoftware.steam` App IDs คือ executable paths แบบเต็มหรือ application user model IDs ที่มีให้ใน events และ app picker Block/Quit จะละเว้น Windows system processes, browsers, Vault และ helpers ของ Vault รวมถึง IDs ว่าง Panel IDs/state เป็นของกลุ่ม ไม่ใช่ชื่อที่แสดง

## Events

สัญกรณ์ payload ด้านล่างระบุชนิดข้อมูล ไม่ใช่โค้ดที่รันได้ `?` หมายถึง field ที่ไม่บังคับ

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

- `tick` เป็นค่าโดยประมาณ ใช้ timestamps แทนการนับ ticks Running แสดง Windows application processes ที่ระบุได้ Frontmost อาจเป็น null หรือมี app ID ว่าง
- `app` รายงานการเปลี่ยน lifecycle ที่สังเกตเห็นก่อน `tick` event ของ tick นั้น เฉพาะ focus เท่านั้นที่มี `previousAppId` (ถ้าไม่ทราบจะเป็น null) Names เป็นชื่อแสดงผล ไม่ใช่ IDs ถาวร
- `snooze` หมายถึงกดปุ่ม Snooze ของกลุ่ม ไม่ได้พักอะไรด้วยตัวเอง
- File replies ส่งให้กลุ่มที่ร้องขอ จับคู่ด้วย `requestId`, ตรวจ `ok` และตั้ง deadline ด้วย ticks: replies อาจหายถ้า reload/disable rule Request IDs อาจซ้ำหลัง Run; requests ที่ค้างไม่ใช่งานถาวร

## Panels

```text
spec = { title?: string, description?: string, controls?: Control[],
         position?: "top-left" | "top-right" | "bottom-left" | "bottom-right" | "center",
         width?: "small" | "medium" | "large" | number }
Control = { id?: string, type?: string, label?: string, value?, disabled?: boolean,
            ...type-specific fields below }
```

ค่าเริ่มต้น: ตำแหน่งล่างขวา ความกว้าง 300px; presets small/medium/large คือ 220/280/360px; ความกว้างตัวเลขจำกัดที่ 180–520px และรับ pixel strings ได้ Native panels/sections เรียง controls แนวตั้ง; browser layout, alignment, role, autofocus และ control-dimension fields ไม่มีผลกับ native renderer

IDs ปรับเป็น ASCII letters/digits/`_`/`-` (สูงสุด 80); เลือก IDs ที่ไม่ซ้ำและคงที่ หากไม่ระบุ control ID จะเป็น `control-N`; type ที่ไม่ระบุ/ไม่รู้จักจะเป็น text ข้อความ/lists ที่ไม่ระบุจะว่าง; disabled เป็น false แต่ละ call จะแทนที่ spec ทั้งหมด `value` ที่ระบุชัดจะใช้ก่อน; หากไม่ระบุจะใช้ event value ล่าสุดแล้ว normalize ตาม type Native events ส่ง strings: parse เป็น value type ที่กำหนดก่อน render panel ใหม่ fields ที่ไม่รู้จักจะถูกทิ้ง; panel colors/fonts/CSS เป็นของ Vault

Fields และค่าเริ่มต้นของ controls:

- `text`: string `text`; ค่าเริ่มต้นตาม label `html`: string `html`, ผ่านการ sanitize และแสดงเป็นข้อความธรรมดาบน Windows
- `button`: `label`, และ `action: "submit" | "cancel" | "close"` ที่ไม่บังคับ ค่า click คือ action string หรือค่าว่าง Actions ไม่ submit/close อะไรโดยอัตโนมัติ
- `checkbox`, `toggle`: boolean `value` (เริ่มต้น false); event value คือ `"true"`/`"false"`
- `select`, `radio`: `options: (string | { value: string, label?: string })[]`; string value (เริ่มต้นว่าง) นำ option values ที่ว่างออก; labels เริ่มต้นตาม value หลังเลือกให้อัปเดต typed value ของ panel
- `textInput`, `textarea`: string value (เริ่มต้นว่าง); textInput มี `placeholder`; textarea `rows` 1–12 (เริ่มต้น 3) Native textarea ไม่ใช้ placeholder
- `numberInput`, `range`: numeric value (เริ่มต้น 0), `min`, `max`, `step` ที่เป็นบวก เมื่ออัปเดต panel จะจำกัด values ให้อยู่ใน bounds; bounds สำหรับ normalization ที่ไม่ได้ระบุคือ −1000000…1000000 Native numberInput เป็นการป้อนข้อความ: ตรวจ `Number(event.value)` เอง; min/max/step ไม่จำกัดการพิมพ์ Native range เริ่มต้น 0…100 โดย step 1
- `date`, `time`: ป้อนข้อความ; รูปแบบค่าเริ่มต้น `YYYY-MM-DD`, `HH:MM`/`HH:MM:SS` (รูปแบบเริ่มต้นไม่ถูกต้องจะว่าง) ตรวจ edits เอง `color`: `#RRGGBB` (เริ่มต้น `#000000`)
- `pin`: string ตัวเลข; `length` 3–12 (เริ่มต้น 6), `masked` เริ่มต้น true, `autoSubmit` false `section`: `text`, `controls`; child sections ที่ depth 3 ไม่มี children (root controls depth 0)

Panel events: inputs ทั่วไปส่ง `change`; buttons ส่งเฉพาะ `click`; PIN ส่ง `change` และ `submit` เมื่อ autoSubmit กรอกครบ ไม่มี native mount/unmount/focus/key events Values เป็น strings รวมถึง numbers/booleans `values` มีค่า input จาก rendered snapshot และอาจล้าหลัง edit ที่ทำให้เกิด event; `value` ระบุ edit นั้น สำหรับ forms ที่เชื่อถือได้ ให้เก็บไว้ใน `v.state` และ render typed values Non-click events จะรวมภายใน 100ms ต่อ control; อย่านับ events เป็นจำนวน keystrokes

ขีดจำกัดข้อความ: title/label 240; description/text 1000; HTML 20000; placeholder 500; input text 2000; value strings อื่น 512; option value/label 256 ข้อความเกินจะถูกตัด

## Files

`op`: `"read"`, `"write"`, `"append"`, `"list"`, `"exists"` ต้องเปิด **โฟลเดอร์กฎแบบกำหนดเอง** และ permission ของโฟลเดอร์ใน Settings

- `path` เป็น relative; `/` ใช้แยก directories แต่ละส่วนอนุญาต ASCII letters/digits, spaces และ `_.,@()-`; ห้ามเริ่มด้วยจุด, `.`/`..`, absolute path หรือ URL นามสกุลไฟล์: `.txt`, `.csv`, `.json` (ไม่แยกตัวพิมพ์เล็กใหญ่) List path เป็น directory; `""` แสดง root ที่เลือก Paths ที่ออกนอกโฟลเดอร์ที่เลือก รวมถึงผ่าน symlinks จะถูกปฏิเสธ
- Read คืน UTF-8 text Write แทนที่/สร้างไฟล์; append สร้าง/ต่อท้ายโดยไม่เพิ่ม newline อัตโนมัติ การเขียนจะสร้าง parent directories ให้ String payload เขียนตามตัวอักษร; JSON payload อื่นจะ serialize; null/ไม่ระบุหมายถึง text ว่าง JSON/CSV parsing เป็นหน้าที่ของ rule ขนาดไฟล์สูงสุด: 1048576 UTF-8 bytes
- List คืน subdirectories และ files ที่รองรับซึ่งมองเห็นได้ในระดับถัดไป Entries: `{ name: string, path: string, kind: "directory" | "file", extension?: string }`; extension ของ files มีจุดนำหน้า Exists คืน boolean สำหรับ file path ที่รองรับ

```text
file.data = { requestId: string, op: string, path: string, ok: boolean,
              text: string | null, entries: Entry[] | null,
              exists: boolean | null, error: string }
```

result fields ที่ไม่ได้ใช้เป็น null; เมื่อสำเร็จ error จะว่าง ความล้มเหลวรวมถึง invalid-path, unsupported-file-type, folder unavailable, missing file และ file-too-large ให้ถือว่า error เป็น string ไม่ใช่ enum ที่แจกแจงครบ API ไม่มี transaction; จัด read-modify-write ตามลำดับสำหรับแต่ละ path

## บันทึกของกลุ่ม

`v.store(request)` เริ่มการทำงานบันทึก JSON แบบ asynchronous ภายใน handler บันทึกเป็นของกลุ่มนั้น อยู่ต่อหลัง Run และ restart และถูกลบพร้อมกลุ่ม กลุ่มที่เชื่อมกันมี store แยกกัน Rule เลือก key, value, version, cache invalidation และลำดับความสำคัญของ feedback กฎ Mac และ browser ใช้ API เดียวกัน แต่ store ของแต่ละ app/browser แยกกัน

| Request | Result |
|---|---|
| `{op:"get", key}` | `record` หรือ `null` |
| `{op:"put", key, value, version?, ifRevision?}` | `record` ที่บันทึกแล้ว |
| `{op:"delete", key, ifRevision?}` | `record: null` |
| `{op:"list", prefix?, after?, limit?}` | `entries` ที่เรียงแล้วและ `next` |
| `{op:"clear", prefix?}` | จำนวน `deleted` |

แต่ละ record คือ `{value, revision, version, updatedAt}` ค่า revision แบบ opaque เปลี่ยนเมื่อเขียนจริง; `version` เป็น metadata ของ caller `list` คืน headers โดยไม่มี values ใช้ `get` เพื่ออ่านและ `after: next` เพื่อขอหน้าถัดไป เมื่อไม่ระบุ `put`/`delete`, `ifRevision` put/delete จะไม่มีเงื่อนไข; `null` ต้องไม่มี key นั้น ส่วน revision string ต้องตรงกันพอดี หากไม่ตรงจะคืน `ok:false`, `error:"store-conflict"` และ `record` ปัจจุบัน เพื่อป้องกัน automatic reply ที่ล่าช้าเขียนทับ feedback ของผู้ใช้ `v.context.revision` เป็น configuration marker แบบ opaque ที่ rule อาจรวมใน version ของตัวเอง framework ไม่กำหนดนโยบาย cache

ขีดจำกัด: key 1–2048 ตัวอักษร, version ≤128, JSON value ≤64KiB, depth ≤32, 10000 keys ต่อกลุ่ม และเอกสารรวม16MiB `list` คืนได้สูงสุด100 headers และ96KiB ต่อหน้า สูงสุด32 requests ต่อกลุ่มและ128 starts/second โดยรวม เมื่อเกินจะรายงาน `store-busy` หนึ่งครั้งต่อกลุ่มต่อวินาทีและทิ้งรายการที่เหลือ Storage error คืน `ok:false`; เอกสารอนาคตหรือผิดรูปจะถูกเก็บไว้และรายงาน `store-unavailable` ไม่มี retry หรือ eviction อัตโนมัติ ใช้ ticks กำหนด deadline

## ขีดจำกัด

ต่อ event ต่อ group: 256 queued actions, 200 log calls, 64 emits; ส่วนเกินจะถูกทิ้ง ต่อ rule: 1000 handlers, 24 panels; แต่ละ control list มี 32 entries และแต่ละ choice มี 64 options; ส่วนเกินจะถูกละเว้น/ตัด Emit chains หยุดหลัง 16 generations Serialized state limit: 65536 JavaScript string characters ให้ registration และ handlers รวมของแต่ละ event ใช้เวลาต่ำกว่า 1 second หากเกินซ้ำ ๆ หรือชน hard timeout rule จะหยุดจนกด Run Log เก็บ 200 entries ต่อกลุ่ม Timing/replies เป็น best-effort ไม่ใช่การรับประกันแบบ real-time

## กฎฉบับเต็ม

Steam ถูกบล็อก ยกเว้นช่วงพักห้านาทีที่เริ่มจาก Snooze หรือปุ่ม panel ของมัน:

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


## การเรียก AI จากกฎที่กำลังทำงาน

แต่ละการเรียก `v.ai` ระบุ connection และ model ค่า prompt และ context ที่บันทึกไว้แบบเลือกได้ใช้ใน handler ผ่าน `v.context.ai.prompt` และ `v.context.ai.context`; Vault จะไม่ส่งค่าเหล่านี้เอง rule เลือกข้อมูลของแต่ละ `v.ai` call การแก้ไขค่าจะทำให้คำตอบจาก configuration เก่าใช้ไม่ได้

ชื่อ `conversation` แบบเลือกได้บันทึก transcript ของผู้ใช้/assistant ทั้งหมดไว้ในเครื่อง และส่งข้อความล่าสุดได้สูงสุด16KB ใน call ถัดไป เปลี่ยน model ระหว่าง conversation ได้ การลบหรือ unlink group ไม่ลบ conversation ประวัติชื่อซ้ำที่นำเข้าจะเปลี่ยนชื่อและรายงานใน `preservedConversations` ลบ conversation ร่วมด้วย `v.ai({op:"delete", conversation:"name"})`; คำตอบมาใน event `ai` call ที่กำลังทำงานด้วยชื่อเดิมจะคืน `ai-conversation-busy`

`v.ai({ model, reasoningEffort?, conversation?, prompt, input? })` คืน request ID ทันที เรียกภายใน handler `model` ต้องเป็น ID ที่มี; effort คือ `default`, `low`, `medium` หรือ `high` เมื่อรองรับ `input` เป็น JSON ใดก็ได้; prompt/input จำกัดอย่างละ20KB Replies arrive as `ai` events: `{requestId, ok, text, citations,
error, preservedConversations?}`. Success text is at most48KB; citations are `{url,title}`. Preserve visible,
clickable source links when displaying answers based on web search. Search is always
available to the model. Generation requests have no fixed app deadline. Several
named conversations can run at once; calls using the same name run in order.
Start another call from an `ai` event for a sequence. Vault ยังไม่กำหนดเวลาการเรียกแบบหน่วง ความจุและโควตาของผู้ให้บริการอาจทำให้คำขอเข้าคิวหรือถูกปฏิเสธ ไม่มี fallback หรือ retry อัตโนมัติ คำตอบเป็นข้อมูล ไม่ใช่ JavaScript; rule เลือก action เอง

ตัวอย่าง panel ที่มีปุ่ม `ask` และช่อง `note`:

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

call ที่ค้างจะไม่ทำซ้ำหลัง restart Run, disable/re-enable และ delete จะทิ้งคำตอบเก่า browser ที่ขาดการเชื่อมต่อแจ้ง `ai-unavailable` error อื่นได้แก่ `ai-not-configured`, `ai-invalid-input`, `ai-daily-limit`, `ai-request-failed`, `ai-usage-storage-unavailable` และ `ai-conversation-storage-unavailable` call ที่ล้มเหลวไม่เขียนทับผลเดิมที่เก็บไว้ จำกัด state ที่64KB

ตัวเลือกที่ไม่ต้องใช้ credentials ดูจาก `rule_ai_models` บน Windows หรือ `settings-rule-ai-models` ใน browser และตั้ง `ruleAI` ผ่าน set-group เดิม ฟิลด์คือ `dailyRequestLimit` (null หรือ1–100000), `prompt`, `context` (string เลือกได้ ไม่เกิน16KBต่อค่า) เลือก model และ effort ทุก call ใช้ตัวเลือกที่ส่งกลับมา อย่าใส่ key หรือ provider URL ใน rule
