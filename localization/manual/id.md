# Panduan pengguna Windows Vault

Windows Vault memiliki tiga halaman: **Vault** memblokir aplikasi native, **Klasifikasi** memberi tag pada konten browser yang didukung, dan **Aktivitas** menampilkan penggunaan yang direkam. Ekstensi browser mengumpulkan konten yang didukung dan menerapkan pemblokiran browser. Pasang dan hubungkan di browser yang Anda gunakan.

## Mulai cepat

1. Di **Vault**, tambahkan grup pemblokiran dan target Apps, lalu pilih aplikasi dengan pemilih +.
2. Pilih perilaku pemblokiran grup dan aktifkan.
3. Di **Klasifikasi**, buat grup, pilih platform, lalu tambahkan tag beserta deskripsinya.
4. Pilih tingkat model lokal dan unduh jika perlu. Aktifkan tagging di setelan Klasifikasi dan lanjutkan grup.
5. Buka konten yang didukung di browser terhubung. Atur filter tag di grup pemblokiran browser jika tag ingin mengendalikan pemblokiran.

## Grup pemblokiran

**Grup pemblokiran** menerapkan kebijakan pemblokiran. **Grup Klasifikasi** memberi tag pada konten; grup ini sendiri tidak memblokir apa pun.

1. Tambahkan grup pemblokiran dan beri nama.
2. Pilih target pada **Berlaku untuk**.
3. Pilih kapan pemblokiran berlaku, lalu atur jadwal atau jatah waktu.
4. Aktifkan grup. Targetnya berbagi kebijakan grup tersebut.

Perubahan rutin tersimpan otomatis. Pesan kesalahan berarti perubahan tidak diterima; perbaiki kolom lalu coba lagi. Nonaktifkan grup untuk menghentikan kebijakannya sambil mempertahankan konfigurasi. **Hapus grup** menghapusnya. Seret grup untuk mengubah urutan. Beberapa grup dapat berlaku untuk satu target; menunda satu grup tidak menghapus pemblokiran grup lain.

**Ekspor** menyalin konfigurasi grup. **Impor** mengganti konfigurasi grup terpilih setelah konfirmasi.

### Jatah waktu dan jadwal

**Blokir segera** berlaku setiap kali grup aktif cocok dan jadwalnya berjalan. **Blokir setelah jatah waktu habis** mengizinkan penggunaan yang cocok hingga jatahnya habis.

Atur jatah dalam menit dan interval reset dalam jam. Batas bergulir menghitung penggunaan dalam jendela sebelumnya. Reset tengah malam memulai periode baru pada tengah malam waktu setempat, termasuk untuk batas bergulir.

Pilih hari aktif dan jendela waktu lokal opsional, satu per baris, seperti **09:00-12:00**. Daftar jendela kosong berlaku sepanjang hari yang dipilih. Jendela harus berakhir setelah waktu mulainya pada hari yang sama; bagi jadwal semalam menjadi beberapa hari.

### Penundaan sementara

Atur penundaan di setiap grup pemblokiran. **Jeda pemblokiran** menangguhkan kebijakan grup selama durasi jeda. **Tambahkan ke jatah waktu** menambah menit pakai ke grup dengan batas waktu. Hanya jatah tambahan yang digunakan yang dihitung sebagai waktu tunda. Jatah tambahan yang tidak dipakai kedaluwarsa saat reset berikutnya; untuk batas bergulir, setelah satu jendela atau lebih cepat pada tengah malam jika diaktifkan.

**Tunda aktivasi** menunda awal jeda sementara pemblokiran tetap berjalan. **Masa jeda** adalah waktu tunggu setelah penundaan berakhir sebelum permintaan berikutnya. **Konfirmasi wajib** menentukan jumlah langkah konfirmasi. Grup yang dibekukan hanya dapat ditunda jika diizinkan sebelum dibekukan.

### Kunci pengeditan dan PIN

