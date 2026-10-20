# Panduan kode Windows Vault

[Panduan pengguna](../manual/id.md)

## Kontrak aturan

Sumber: satu ekspresi fungsi `(on, v) => { ... }`. Hanya JavaScript sinkron dan API di bawah yang didukung; tanpa callback penjadwalan JavaScript, jaringan, API sistem native, atau akses halaman browser. Aturan berbasis waktu memakai timer/pelacak terkelola di bawah, atau `ev.now` dan peristiwa. Aturan browser menggunakan panduan kode ekstensi browser.

- Pengeditan menyimpan draft; **Run** mengaktifkan aturan dan grup. Grup beku tidak dapat Run. Sumber kosong membongkar aturan.
- Run yang berhasil mengganti handler/panel dan menghapus set pemblokiran aplikasi grup ini sambil mempertahankan `v.state`. Kegagalan kompilasi/registrasi mempertahankan aturan sebelumnya; timeout dapat menghentikannya. Restart mendaftarkan sumber terakhir yang diaktifkan; variabel closure dan set blok aplikasi direset.
- Registrasi dapat menginisialisasi state, mendaftarkan handler, menampilkan panel, dan mencatat log. Aksi app/file dan emit harus berada di handler; antrean saat registrasi dibuang.
- Disable menekan handler dan mencabut panel/blok aplikasi. Enable melanjutkan aturan yang dimuat beserta panel/blok tersimpannya. Delete menghapus handler/state/efeknya. Aplikasi yang sebelumnya dihentikan tidak dibuka lagi; penulisan berkas tidak dibatalkan.
- Peristiwa tidak dibatasi target grup biasa; pilih app dalam aturan. Aksi diantrekan lalu diterapkan setelah dispatch. Exception menghentikan handler itu tanpa membatalkan state/aksinya; handler berikutnya masih dapat berjalan. Hanya aksi file memiliki event hasil.

## Timer terkelola dan penggunaan bergulir

`v.timer(id, {direction:"down", durationMs:300000})` membuat atau mengembalikan hitung mundur bernama, awalnya dijeda. Gunakan `{direction:"up"}` untuk timer hitung naik. Handle menyediakan `start()`, `pause()`, `resume()`, `reset()` dan `snapshot()`. Start/resume bersifat idempoten; reset mengembalikannya ke keadaan dijeda dengan waktu berlalu nol. Field snapshot mencakup `ready`, `error`, `elapsedMs`, `remainingMs` (null untuk hitung naik), `running` dan `completed`.

Timer yang berjalan terus maju menurut waktu jam nyata saat app/browser ditutup atau aturan dinonaktifkan. Hitung mundur yang selesai mengirim `timerEnded` dengan `{id, direction, durationMs, generation, incarnation}` sekali per inkarnasi dan generasi timer untuk setiap anggota saat aturannya berjalan berikutnya; Run tidak mengirimnya ulang. Pengiriman bergantung pada peristiwa host; ini bukan alarm waktu nyata. Masukkan `{type:"timer", timerId:"focus", label:"Focus"}` ke panel untuk menampilkannya melalui kontrol teks yang sudah ada.

`v.rolling(id, {windowMs:3600000, limitMs:600000})` membuat atau mengembalikan pelacak penggunaan aktif bernama. `assign({websites:["youtube.com","reddit.com"], apps:[]})` mengganti penetapan target anggota ini. Aturan native dapat menetapkan ID app platform sebagai gantinya. Domain mencakup subdomain. Satu pelacak menggabungkan penggunaan targetnya; ID lain memiliki jatah terpisah. `snapshot()` mengembalikan `ready`, `error`, `usedMs`, `remainingMs`, `available`, dan `nextReturnAtMs` (milidetik berikutnya saat penggunaan tercatat mana pun kedaluwarsa). `available(target?)` juga memeriksa penetapan jika target diberikan. `reset()` menghapus riwayat objek ini.

