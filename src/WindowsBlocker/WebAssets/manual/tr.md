# Windows Vault kullanıcı kılavuzu

Windows Vault üç sayfaya sahiptir: **Vault** native uygulamaları engeller, **Classifier** desteklenen tarayıcı içeriğini etiketler, **Activity** kaydedilen kullanımı gösterir. Tarayıcı uzantısı desteklenen içeriği toplar ve tarayıcı engellemesini uygular. Kullandığınız tarayıcıya kurup bağlayın.

## Hızlı başlangıç

1. **Vault** içinde bir engelleme grubu ve Apps hedefi ekleyin; ardından + seçicisiyle uygulamaları seçin.
2. Grubun engelleme davranışını seçip etkinleştirin.
3. **Classifier** içinde grup oluşturun, platformları seçin ve açıklamalarıyla etiket ekleyin.
4. Yerel model katmanı seçin, gerekiyorsa indirin. Classifier ayarlarında etiketlemeyi etkinleştirin ve grubu sürdürün.
5. Bağlı tarayıcıda desteklenen içeriği açın. Etiketlerin engellemeyi denetlemesini istiyorsanız tarayıcı engelleme grubunda etiket filtresi yapılandırın.

## Engelleme grupları

**Engelleme grubu** bir engelleme ilkesi uygular. **Classifier grubu** içeriğe etiket atar; tek başına hiçbir şeyi engellemez.

1. Bir engelleme grubu ekleyip ad verin.
2. **Uygulandığı yerler** altında hedefleri seçin.
3. Engellemenin ne zaman uygulanacağını seçin, ardından gerekirse zamanlama veya izin verilen süre belirleyin.
4. Grubu etkinleştirin. Hedefleri grubun ilkesini paylaşır.

Rutin düzenlemeler otomatik kaydedilir. Hata, düzenlemenin kabul edilmediği anlamına gelir; alanı düzeltip yeniden deneyin. Yapılandırmasını koruyarak ilkesini durdurmak için grubu devre dışı bırakın. **Grubu sil** grubu kaldırır. Grupları yeniden sıralamak için sürükleyin. Bir hedefe birden fazla grup uygulanabilir; birini ertelemek diğer grubun engelini kaldırmaz.

**Dışa aktar** grup yapılandırmasını kopyalar. **İçe aktar** onaydan sonra seçilen grubun yapılandırmasını değiştirir.

### İzin verilen süre ve zamanlama

**Hemen engelle**, etkin grup eşleştiğinde ve zamanlaması etkin olduğunda uygulanır. **İzin verilen süre dolduğunda engelle**, süre bitene kadar eşleşen kullanıma izin verir.

İzin verilen süreyi dakika, sıfırlama aralığını saat olarak belirleyin. Kayan sınır, önceki zaman aralığındaki kullanımı sayar. Gece yarısı sıfırlama, kayan sınır için de yerel gece yarısında yeni dönem başlatır.

Etkin hafta günlerini ve isteğe bağlı yerel saat aralıklarını satır başına bir tane olacak şekilde seçin; örneğin **09:00-12:00**. Aralık listesi boşsa seçilen gün boyunca uygulanır. Aralık aynı gün içinde başladığından daha geç bitmelidir; geceyi aşan zamanlamayı ayrı günlere bölün.

### Erteleme

Ertelemeyi her engelleme grubunda yapılandırın. **Engellemeyi duraklat**, grubun ilkesini belirlenen duraklama süresince askıya alır. **İzin verilen süreye ekle**, süre sınırlı gruba kullanılabilir dakika ekler. Ertelenmiş süre olarak yalnızca tüketilen ek süre sayılır. Kullanılmayan ek süre bir sonraki sıfırlamada sona erer; kayan sınırda bir aralık sonra, ayar açıksa daha önce gece yarısında biter.

**Etkinleştirme gecikmesi**, engelleme sürerken ertelemeyi geciktirir. **Bekleme süresi**, erteleme bittikten sonraki yeni isteğe kadar beklenecek süredir. **Gerekli onay sayısı**, onay adımlarını belirler. Erteleme, dondurulmuş grupta yalnızca dondurmadan önce izin verilmişse kullanılabilir.

### Dondurma ve PIN