**Bekukan** mencegah pengeditan rutin. Untuk mencairkan, diperlukan sepuluh konfirmasi dengan jarak lima detik, ditambah waktu tunggu yang diatur dan PIN enam digit jika tersedia. **Tunggu sebelum mencairkan** menerima 0–72 jam; 0 berarti tanpa waktu tunggu tambahan.

Saat grup dibekukan, waktu tunggu dapat diperpanjang dan PIN dapat ditambahkan jika belum ada. Syarat itu tidak dapat dilonggarkan sebelum grup dicairkan. Penghapusan juga tunduk pada waktu tunggu tersisa dan PIN.

### Grup tertaut

Gunakan **Tautkan** untuk menghubungkan grup yang dipilih secara eksplisit di program Vault lain. Grup tertaut berbagi nama, setelan kebijakan yang didukung, target, penggunaan, dan syarat pembekuan. Tiap program mengedit dan menerapkan jenis target yang didukungnya; entri target lain tetap tersedia bagi program tertaut. Membatalkan tautan mempertahankan setiap grup dan setelannya.

Jika anggota tertaut offline, pengeditan mungkin tidak tersedia. Buka Windows Vault dan browser tertaut untuk menyambung kembali. Kebijakan tersimpan lokal dapat terus berlaku saat anggota offline.

## Bantuan

Klik **i** kecil di samping kolom untuk melihat penjelasan. Klik di luarnya atau tekan Escape untuk menutup. Daftar berada di dalam kotak yang dapat digulir; gulir kotak untuk melihat entri lainnya. Pencarian memfilter daftar yang terlihat tanpa menghapus entri.

Aturan khusus memiliki [Panduan kode](../code-manual/id.md) tersendiri. Panduan itu menjelaskan editor, aktivasi, log, akses berkas, dan API yang didukung.

## Aplikasi native

Gunakan pemilih + pada target Apps untuk memilih aplikasi terpasang. **Blokir semua aplikasi kecuali ini** mengubah daftar menjadi daftar izin. Aplikasi sistem, browser, dan Vault sendiri dikecualikan dari pemblokiran aplikasi native.

Aplikasi yang diblokir diminta untuk berhenti. **Minta aplikasi yang diblokir berhenti lagi setiap (menit)** mengatur percobaan ulang. Pengalihan situs, jeda halaman, dan penyembunyian feed diterapkan ekstensi browser; semuanya bukan aksi aplikasi native.

## Penandaan konten

Grup Penandaan konten memberi tag pada konten platform yang ditetapkan memakai pohon tag dan pengaturan modelnya sendiri. Tiap platform milik satu grup; pilih saat membuat grup, tidak dapat diubah setelahnya. Jadwal dan filter blokir Grup tidak mengendalikan tagging. Aktifkan tagging di Pengaturan. Gunakan **Jeda penandaan / Lanjutkan penandaan** secara terpisah untuk tiap grup. Mematikan recording feed platform di **Aktivitas → Perekaman** juga menghentikan tagging-nya.

### Tag dan pengaturan model

Buat tag, jelaskan artinya, dan tetapkan atau hapus parent di pohon. Seret tag untuk memindahkan cabang. Deskripsi jelas membantu model membedakan tag serupa. Pengaturan tiap grup independen. **Confidence tag minimum** menetapkan tingkat bukti untuk setiap tag prediksi, 1–5. Grup baru mulai di 4 (dukungan kuat). Tidak ada jumlah tag sasaran; item dapat tidak mendapat tag. **Instruksi penandaan** menyimpan preferensi grup. Edit Penandaan konten biasa tersimpan otomatis. Setiap grup memakai koneksi, model, dan reasoning effort yang dipilih secara eksplisit.

### Koneksi AI