Semua penggunaan teramati yang memenuhi syarat dihitung, termasuk yang melampaui jatah; pelacak itu sendiri tidak memblokir halaman/app. Interval halaman terlihat yang tumpang tindih dihitung sekali, dan app native menghitung penggunaan latar depan yang teramati. Aturan nonaktif tidak menambah penggunaan; riwayat tetap kedaluwarsa tepat pada batas interval. Ketersediaan baru kembali saat riwayat yang tersisa berada di bawah batas. Sampel heartbeat dibatasi hingga lima detik; jeda karena penutupan/nonaktif tidak menciptakan aktivitas. Peristiwa yang dikirim pengguna tidak menambah penggunaan.

ID objek yang sama dalam grup kustom tertaut berbagi satu otoritas hub native lokal. Penautan menggabungkan riwayat dan penetapan pelacak serta mempertahankan keadaan timer pihak yang memulai; konfigurasi yang tidak kompatibel menolak penautan. Setiap anggota menerima peristiwa selesainya sendiri. Putus koneksi membuat pelacak bersama tidak tersedia dan menolak kontrol timer sampai otoritas terbaru kembali. Saat tautan diputus, setiap anggota menyimpan penggunaan bersama dan keadaan timer terakhir, memulihkan targetnya sendiri, serta membuang perintah dari tautan lama. Hub menyimpan snapshot akhir sampai anggota yang kembali menyimpan dan mengakuinya; serah terima tertunda diselesaikan sebelum tautan baru tersedia.

Objek bernama bertahan melewati Run dan restart dalam `v.state.$vaultTime` yang dicadangkan; jangan sunting field itu secara langsung. Hapus secara eksplisit dengan `v.removeTime(id)`. ID terdiri dari 1–80 huruf ASCII, digit, `_` atau `-`; nama properti cadangan ditolak. Maksimal 32 objek berbagi batas state aturan sebesar 64 KiB UTF-8. Durasi berupa milidetik bulat hingga 365 hari. Riwayat bergulir dibatasi hingga 1024 bucket menit dan 2048 interval gabungan. Kesalahan kapasitas riwayat membuat pelacak tidak tersedia sampai direset secara eksplisit; konfigurasi tidak valid dan penyimpanan terlalu besar melaporkan kesalahan tanpa menghapus data tersimpan.

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

- `on(type, handler)` → boolean. Mendaftarkan `handler(ev)`; beberapa handler berjalan sesuai urutan pendaftaran. False berarti argumen tak valid atau batas handler tercapai. `ev = { type: string, now: number, data }`; `now` adalah Unix milliseconds.
- `v.state`: objek JSON yang dapat diubah dan disimpan setelah dispatch. Inisialisasi field hilang alih-alih menimpa state lama. Nilai non-object/array meresetnya menjadi `{}`; update tak dapat diserialisasi/terlalu besar tidak disimpan.
- `v.log(...values)`: menambahkan ke Log grup ini. Kegagalan eksekusi dan pengiriman muncul sebagai entri kesalahan eksplisit dalam feed yang sama. Logs/Clear terpisah per grup. Kesalahan pemuatan juga muncul di status Run; peristiwa yang baru dijalankan sebagian tidak dijalankan ulang secara otomatis.
- `v.emit(type, data)`: mengantre salinan JSON `data` untuk handler grup ini setelah peristiwa saat ini, dengan `now` baru; bukan panggilan sinkron.
- `v.panel(id, spec)`: mengganti panel mengambang bernama grup; null `spec` menghapusnya. Lihat Panels.
- `v.file(op, path, payload?)` → string ID permintaan. Lihat Files.
- `v.block(appId, on)`: true mempertahankan blok aplikasi, false menghapus blok grup ini. Blok bergabung antarkelompok aktif; pemanggilan ini tidak dapat membuka target grup lain. Pemblokiran meminta quit biasa dan mencoba ulang pada interval Setelan; tidak mencegah proses dimulai atau menjamin app menerima Quit.
- `v.quit(appId)`: satu permintaan quit biasa, tunduk pada kebijakan perlindungan/coba ulang yang sama; tanpa blok berkelanjutan.
- `v.open(appId)`: meminta Windows membuka app terpasang; tanpa callback sukses.