**Dondur**, rutin düzenlemeleri engeller. Dondurmayı kaldırmak için beş saniye arayla on onay, ayarlanmış bekleme süresi ve altı haneli PIN gerekir. **Dondurmayı kaldırmadan önce bekle** 0–72 saat kabul eder; 0 ek bekleme koymaz.

Grup donmuşken bekleme uzatılabilir ve yoksa PIN eklenebilir. Grup çözülene kadar bu koşullar gevşetilemez. Silme işlemi de kalan bekleme süresine ve PIN'e tabidir.

### Bağlı gruplar

Diğer Vault programlarında açıkça seçilen grupları bağlamak için **Bağla** seçeneğini kullanın. Bağlı gruplar adlarını, desteklenen ilke ayarlarını, hedefleri, kullanımı ve dondurma koşullarını paylaşır. Her program desteklediği hedef türlerini düzenler ve uygular; diğer hedef kayıtları bağlı programlar için kullanılabilir kalır. Bağlantıyı kaldırmak grupları ve ayarlarını korur.

Bağlı bir üye çevrimdışıysa düzenleme kullanılamayabilir. Yeniden bağlanmak için Windows Vault ve bağlı tarayıcıyı açın. Yerel olarak kaydedilen ilke, üye çevrimdışıyken uygulanmaya devam edebilir.

## Yardım alma

Açıklamasını görmek için alanın yanındaki küçük **i** simgesine tıklayın. Kapatmak için dışına tıklayın veya Escape tuşuna basın. Listeler kaydırılabilir kutulardadır; daha fazla öğe için kutuyu kaydırın. Arama öğeleri silmeden görünür listeyi filtreler.

Özel kuralların ayrı bir [Kod kılavuzu](../code-manual/tr.md) vardır. Düzenleyiciyi, etkinleştirmeyi, günlükleri, dosya erişimini ve desteklenen API'yi açıklar.

## Native uygulamalar

Yüklü uygulamaları seçmek için bir Apps hedefinin + seçicisini kullanın. **Bunlar dışındaki tüm uygulamaları engelle** listeyi izin listesine dönüştürür. Sistem uygulamaları, tarayıcılar ve Vault'un kendisi native uygulama engellemesine dâhil değildir.

Engellenen bir uygulamadan çıkması istenir. **Engellenen uygulamadan her (dakikada) yeniden çıkmasını iste** tekrar denemeleri denetler. Web sitesi yönlendirmeleri, sayfa duraklatmaları ve akış gizleme tarayıcı uzantısı tarafından uygulanır; native uygulama işlemlerine dönüşmez.

## İçerik etiketleme

Her İçerik etiketleme, atanan platformların içeriğini kendi tag tree ve model ayarlarıyla etiketler. Her platform tek gruba aittir; oluştururken seçin, sonra değiştirilemez. Engelleme gruplarının çizelge ve filtreleri etiketlemeyi denetlemez. Ayarlar'te etiketlemeyi açın; her grupta **Etiketlemeyi duraklat / Etiketlemeyi sürdür** ayrı çalışır. **Etkinlik → Kayıt** içinde platform feed kaydını kapatmak onun etiketlemesini de durdurur.

### Etiketler ve model ayarları

Etiket oluşturun, anlamını açıklayın ve tree içinde üst etiketini ayarlayın veya kaldırın. Sürüklemek dalı taşır. Açık açıklamalar benzer etiketleri ayırmaya yardım eder. Grup ayarları bağımsızdır. **En düşük etiket güveni** her tahmin edilen etiket için gereken kanıt derecesini 1–5 belirler; yeni gruplar 4 (güçlü destek) ile başlar. Hedef etiket sayısı yoktur; öğe etiketsiz kalabilir. **Etiketleme talimatları** grup tercihlerini tutar. Olağan düzenlemeler otomatik kaydolur; her grup açıkça seçilmiş bağlantı, model ve reasoning effort kullanır.

### AI bağlantıları

Ayarlar'te ChatGPT subscription, OpenAI API, Gemini API ve Grok API vardır. Abonelik Vault'un kendi oturum açmasını ve uygun planı kullanır. API anahtarları seçilen sağlayıcının ücretlendirme ve kullanım sınırlarına tabidir. Sağlayıcıyı seçip anahtarı ortak açılır listenin altına girin; anahtar düzenlemeleri otomatik kaydolur, alanı temizlemek anahtarı siler. **Model listesini getir** modelleri getirir; İçerik etiketleme veya custom-rule grubunda modeli ayrıca seçin.

