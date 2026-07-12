# Windows Kasası

Windows Vault, Vault ürün ailesinin yerel Windows üyesidir. WebView2 düzenleyicisi, yerel uygulama envanteri, zorlama motoru, Özel kural çalışma zamanı ve yerel bir web uygulaması köprü merkezi içeren bir .NET 8 WPF uygulamasıdır.

Kod ürün sözleşmesidir. Bakımı yapılan uygulama içi kılavuz şu şekildedir: [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md).

## Mevcut yetenekler

- Seçilen Windows uygulamaları için varsayılan gruplar ve gelişmiş ilke kuralları için Özel gruplar.
- Anında, ödenek ve geri sayım modları; programlar; donmak; kestirmek; ve grup içe/dışa aktarma.
- Windows uygulama envanteri ve pencere tabanlı uygulama bileşenleri.
- `src/WindowsBlocker/WebAssets/` tarafından barındırılan bir WebView2 düzenleyicisi.
- Sözdizimi kontrolü ve günlük beslemesi ile kontrollü Özel kural yürütme.
- Açıkça bağlantılı uyumlu gruplar için bir geridöngü köprü merkezi.
- Yerel zamanlayıcı, tost ve panel yer paylaşımı pencereleri.

## Oluştur

Teslim edilen çözümü ve projeyi kullanın:

```powershell
dotnet build WindowsBlocker.sln
```

Uygulama projesi `net8.0-windows`'yi hedefler ve WPF artı WebView2'yi kullanır. Gerekli .NET SDK ve WebView2 çalışma zamanı mevcut olacak şekilde Windows üzerinde oluşturun ve çalıştırın.

## Proje haritası

| Alan | Kaynak dizini |
| --- | --- |
| Grup modeli ve politika değerlendirmesi | `src/WindowsBlocker/Core/` |
| Yerel yaptırım | `src/WindowsBlocker/Enforcement/` |
| Uygulama envanteri ve WebView köprüsü | `src/WindowsBlocker/WebUI/` |
| Özel kural çalışma zamanı | `src/WindowsBlocker/Rules/` |
| Köprü merkezi | `src/WindowsBlocker/Bridge/` |
| WPF pencereleri ve kaplamaları | `src/WindowsBlocker/` |

## Dokümantasyon ve çeviriler

İngilizce belgeler kanonik olarak kalır. Kullanıcı arayüzü etiketleri `src/WindowsBlocker/WebAssets/translation/`'deki JSON kataloglarının tamamını kullanır; çevrilmiş kılavuzlar `manual/en.md` yanında yayınlanır ve geri kalan korunan belgelerin çevrilmiş kopyaları `i18n-docs/<locale>/` altında bulunur.
