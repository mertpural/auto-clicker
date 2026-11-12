# AutoClicker 1.0.0 — Son Manuel QA Kontrol Listesi

Bu liste gerçek masaüstü doğrulaması içindir. Önce düşük CPS kullanın ve yalnızca kaydetme/işlem riski olmayan bir hedefte deneyin. Test sırasında tıklamaları bırakmak için `F12` hazır olsun. Her maddeyi Windows ve macOS için ayrı kaydedin; bu dosyadaki adımların listelenmiş olması test edildiği anlamına gelmez.

## Son polish düzeltmeleri için Windows yeniden testi

Gerçek kontrol otomasyonu, iki turlu bellek ölçümü ve incelenen render görüntülerinin sonuçları [FINAL_OWNER_AUDIT.md](FINAL_OWNER_AUDIT.md) içindedir. Aşağıdaki kutular manuel kontroller için boş tutulmuştur; otomatik smoke bunların elle yapıldığı anlamına gelmez.

- [ ] Settings ve About açıkken profiller düğmesine veya listedeki herhangi bir profile tıklayın; seçilen profil sayfası açılır ve doğru gezinme öğesi seçili görünür.
- [ ] Türkçe → English → Türkçe → English geçişini aynı oturumda yineleyin. Açık sayfa, dil seçici, tepsi menüsü ve mouse/tıklama modu açılır listeleri her geçişte güncellenir; seçili profil, tema ve sekme korunur.
- [ ] Yeniden adlandırma açık değilken tutamaçta düğmeyi basılı tutup 5 pikselden fazla dikey sürükleyin. İnce hedef çizgisini görüp bırakın; sıra bir kez değişir, seçili profil kimliği/default/renk korunur ve yeniden açınca sıra saklıdır. Yeniden adlandırma sırasında tutamaç sürüklemeyi başlatmamalıdır.
- [ ] Profil sekmeleri kompakt görünür, aktif sekme dolgu ve odak halkasıyla ayırt edilir. General, Advanced, Position ve Statistics sayfaları tutarlı genişlikte kalır.
- [ ] Kızıl Altın temasında arka plan, sidebar ve alanlar nötr koyu grafit; altın yalnızca küçük vurgu ve ana eylemde görünür. Ayar değişimi profil değerlerini değiştirmez.
- [ ] 500 CPS üst sınırının CPS ve rastgele CPS alanlarına uygulandığını doğrulayın. 501 girilememeli; çalışan satırdaki CPS, yapılandırılmış hedef yerine ölçülen gerçek hızı göstermelidir.
- [ ] 980×760, 1000×800, 1200×800 ve 1920×1080 boyutlarında yatay taşma, kesilen sekme/alt eylem çubuğu ve kenara yapışan alan olmadığını kontrol edin.

## Windows uygulaması

- [ ] Portable uygulama normal kullanıcı yetkisiyle açılır; .NET runtime kurulumu istemez.
- [ ] EXE simgesi Explorer, pencere ve görev çubuğunda doğru görünür.
- [ ] `Start` / `Stop` ve F6 Toggle çalışır; durum göstergesi ve tıklama sayacı güncellenir.
- [ ] Güvenli hedefte sırasıyla 50, 100 ve 200 CPS denenir; çift tıklama ve rastgele CPS de kontrol edilir. Gerçek hız donanım/Windows zamanlamasına bağlıdır.
- [ ] Hold kısayolu basılıyken başlar, bırakılınca durur. F12 ve UI Stop çalışırken motor durur.
- [ ] Hold Mouse Button modunda F5 basılı düğmeyi bırakır; F5 ikinci kez, F12, UI Stop ve Quit tümü düğmeyi serbest bırakır.
- [ ] Mouse Hold fiziksel düğme tetikleyicisi çalışır; tetikleyici ile çıktı düğmesi aynı olduğunda uygulamanın kendi sentetik girdileri yeniden tetikleme yapmaz. Manuel/Emergency Stop sonrasında yeniden kurma davranışı kontrol edilir.
- [ ] Pick Position güvenli hedefte doğru imleç koordinatını alır.
- [ ] Profil adı Enter ile kaydedilir, odak kaybında kaydedilir, Escape ile iptal edilir; yinelenen ad reddedilir.
- [ ] Profil sürükle-bırak, Move Up/Down/Top/Bottom ve renk seçimi çalışır. Yeniden başlatınca adlar, sıralama ve renkler korunur; sıralama varsayılan profili değiştirmez.
- [ ] Türkçe varsayılandır; İngilizceye geçişte sekmeler, tepsi menüsü, iletişimler, doğrulama metinleri, ipuçları, durum ve kısayol özeti çevrilir.
- [ ] System, Night, Crimson Gold, Graphite ve Light temalarında metin, odak, seçili sekme/profil, devre dışı ve tehlikeli eylemler, renk işaretleri ve simge okunaklıdır.
- [ ] Pencere yaklaşık 1000×800, 1200×800 ve 1920×1080 boyutlarında denenir. Normal kullanımda yatay kaydırma, üst üste binme veya erişilemeyen alt eylem çubuğu olmamalıdır. Settings ve About sekmeleri kullanılabilir kalır.
- [ ] Başarılı tek tıklama istatistiği 1, çift tıklama 2 artırır; başarısız tıklama artırmaz. Aktif süre yalnızca Running sırasında artar; start delay dahil edilmez.
- [ ] İstatistikleri sıfırlama, çıkışta kaydetme ve yeniden başlatınca yükleme denenir.
- [ ] Sistem tepsisinden Open, Start/Stop ve Quit denenir. Close-to-tray ve Start Minimized ayarları kontrol edilir.
- [ ] Launch on Startup aç/kapat ve Windows oturum açma sonrası başlatma denenir.
- [ ] Uygulamanın ikinci kopyası ilk pencereyi etkinleştirir; Quit sonrası uygulama kapanır; yeniden açınca ayar/profiller korunur.

