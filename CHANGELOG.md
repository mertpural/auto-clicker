# Değişiklik Günlüğü

## 1.0.0

- Yeni tasarım dili: kartlı yerleşim, yuvarlak köşeler ve hap biçimli sekmeler. Beş yeni tasarım teması eklendi: Gece Neon (varsayılan, siyah), Sade Beyaz, Aurora Cam, Terminal ve Oyun HUD. Sistem, Gece, Kızıl Altın, Grafit ve Açık temaları korunur ve yeni yerleşimi kullanır.
- macOS menü çubuğu: simgeye tıklayınca profil seçimi, Başlat/Durdur ve kullanım istatistiklerini gösteren pop-up açılır; simge oluşturulamazsa önceki menü kullanılır. Windows sistem tepsisi menüsü değişmedi.
- macOS uygulama menüsü adı "AutoClicker" olarak düzeltildi.
- Profil listesi: satırın herhangi bir yerinden sürükleyerek sıralama yapılır (tutacak da çalışır); tutacak büyütüldü, satır yüksekliği ve varsayılan profil işareti (★) düzenlendi.
- macOS paketleme: `.app` iCloud klasöründe değil geçici klasörde derlenip imzalanır; DMG içindeki uygulama doğrulanır. Ücretsiz ad-hoc imza kullanılır.
- macOS: eski sürümden kalan `profiles.json` (ör. Minecraft/Roblox profilleri) açılışta yeni biçime aktarılır; özgün dosyalar `.legacy-backup` olarak yedeklenir. İzin eksikse ana ekranda uyarı ve izin düğmeleri gösterilir; Sistem Ayarları'ndan dönünce izinler otomatik yeniden denenir. Çift tıklama macOS'ta gerçek çift tık sayısıyla gönderilir. Yayın betiği uygulamayı ücretsiz ad-hoc imzalar. Windows yolu ayrıştırması Mac'te de doğru çalışır.
- Dil değişiminde ComboBox seçeneklerinin kimliği korunur; aynı oturumda Türkçe/İngilizce dönüşü ve sayısal alanların yerel biçimi gerçek kontrol testleriyle doğrulanır.
- Sürükleme tutamacında yanlış profil seçimi ve hedef çizgisinin gereksiz yenilenmesi giderildi; taşıma drop anında tek kayıt yapar, iptal sıralamayı korur.
- Profil düzenleyici, autosave, engine ve tray abonelikleri kapanışta temizlenir; tray tekrar başlatılması yinelenen handler oluşturmaz.
- Ayarlar'daki İzinler başlığı tamamlandı. Windows geliştirme çalıştırmasındaki başlangıç kaydı uygulamanın giriş DLL'ini hedefler; dağıtım paketleri kişisel kaynak yollarını içeren PDB dosyalarını taşımaz.
- Windows ve macOS için Türkçe varsayılanlı, İngilizce dil seçeneği bulunan masaüstü AutoClicker.
- Sabit ve rastgele CPS, çift tıklama, burst, tıklama/süre sınırları ve sabit imleç konumu.
- Klavye Toggle/Hold, fiziksel Mouse Hold tetikleyicisi ve ayrı kısayollu Hold Mouse Button modu; F12 acil durdurma.
- Profil oluşturma, yeniden adlandırma, renk seçme ve sıralama; düzenlemeler yeniden başlatma sonrasında korunur.
- System, Night, Crimson Gold, Graphite ve Light temaları; sekmeli düzen, yardım ipuçları ve çalışma durumu/kısayol özeti.
- Başarılı tıklama ve çalışma süresi için yerel kullanım istatistikleri; uygulama çalışırken izleme ve sıfırlama.
- Windows sistem tepsisi ve macOS menü çubuğu; küçültme, başlangıçta açma ve mevcut pencereyi etkinleştiren tek örnek davranışı.
- Koordinat seçmek için Pick Position aracı ve Windows/macOS yerel giriş desteği.
- Grafit mouse görseli uygulama, yürütülebilir dosya, tepsi ve paketleme kaynaklarında kullanılır; Windows çok boyutlu ICO'su ve macOS ICNS üretim betiği sağlanır.
- Windows x64 self-contained portable paketi, SHA-256 ve release manifesti üretimi. Windows kurulum paketi Inno Setup 6 ile ayrıca oluşturulur. macOS `.app` ve DMG üretimi Mac gerektirir.
- Ayarlar ve Hakkında'dan profillere açık gezinme; çalışma sırasında iki yönlü Türkçe/İngilizce geçişi ve profil seçeneklerinin anında yenilenmesi.
- Profil tutamacında eşik aşılınca başlayan sürükle-bırak sıralaması; seçim ve varsayılan profil korunur.
- Gerçek Windows `SendInput` ölçümüne göre desteklenen hız üst sınırı 500 CPS. Daha önce kaydedilmiş yüksek CPS değerleri ilk yüklemede 500'e uyarlanır.
- Daha kompakt sekmeler, profil düzenleme sayfalarında dengeli içerik genişliği, yapılandırılmış Gelişmiş seçenekler ve nötr grafit yüzeyli Kızıl Altın teması.

Platform doğrulama durumu ve bilinen sınırlamalar [README.md](README.md) içinde açıklanır.