**Akıl yürütme düzeyi** modelin ne kadar düşüneceğini belirler: destekleniyorsa Model default, Low, Medium veya High. Yeni seçimler Medium ile başlar; eskiler kayıtlı kalır. Yüksek düzey süreyi ve token kullanımını artırabilir. Etiketleme için web search daima açıktır; ne zaman kullanılacağına model karar verir. Arama ve yapılandırılmış etiketlemeyi destekleyen model seçin. Başarısız/erişilemeyen istekler geçerli önbellek sonuçlarını korur; **Bulut sınıflandırmasını yeniden dene** seçilene kadar yeni öğeler çözümsüz kalır. Vault sağlayıcıyı otomatik değiştirmez; her bağlantının retry aralığı ve deneme sınırı ayrıdır. Tarayıcı uzantısında **+ tag** ile arayıp etiket ekleyin; kaldırma kontrolüyle ya da etiketi seçip Delete'e bir kez basarak silin. **Etiketsiz** etiketleme bitti ama etiket yok demektir; **Etiketleniyor** sonuç bekleniyor demektir. Düzeltmeniz o öğede geçerlidir.


## Etkinlik

Etkinlik etkin uygulama kullanımını, web sitesi ziyaretlerini ve desteklenen **Görüntülenen içerik** içeriğini yerel olarak kaydeder. Grafikler kaydedilen veriyi yansıtır; boş alan PC'in boşta olduğunu kanıtlamaz. Tarih aralığı seçin. **Zaman çizelgesi** günün saatine göre kullanım gösterir, **Toplamlar** süreleri toplar, **Zaman aralığı** her aralığın kullanımını dikey bloklarda birleştirir. **Renkler** tıklanabilir bir lejanttır; kaynağı seçerek grafikleri ona odaklayın. Bir gün seçip o günden sonraki kullanımı görün.

### Etkinlik grupları

Seçilen uygulama ve siteleri Usage içinde birlikte göstermek için grup oluşturun. **Merge**, Etkinlik boyunca üyeler için tek ad ve renk kullanır. Etkinlik group kayıtlı kullanımı düzenler; blocking ve İçerik etiketleme gruplarından ayrıdır. Düzenleyiciyi açıkça **Save** ile kaydedin.

### Kayıt ve saklama

**Kayıt** içinde kategori veya tekil kaynak kaydını açıp kapatın. **Sakla** geçmişin ne kadar saklanacağını belirler; **Sonsuza dek** otomatik sona erme olmadan tutar. Tekil seçimler üst ayarı izleyebilir. Kaydı kapatmak yeni kayıtları durdurur; geçmişi silmek kayıtlı girdileri kaldırır. Platform feed'leri desteklenen sayfalarda gösterilen içeriği, açılmış olsun ya da olmasın toplar. **Etiketleme destekleniyor** feed, kayıt açıkken İçerik etiketleme'e içerik sağlayabilir. Saklama süresi uygulama ve site kullanımından ayrıdır. Tagging grubunu duraklatmak kaydı kendiliğinden kapatmaz.

## Ayarlar

Ayarlar'te arayüz dilini seçin. Alan açıklamaları seçilen dildeki Info düğmelerindedir.

## Kaydetme ve sorun giderme

Vault ve Classifier rutin düzenlemeleri otomatik kaydedilir. Activity grubu düzenleme **Kaydet** gerektirir. Ekleme, silme, model indirme, bağlantı testi ve model listesi alma açıkça başlatılan işlemlerdir.

Etiketler eksikse tarayıcı bağlantısını, genel etiketleme düğmesini, grup duraklatma durumunu, platform kaydını ve model indirmesini denetleyin. Araştırma çalışmıyorsa onayı, grup seçimini, sağlayıcı kimlik bilgilerini, araştırma modelini ve araştırma durumunu kontrol edin. Bağlı bir grup düzenlenemiyorsa programları yeniden bağlayın veya belirtildiği gibi dondurmayı kaldırın.