## Windows kurulum paketi

Bu sürümde Inno Setup bulunmadığından setup EXE üretilmedi. Aşağıdaki maddeler setup üretildikten sonra uygulanmalıdır:

- [ ] Inno Setup 6 ile `AutoClicker-1.0.0-win-x64-setup.exe` oluşturulur; dosya sürümü ve simgesi kontrol edilir.
- [ ] İzole test ortamında kurulum, Start Menu kısayolu, isteğe bağlı masaüstü kısayolu ve açılış denenir.
- [ ] Önceki kurulum üzerine yükseltme yapılır; profiller, ayarlar ve loglar korunur.
- [ ] Launch on Startup etkinleştirilip oturum açma testi yapılır.
- [ ] Kaldırmada uygulama dosyaları ve başlangıç girdisi kalkar; kullanıcı profilleri, ayarları ve logları kalır.

Beklenen derleme komutu (depo kökünden):

```powershell
ISCC.exe /DAppVersion=1.0.0 packaging\windows\AutoClicker.iss
```

Kurulum yolu per-user `%LOCALAPPDATA%\Programs\AutoClicker` olmalıdır. Gerçek kullanıcı makinesinde doğrulama yerine izole Windows test ortamı kullanılmalıdır.

## macOS uygulaması

macOS'ta gerçek masaüstü testi ve paket üretimi bu Windows oturumunda yapılmadı. Mac'te aşağıdakileri doğrulayın:

- [ ] `bash scripts/generate-macos-icons.sh` ile ICNS üretilir; `bash scripts/publish-macos.sh` ile arm64 publish, `.app` ve DMG oluşturulur.
- [ ] DMG bağlanır, AutoClicker.app `/Applications` veya `~/Applications` içine taşınır ve açılır.
- [ ] Gerekince Accessibility ve Input Monitoring izinleri verilir; Settings içindeki Retry sonrası durum güncellenir.
- [ ] Toggle, Hold, Hold Mouse Button, eski Mouse Hold tetikleyicisi, F12 ve Pick Position güvenli hedefte denenir.
- [ ] Menü çubuğu Open/Start/Stop/Quit, dil ve beş tema kontrol edilir.
- [ ] Başlangıç kaydı, ikinci örneğin ilk pencereyi etkinleştirmesi, normal kapatma ve yeniden başlatma denenir.
- [ ] İmzalı dağıtım hazırlanıyorsa codesign; notarization hazırlanıyorsa ayrı Mac'te Gatekeeper doğrulaması yapılır. Bu adımlar bu teslimatta tamamlanmış sayılmaz.

## QA notları

- Otomatik testler sentetik native API'ler kullanır; gerçek masaüstüne tıklama göndermez.
- Sürüm QA raporunda otomatik test, sentetik zamanlama ölçümü, pasif süreç tanılaması ve gerçek masaüstü manuel kontrolleri ayrı raporlanmalıdır.
- Kamuya açık dağıtımdan önce uygun lisans seçilip eklenmelidir. Bu depoda dağıtım lisansı henüz seçilmemiştir.
