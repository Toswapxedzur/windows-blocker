# Gudang Windows

Windows Vault adalah anggota asli Windows dari keluarga produk Vault. Ini adalah aplikasi .NET 8 WPF dengan editor WebView2, inventaris aplikasi asli, mesin penegakan, waktu proses aturan khusus, dan hub jembatan aplikasi web lokal.

Kodenya adalah kontrak produk. Panduan dalam aplikasi yang dikelola adalah [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Kemampuan saat ini

- Grup default untuk aplikasi Windows yang dipilih dan grup Kustom untuk aturan kebijakan lanjutan.
- Mode segera, tunjangan, dan hitung mundur; jadwal; membekukan; tidur sebentar; dan kelompok impor/ekspor.
- Inventaris aplikasi Windows dan komponen penegakan berbasis jendela.
- Editor WebView2 yang dihosting dari `src/WindowsBlocker/WebAssets/`.
- Eksekusi aturan khusus yang dikontrol dengan pemeriksaan sintaksis dan umpan log.
- Hub jembatan loopback untuk grup kompatibel yang tertaut secara eksplisit.
- Pengatur waktu asli, roti panggang, dan jendela overlay panel.

## Bangun

Gunakan solusi dan proyek yang sudah didaftarkan:

```powershell
dotnet build WindowsBlocker.sln
```

Proyek aplikasi menargetkan `net8.0-windows` dan menggunakan WPF plus WebView2. Bangun dan jalankan di Windows dengan runtime .NET SDK dan WebView2 yang diperlukan tersedia.

## Peta proyek

| Daerah | Direktori sumber |
| --- | --- |
| Model kelompok dan evaluasi kebijakan | `src/WindowsBlocker/Core/` |
| Penegakan asli | `src/WindowsBlocker/Enforcement/` |
| Inventaris aplikasi dan jembatan WebView | `src/WindowsBlocker/WebUI/` |
| Waktu proses aturan khusus | `src/WindowsBlocker/Rules/` |
| Pusat jembatan | `src/WindowsBlocker/Bridge/` |
| Jendela dan overlay WPF | `src/WindowsBlocker/` |

## Dokumentasi dan terjemahan

Dokumen berbahasa Inggris tetap kanonik. Label UI menggunakan katalog JSON lengkap di `src/WindowsBlocker/WebAssets/translation/`; manual yang diterjemahkan ada di samping `manual/en.md`, dan salinan terjemahan dari sisa dokumen yang disimpan ada di bawah `i18n-docs/<locale>/`.