Pemanggilan lain mengembalikan `undefined`. ID app adalah path executable penuh atau application user model ID yang tersedia dalam peristiwa dan pemilih app. Block/Quit mengabaikan proses sistem Windows, browser, Vault/pembantunya, dan ID kosong. ID/state panel milik satu grup, bukan nama tampilannya.

## Peristiwa

Notasi payload di bawah menjelaskan tipe, bukan kode yang dapat dijalankan. `?` menandai field opsional.

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

- `tick` perkiraan; gunakan timestamp, bukan jumlah tick. Running mencantumkan proses aplikasi Windows yang dikenali. Frontmost dapat null atau ber-ID app kosong.
- `app` melaporkan perubahan siklus hidup yang diamati sebelum event `tick` itu. Hanya focus menyertakan `previousAppId` (null jika tak diketahui). Nama adalah nama tampilan, bukan ID stabil.
- `snooze` berarti tombol Snooze grup ditekan. Ini sendiri tidak menjeda.
- Balasan file ditujukan ke grup peminta. Cocokkan `requestId`, periksa `ok`, dan tetapkan tenggat dengan tick: balasan dapat hilang jika aturan dimuat ulang/dinonaktifkan. ID permintaan dapat berulang setelah Run; permintaan tertunda bukan pekerjaan permanen.

## Panel

```text
spec = { title?: string, description?: string, controls?: Control[],
         position?: "top-left" | "top-right" | "bottom-left" | "bottom-right" | "center",
         width?: "small" | "medium" | "large" | number }
Control = { id?: string, type?: string, label?: string, value?, disabled?: boolean,
            ...type-specific fields below }
```

Default: posisi kanan bawah, lebar 300px; preset small/medium/large 220/280/360px; lebar numerik dibatasi 180–520px dan menerima string pixel. Panel/section native menumpuk kontrol vertikal; field layout, alignment, role, autofocus, dan dimensi kontrol browser tidak memengaruhi renderer native.

ID dinormalisasi ke ASCII huruf/angka/`_`/`-` (maks. 80); pilih ID unik dan stabil. ID kontrol yang tidak diberikan menjadi `control-N`, tipe yang tidak diberikan/tidak dikenal menjadi text. Teks/daftar yang tidak diberikan kosong; disabled false. Setiap pemanggilan mengganti seluruh spec. `value` eksplisit menimpa input tersimpan; value yang tidak diberikan memakai nilai event terakhir lalu normalisasi tipe. Event native memberi string: parse ke tipe nilai yang ditetapkan sebelum merender panel baru. Field tak dikenal dibuang; warna/font/CSS panel milik Vault.

Field kontrol dan nilai awal:

- `text`: string `text`; default label. `html`: string `html`, disanitasi dan ditampilkan sebagai teks biasa di Windows.
- `button`: `label`, opsional `action: "submit" | "cancel" | "close"`. Nilai klik adalah string aksi atau kosong. Aksi tidak otomatis mengirim/menutup.
- `checkbox`, `toggle`: boolean `value` (default false); nilai event `"true"`/`"false"`.
- `select`, `radio`: `options: (string | { value: string, label?: string })[]`; nilai string (default kosong). Nilai opsi kosong dihapus; label default ke nilai. Perbarui nilai panel bertipe setelah pilihan.
- `textInput`, `textarea`: nilai string (default kosong); `placeholder` textInput; `rows` textarea 1–12 (default 3). Textarea native mengabaikan placeholder.
- `numberInput`, `range`: nilai angka (default 0), `min`, `max`, `step` positif. Nilai dibatasi saat panel diperbarui; batas normalisasi tak disebut −1000000…1000000. numberInput native berupa input teks: validasi `Number(event.value)` sendiri; min/max/step tidak membatasi pengetikan. Range native default 0…100, step 1.
- `date`, `time`: input teks; format awal `YYYY-MM-DD`, `HH:MM`/`HH:MM:SS` (format tak valid menjadi kosong). Validasi perubahan sendiri. `color`: `#RRGGBB` (default `#000000`).
- `pin`: string digit; `length` 3–12 (default 6), `masked` default true, `autoSubmit` false. `section`: `text`, `controls`; section anak pada depth 3 tak punya anak (root controls depth 0).