Pengaturan memiliki ChatGPT subscription, OpenAI API, Gemini API, dan Grok API. Langganan memakai login Vault dan paket yang memenuhi syarat. Key API mengikuti penagihan dan batas penggunaan provider terpilih. Pilih provider dan masukkan key tepat di bawah dropdown bersama. Perubahan key tersimpan otomatis; mengosongkan field menghapus key. Pilih **Ambil daftar model**, lalu pilih model di grup Penandaan konten atau custom-rule. Mengambil daftar model tidak otomatis memilihnya.

**Upaya penalaran** mengatur seberapa banyak model berpikir: Model default, Low, Medium, atau High jika didukung. Pilihan baru mulai di Medium; pilihan lama tetap tersimpan. Effort lebih tinggi dapat menambah waktu dan token. Web search selalu aktif untuk tagging; model menentukan kapan menggunakannya. Pilih model yang mendukung search dan structured tagging. Permintaan yang gagal/tidak tersedia mempertahankan hasil cache yang valid; item baru tetap belum terselesaikan sampai memilih **Coba ulang klasifikasi cloud**. Vault tidak mengganti provider otomatis. Tiap koneksi memiliki interval retry dan batas percobaan sendiri. Koreksi tag di ekstensi browser: klik **+ tag**, cari dan pilih tag; gunakan kontrol hapus atau pilih tag lalu tekan Delete sekali untuk menghapus. **Tanpa tag** berarti tagging selesai tanpa tag; **Sedang memberi tag** berarti hasil masih menunggu. Koreksi Anda menjadi acuan item tersebut.


## Aktivitas

Aktivitas merekam lokal penggunaan app aktif, kunjungan situs, dan **Konten yang dilihat** yang didukung. Grafik mencerminkan data yang direkam; area kosong tidak membuktikan PC sedang idle. Pilih rentang tanggal. **Linimasa** menunjukkan penggunaan menurut jam; **Total** menjumlah durasi; **Interval waktu** menggabungkan penggunaan tiap interval menjadi blok vertikal. **Warna** adalah legenda yang dapat diklik untuk memfokuskan grafik pada sumber. Pilih hari untuk melihat penggunaan sejak hari itu.

### Grup Aktivitas

Buat grup untuk menampilkan app dan situs pilihannya bersama di Usage. **Merge** memakai satu nama dan warna untuk anggota di seluruh Aktivitas. Aktivitas group mengatur penggunaan terekam dan terpisah dari blokir Grup maupun Penandaan konten. Simpan editor Aktivitas group secara eksplisit dengan **Save**.

### Perekaman dan retensi

Di **Perekaman**, hidupkan atau matikan perekaman per kategori atau sumber. **Simpan** mengatur lama riwayat disimpan; **Selamanya** menyimpannya tanpa kedaluwarsa otomatis. Pilihan individual dapat mengikuti pengaturan luas. Mematikan perekaman menghentikan rekaman baru; menghapus riwayat menghapus entri. Feed platform mengumpulkan konten yang ditampilkan pada halaman platform yang didukung, dibuka atau tidak. Feed dengan **Penandaan didukung** dapat memasok Penandaan konten saat recording aktif. Retensinya terpisah dari penggunaan app dan situs. Pause pada grup Penandaan konten tidak otomatis mematikan recording.

## Pengaturan

Pilih bahasa antarmuka di Pengaturan. Penjelasan field tersedia melalui tombol Info dalam bahasa terpilih.

## Penyimpanan dan pemecahan masalah

Perubahan Vault dan Klasifikasi yang rutin tersimpan otomatis. Pengeditan grup Aktivitas memakai **Simpan**. Menambah, menghapus, mengunduh model, menguji koneksi, dan mengambil daftar model tetap merupakan tindakan eksplisit.

Jika tag hilang, periksa koneksi browser, sakelar tagging global, status jeda grup, rekaman platform, dan unduhan model. Jika riset tidak berjalan, periksa persetujuan, pilihan grup, kredensial penyedia, model riset, dan status riset. Jika grup tertaut tak dapat diedit, sambungkan kembali programnya atau cairkan seperti yang ditunjukkan.