Event panel: input biasa mengirim `change`; tombol hanya `click`; PIN mengirim `change`, lalu `submit` jika autoSubmit terisi. Tidak ada event mount/unmount/focus/key native. Nilai berupa string termasuk angka/boolean. `values` berisi nilai input snapshot yang dirender dan mungkin tertinggal dari edit pemicu; `value` mengidentifikasi edit itu. Simpan ke `v.state` dan render nilai bertipe agar form andal. Event non-click digabung dalam 100ms per kontrol; jangan menghitungnya sebagai ketukan tombol.

Batas teks: title/label 240; description/text 1000; HTML 20000; placeholder 500; input text 2000; string nilai lain 512; option value/label 256. Kelebihan dipotong.

## Berkas

`op`: `"read"`, `"write"`, `"append"`, `"list"`, `"exists"`. Memerlukan **Folder aturan khusus** di Setelan dan izinnya.

- `path` relatif; `/` memisahkan direktori. Segmen mengizinkan ASCII huruf/angka, spasi dan `_.,@()-`; tanpa titik awal, `.`/`..`, path absolut, atau URL. Sufiks `.txt`, `.csv`, `.json` (tidak peka kapitalisasi). Path List harus direktori; `""` mencantumkan root pilihan. Path keluar dari folder pilihan, termasuk melalui symlink, ditolak.
- Read mengembalikan teks UTF-8. Write mengganti/membuat; append menambah/membuat tanpa baris baru otomatis. Direktori induk dibuat saat menulis. Payload string ditulis persis; payload JSON lain diserialisasi; null/tidak diberikan berarti teks kosong. Parsing JSON/CSV tugas aturan. Ukuran berkas maksimum 1048576 byte UTF-8.
- List mengembalikan subdirektori langsung yang terlihat dan berkas didukung. Entri: `{ name: string, path: string, kind: "directory" | "file", extension?: string }`; extension berkas mencakup titik. Exists mengembalikan boolean untuk path berkas didukung.

```text
file.data = { requestId: string, op: string, path: string, ok: boolean,
              text: string | null, entries: Entry[] | null,
              exists: boolean | null, error: string }
```

Field hasil yang tak dipakai bernilai null; sukses memiliki error kosong. Kegagalan termasuk invalid-path, unsupported-file-type, folder tak tersedia, file hilang, dan file-too-large. Perlakukan error sebagai string, bukan enum tetap lengkap. Tidak ada API transaksi; serialkan operasi read-modify-write per path.

## Batas

Per peristiwa per grup: 256 tindakan dalam antrean, 200 pemanggilan log, 64 emit; kelebihannya dibuang. Per aturan: 1000 handler, 24 panel; tiap daftar kontrol memiliki 32 entri dan tiap pilihan 64 opsi; kelebihannya diabaikan/dipotong. Rantai emit berhenti setelah 16 generasi. Batas state terserialisasi: 65536 byte UTF-8. Pendaftaran dan gabungan handler tiap peristiwa harus selesai dalam kurang dari 1 detik; pelampauan berulang atau timeout paksa menghentikan aturan sampai Run. Log menyimpan 200 entri per grup. Waktu/balasan bersifat upaya terbaik, bukan jaminan waktu nyata.

## Aturan lengkap

Steam diblokir kecuali selama jeda lima menit yang dipicu Snooze atau tombol panelnya:

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
