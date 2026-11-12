# AutoClicker 1.0.0 — Son owner doğrulama raporu

Tarih: 27 Eylül 2026. Host: Windows 10.0.26200, .NET SDK 10.0.301, hedef net8.0, Avalonia 11.3.22. Bu rapor önceki çalışmanın devamıdır. Otomatik kontrol, native pencere render'ı, sahte mouse zamanlaması ve insan tarafından yapılan masaüstü testleri ayrı değerlendirilmiştir.

## 1. Önceki değişiklikler ve repository durumu

Mevcut solution ve beş proje korundu. Dil seçeneklerinin sabit kimliği, tunnel pointer handler, drag oturumu ve lifecycle cleanup kaynaklarda doğrulandı; reset, yeniden oluşturma veya geri alma yapılmadı. Bu klasörde `.git` bulunmadığından Git diff/commit karşılaştırması mevcut değildir. Son kaynak dosyalarının SHA256 listesi `artifacts/owner-audit/source-fingerprint.json` içindedir. Başlangıçtaki 204 testlik kapsam korunarak 217 teste çıkıldı; devam isteğinde belirtilen 213 test de korundu.

## 2. Dil hatasının gerçek kök nedeni

Dil değişiminde seçenek dizileri ve yerelleştirilmiş label içeren record nesneleri yeniden oluşturuluyordu. Gerçek Settings ComboBox'un iki yönlü SelectedItem binding'i çalışırken ItemsSource ve seçenek kimlikleri değişiyordu. TR → EN çalışırken EN → TR dönüşü UI testinde başarısız oldu; yalnız ViewModel üzerinden dili değiştiren eski testler bunu yakalamıyordu.

Settings ve profil enum seçenekleri artık yaşam süresi boyunca aynı dizi/nesne kimliğini korur. Dil değişiminde observable Label alanları güncellenir. Kalıcı enum değerleri İngilizce kalır. Ekran incelemesinde ayrıca İngilizce sayısal alanların Türkçe ondalık ayıracı kullandığı bulundu; NumericUpDown.NumberFormat yerelleştirmeye bağlandı. CPS/interval değerleri değiştirilmez.

## 3. Gerçek dil kontrol testleri

Headless test gerçek App stillerini, MainWindow'u ve bağlı Settings ComboBox'u kullanır. TR → EN → TR → EN → TR ve EN → TR → EN döngüleri geçti. ComboBox display, Settings başlığı, sidebar, dört sekme, durum, action bar ve enum label'ları kontrol edilir. Profil verileri/sırası/kimliği, tema ve seçili sekme korunur. Ayrı test çalışan gerçek Core engine ve basılı mouse state'inin dil değişiminde korunmasını sahte mouse output ile doğrular.

Yayımlanan EXE'de aynı canlı ComboBox.SelectedItem binding'i ve gerçek NativeMenu etiketleri doğrulandı. Bu, popup'a insan tarafından mouse ile tıklama testi değildir; önce yeniden üretilen binding hatasını gerçek kontroller üzerinden kapsar. Kanıt: `tests/AutoClicker.Tests/DesktopUiTests.cs`, `artifacts/owner-audit/published-final-controls.json`.

## 4. Navigation actual control akışı

Settings → Profiles, Settings → mevcut/başka profil ve About → profil geçti. Profile workspace görünürlüğü, seçili profil ve Settings'in seçili görsel durumunun kapanması assert edilir. Headless test sidebar'a gerçek hit test ile MouseDown/MouseUp gönderir. Native/published smoke gerçek ButtonAutomationPeer.Invoke ve profil TextBlock'una routed PointerPressed kullanır; yalnız ViewModel komutu çalıştırmaz.

Mevcut kaynakta başlangıç navigation testleri zaten geçiyordu; kullanıcının önceki portable dosyasındaki arızanın kök nedeni bu kaynakla yeniden üretilemedi. Eski binary'nin aynı kaynakla eşleştiği doğrulanamadı. Tek NavigationPage state modeli korundu, liste pointer akışı tunnel üzerinden güvenilir biçimde OpenProfile'a yönlendirildi ve güncel yayın dosyasında kontrol akışları tekrar doğrulandı. Eski portable'ın manuel problemi yalnız önceki test sayısına dayanılarak kapatılmadı.

## 5. Drag/drop davranışı

5 DIP eşik korunur. Handle press aktif profili değiştirmez. Pointer hareketi listeyi veya diski değiştirmez. Başarılı drop ortak taşıma mantığında bir sıra değişimi ve bir autosave üretir. Aynı hedefin tekrarlı bildirimleri state değişimi oluşturmaz. İptal ve dışarı bırakma oturum sonunda sırayı korur; rename açıkken drag başlamaz. Seçili profil kimliği, renk ve default flag korunur. Context menu taşıma komutları korunmuştur.

Kök sorunlar: ListBox seçiminin handle handler'dan önce çalışması, handle'ın hit bölgesi ve aynı hedef için yinelenen gösterge güncellemeleriydi. Şeffaf 22 DIP handle hit bölgesi, tunnel intercept ve idempotent hedef güncellemesi bunları giderdi. Gerçek pointer press/küçük hareket testi ve drop/cancel/tek disk kaydı testleri geçer. OS drag döngüsünün insan tarafından hissedilen akıcılığı manuel kontrol listesinde kalır.

## 6. Drag görseli

Sürüklenen satırın opacity'si %76; hedefin üst/alt kenarında ince insertion çizgisi vardır. Drop/cancel sonrası opacity ve hedef işareti temizlenir. Animasyon eklenmedi; aynı hedefte gereksiz güncelleme ve flicker kaldırıldı.

## 7. Son UI düzenlemeleri

- 204 DIP sidebar, 20 px profil başlığı, ayrı durum ve küçük kısayol satırı.
- 36 DIP sekmeler, 14 px yazı, seçili sekmede küçük vurgu ve dolgu.
- Temel: hizalı mouse/tıklama türü, tam satır mod, dengeli CPS/interval ve 220 DIP profil rengi seçicisi. Hold Mouse Button için ilgisiz hız/random/burst alanları gizlenir; saklanan değerler korunur.
- Gelişmiş: Başlangıç, Otomatik Durdurma, Gruplu Tıklama grupları; doğal birimler, devre dışı fakat korunan alanlar. Yeni profilin kapalı başlangıç gecikmesi düzenleme değeri 3 saniyedir; etkinleştirilene kadar uygulanmaz.
- Konum: 460 DIP içerik sınırı, current/fixed açıklaması; sabit konumda X/Y ve Konum Seç.
- İstatistik: iki kompakt yüzey, bu cihazdaki toplam kullanım ve yalnızca yerel saklama notu. Başlangıç değerleri sahte kullanım rakamlarıyla doldurulmadı.
- Alt bar: ince üst border, içerikle aynı arka plan; Kaydedildi ve ikincil işlemler solda, ana eylem sağda. Scroll'dan bağımsızdır.
- Settings: yaklaşık 700 DIP sınır, Genel/Görünüm/Kısayollar/İzinler/Veriler. Dil ve tema 270 DIP alanlarda; Hakkında küçük ikon ve sürümle sadeleşti.

## 8. Crimson Gold

Eski geniş kırmızı/kahverengi yüzey yaklaşımı kaldırıldı. Arka plan `#151515`, sidebar `#191919`, yüzey `#1D1D1D`, yükseltilmiş yüzey `#242424`, input `#202020`, border `#343434`, metin `#F1F1EF`, ikincil metin `#A6A39B`. Kızıl `#741E26` / hover `#8B252E`, altın `#B79A4A` / highlight `#CCAF5A`. Alanlar nötrdür; kızıl küçük gezinme vurgularında, altın ana eylem ve kontrol vurgularında kullanılır. Beş tema ve System fallback semantic resource testleri geçti. İkon renkleri değiştirilmedi.

## 9. Responsive sonucu

980×760, 1000×800, 1200×800 ve 1920×1080 native pencerelerinde Türkçe Gelişmiş/Settings ve İngilizce Temel/Settings render edildi; 16 görüntünün tamamı açılıp incelendi. Sekmeler, normal metinler ve alt bar kırpılmadı; yatay sayfa taşması görülmedi. 980×760 Settings dikey scroll kullanır. Headless layout testi ayrıca her boyutta dört profil sekmesini, alt bar erişimini ve sayfa ScrollViewer genişliğini denetler. Sayısal TextBox'un kendi iç kaydırması sayfa taşması sayılmaz. DPI/çoklu monitör ve tüm etkileşimli durumlar ayrıca manuel denenmelidir.

## 10. İlk bellek turu

Son iki turlu ölçümün ilk turu: 60 saniye idle sonrası 20 Settings aç/kapat, 50 sekme, 50 profil, 20 dil, 20 tema değişimi, dört drop ve bir cancel; ardından 90 saniye idle. Working set işlem sonrasında 225,52, 90 saniye sonunda 222,21 MiB oldu. Private bytes 180,55 → 178,11 MiB; managed tahmini 40,40 → 42,91 MiB. Önceki kısa/tek turlu ölçümdeki yaklaşık 165 → 222 MiB gözlemiyle tutarlıdır.

## 11. İkinci bellek turu

Aynı işlem sayıları tekrarlandı. Working set 228,19 → 225,17 MiB, private bytes 180,65 → 177,48 MiB. İkinci turun 90 saniye sonu, birinci turun aynı noktasına göre working set yalnız +2,96 MiB, private bytes −0,63 MiB. Profil kimliği ve sıra korundu. Kanıt: `artifacts/owner-audit/retention-two-rounds.json`.

## 12. Sızıntı değerlendirmesi

İlk turdaki yaklaşık 61 MiB working set artışı ikinci turda tekrar etmedi. Süreç/native bellek davranışı cache/yüksek su seviyesi ile uyumludur; bu iki turda belirgin doğrusal sızıntı görülmedi. Managed tahmininin yükselmesi tek başına retained/live nesne miktarı değildir. Bu kısa test küçük veya uzun vadeli managed sızıntıları kesin dışlamaz. GC.Collect, trimming, NativeAOT veya scheduler yeniden yazımı kullanılmadı. Önceden gerçek dispose abonelik eksiklikleri bulundu ve giderildi.

## 13. Managed heap

GC.GetTotalMemory(false): başlangıç 18,38; idle60 19,96; ilk turun 90 saniye sonu 42,91; ikinci turun 90 saniye sonu 54,76 MiB. Son GC'deki heap büyüklüğü iki turda 12,64 → 14,53 MiB. GC Gen0/1/2 sayıları 0/0/0 → 11/8/1 → 21/18/1. İkinci turdan sonra Gen2 toplaması olmamıştır; 54,76 MiB değerini tamamen canlı/retained nesne olarak yorumlamak doğru değildir.

## 14. Working set

Başlangıç 172,29; idle60 161,14; ilk tur idle90 222,21; ikinci tur idle90 225,17 MiB. Bu host'ta 100–108 MB hedefi ölçülmedi ve böyle bir sonuç iddia edilmez. Runtime/render cache'leri dahil süreç ölçümüdür.

## 15. Private bytes

Başlangıç 135,95; idle60 123,04; ilk tur idle90 178,11; ikinci tur idle90 177,48 MiB. İkinci turda sürekli tahsis artışı görülmedi.

## 16. Thread sayısı ve view'lar

Başlangıç 99; idle60 92; ilk tur idle90 90; ikinci tur idle90 89 thread. Turlarla thread artışı görülmedi. Her örnekte görsel ağaçta bir SettingsView ve bir ProfileEditorView vardı; bu sayım görsel ağacın dışındaki nesneler için heap dump yerine geçmez. Lifecycle testleri abonelikleri ayrıca denetler.

## 17. Idle CPU

Stopped, diagnostics UI/logger olmadan, sıradan native uygulama penceresinde ölçüldü. Smoke yalnız mouse output'u sahte adaptörle değiştirir; native tray ve global hooks çalışır. 16 kullanılabilir işlemcide ilk 60 saniye toplam sistem kapasitesinin %0,181'i, iki tur sonrası idle bölümleri %0,172–0,212'si; bir çekirdeğe göre %2,76–3,38. Önceki pasif baseline yaklaşık %2,8/çekirdek idi; CPU iyileşmesi iddia edilmez. Uygulamaya ait Stopped click/telemetry polling döngüsü bulunmadı. Near-zero toplam kullanım kabul edilebilir; mutlak sıfır değildir. Bazı ölçüm aralıklarında ayrı test/screenshot süreçleri de çalışmıştır; CPU süresi yalnız incelenen uygulama sürecinindir.

## 18. Subscription/timer temizliği

ProfilesViewModel.Dispose artık autosave.Completed, engine.TelemetryChanged/Error ve seçili profil handler'larını kaldırır; mevcut localization/settings/hotkey/mouse/statistics temizliği korunur. Tray Initialize idempotenttir; aynı seçili profil için handler tekrar bağlanmaz. Tekrarlı initialize ve dispose testleri abonelik sayısının sabit kaldığını/temizlendiğini doğrular.

İstatistik checkpoint timer'ı Stopped durumda sonsuz beklemeye alınır, dispose edilir. Windows timer tanılaması hedef başına tek reusable handle gösterir. Single-instance FileSystemWatcher/channel kullanır; idle polling eklenmedi. Bitmap/icon tekrar decode için yeni bir event path eklenmedi; tray ikonunun tekrar başlatılması engellendi.

## 19. Test sayısı ve kapsamı

204 → 213 → **217** test. Gerçek kontrol dil/gezinme, numeric culture, inline rename Enter/Escape/focus loss, drag selection/cancel/drop/tek save, runtime running/held korunması, semantic resource'lar, responsive layout, lifecycle ve QA single-instance izolasyonu eklendi/güçlendirildi. Native API testleri sahte çağrılar kullanır. Eski testlerin birindeki tray testi gerçek UI thread ihtiyacı nedeniyle AvaloniaFact'e taşındı; ürün davranışı değiştirilmedi.

## 20. Debug sonucu

Son kaynakla build: **0 uyarı, 0 hata**. **217/217** test başarılı. Resmi yayın betiği aynı Debug kapılarını yeniden geçti. Kanıt: `artifacts/owner-audit/tests/debug-complete.trx`, `artifacts/owner-audit/publish-windows-final.txt`.

## 21. Release sonucu

Son Release/PathMap derlemesi: **0 uyarı, 0 hata**. **217/217** test başarılı. Resmi betikte restore/test/publish başarılı; NuGetAudit kapatılmadı. Inno eksikliği betiğin paketleme durum uyarısıdır; C# derleme uyarısı değildir.

## 22. On ardışık Release sonucu

Son kaynak ve Release yol eşlemesiyle **10/10 tur**, her tur **217/217**, toplam 2170 başarılı test çalışması; başarısız/atlanan test yok. Timeout artırılmadı. Son kanıtlar `artifacts/owner-audit/tests/release-packaging-1.trx` … `release-packaging-10.trx`; toplu sayılar `release-repeat-summary.json` içindedir.

## 23. 50 / 100 / 200 / 500 CPS

Hedef başına 5 saniye, sahte mouse sink: 50 → **50,0** (250 click), 100 → **100,0** (500), 200 → **200,0** (1000), 500 → **499,4** (2497). Her hedefte bir high-resolution timer handle, standard wait sıfır. Yaklaşık 20 saniyede 140,6 ms process CPU (%0,70/çekirdek). Kanıt: `artifacts/owner-audit/timing.txt`.

Desteklenen maksimum **500 CPS** korunur; UI/validation/migration/diagnostic aynı sınırı kullanır. Scheduler ve SendInput/CGEvent sistemi değiştirilmedi. Kullanıcının önceki 500 CPS native ölçümü bağlamdır; bu oturumda yeni gerçek click gönderilmedi. UI actual CPS başarılı fiziksel click sayısının gerçek elapsed süresine göre hesaplanır; hedef değer değildir.

## 24. Son screenshot yolları

Son on görüntü yayımlanan EXE'nin gerçek native penceresinden 1000×800 istemci alanı render edilerek alındı ve tek tek açılıp incelendi. İlk incelemede boş İzinler başlığı ve İngilizce decimal biçimi bulundu, düzeltildi ve görüntüler yenilendi.

- [Türkçe Gece — Temel](screenshots/owner-audit/turkish-night-general.png)
- [Türkçe Gece — Gelişmiş](screenshots/owner-audit/turkish-night-advanced.png)
- [Türkçe Gece — Konum](screenshots/owner-audit/turkish-night-position.png)
- [Türkçe Gece — İstatistik](screenshots/owner-audit/turkish-night-statistics.png)
- [Türkçe Gece — Ayarlar](screenshots/owner-audit/turkish-night-settings.png)
- [Türkçe Kızıl Altın — Temel](screenshots/owner-audit/turkish-crimson-gold-general.png)
- [Türkçe Kızıl Altın — Ayarlar](screenshots/owner-audit/turkish-crimson-gold-settings.png)
- [English Graphite — General](screenshots/owner-audit/english-graphite-general.png)
- [Türkçe Açık — Temel](screenshots/owner-audit/turkish-light-general.png)
- [Hakkında](screenshots/owner-audit/about.png)

Responsive dosyaları `docs/screenshots/owner-audit/responsive/` altındadır. Yeniden üretim: `scripts/capture-owner-qa.ps1`, `scripts/capture-responsive-qa.ps1`. PNG ölçüleri `artifacts/owner-audit/screenshot-dimensions.json` içinde saklanır.

## 25. Portable artifact

UI, düzeltmeler ve testler tamamlandıktan sonra resmi `scripts/publish-windows.ps1` yeniden çalıştırıldı. Son portable: `artifacts/release/windows/AutoClicker-1.0.0-win-x64-portable.zip`, **44.970.679 byte**, 225 ZIP girdisi. Yeni ZIP'i ayrı bir klasöre açıp `AutoClicker/AutoClicker.exe` çalıştırın. Self-contained win-x64, runtime 8.0.28; hedef bilgisayarda ayrıca .NET gerekmez. Önceki ZIP/source eşleşmesi final kabul edilmez.

## 26. Yeni SHA256

`def50ec81a66fa0c776cecd896850b4b375cc4165e3cd5de7d2ada8914ad208d`

ZIP'in yeniden hesaplanan hash'i `.zip.sha256` ve `release-manifest.json` ile aynı. İki dosya da `artifacts/release/windows/` içindedir.

## 27. Yayımlanan EXE smoke

Resmi betikte configuration load/render/shutdown smoke başarılı. Ek published native kontrol smoke **exit code 0**, rapor `passed=true`, `trayChecked=true`. Gerçek kontrollü navigation/dil/status/sekme/tray assertion'ları geçti. EXE process'inin bitmesi Start-Process -Wait ile açıkça beklenir; eski LASTEXITCODE değerine dayanılmaz. Kanıt: `artifacts/owner-audit/published-final-controls.json`.

## 28. Installer

Configured: `packaging/windows/AutoClicker.iss`, per-user kurulum, startup/upgrade/uninstall kuralları mevcut. Generated: **hayır**. `ISCC.exe` PATH'te ve `C:\Program Files (x86)\Inno Setup 6\ISCC.exe` konumunda bulunmadı. Eksik bağımlılık **Inno Setup 6 / ISCC.exe**. Installer kurulumu/yükseltmesi/kaldırılması manuel doğrulanmadı.

## 29. macOS

Configured: CGEvent/event tap/permission akışı, `com.mertpural.autoclicker` bundle kimliği, Info.plist, ICNS/.app/DMG/signing/notarization betikleri. Mevcut `~/Library/LaunchAgents/com.autoclicker.app.plist` ve label değiştirilmedi. Platform managed projesi Windows solution build'inde derlendi; native API fake testleri geçti. İki bash betiği syntax kontrolünü geçti; publish betiği Windows host guard ile artifact üretmeden çıktı.

Generated: bu oturumda **ICNS, .app, DMG, imzalı/notarized Mac artifact yok**. Manually verified: **hayır**. Mac'te izin verme/retry, native click/hotkey/hold/Pick Position, menu bar, startup, single instance, DMG kurulum ve gerekiyorsa codesign/Gatekeeper/notarization yapılmalıdır. Windows üzerindeki managed build, Mac native doğrulaması olarak sunulmaz.

## 30. Kalan gerçek Windows kontrolleri

[RELEASE_QA_CHECKLIST.md](RELEASE_QA_CHECKLIST.md) içindeki manuel kutular boş bırakıldı. Yeni ZIP'ten açılan EXE'de OS mouse ile dil popup'ı, Settings/About → profil, drag threshold/flicker/outside/Escape ve inline rename/restart; DPI/çoklu monitör ve tooltip/focus state'leri denenmelidir. Güvenli hedefte düşük CPS, 50/100/200/500 native throughput, Toggle/Hold/global hotkeys/F12, tüm mouse-release yolları ve Pick Position kontrol edilmeli. Tray menüsüne insan tarafından tıklama, gizliyken ikinci EXE'nin pencereyi açması, startup aç/kapat ve gerçek oturum açma; istatistik/reset/restart ve profiles/colors/order kalıcılığı ayrıca denenmelidir. Startup native kayıt/logon ve gerçek mouse input bu oturumda otomatik olarak değiştirilmedi/üretilmedi.

## 31. Son karar

**Windows x64 portable: otomatik kapıları geçmiş sürüm adayı.** Dil hatası gerçek kontrolle yeniden üretilip düzeltildi; güncel binary kontrol smoke'u, UI görüntüleri, iki bellek turu, CPS ve 10× Release doğrulaması tamamlandı. İnsan tarafından yapılan Windows masaüstü kabul testi hâlâ gereklidir.

**Kamuya açık dağıtıma hazır: hayır.** LICENSE yoktur ve yeni lisans uydurulmadı; proje sahibi dağıtım lisansını seçmelidir. Installer ve macOS artifact/native doğrulaması tamamlanmış değildir.

## Önceki owner kapsamındaki diğer doğrulamalar

- **Profil sistemi:** oluşturma/kopyalama/silme, boş/duplicate rename reddi, Enter/focus-loss commit ve Escape cancel, renk/sıra/default kimliği ve JSON restart testleri geçti. Bozuk JSON kullanıcı dosyasını korur; invalid/cancelled/concurrent save testleri mevcut. Yeni profil sona eklenir.
- **Hold Mouse Button:** bir down/bir up, repeated release, button-change, başarısız down rollback ve başarısız up retry testleri; hotkey/emergency/profile/mode/shutdown güvenlik yolları mevcut. Dil değişimi basılı state'i bırakmaz. Click engine başlatılmaz ve salt hold tıklama istatistiğini artırmaz. Native basılı mouse testi yapılmadı.
- **Mouse Hold Trigger:** legacy davranış korundu. Own injected input göz ardı edilir; emergency/manual stop sonrası fiziksel release/re-press ile yeniden kurma testleri geçti.
- **İstatistik:** başarılı fiziksel click, double +2, failed +0; Running active time, başlangıç gecikmesinin dışlanması, delta sayımı, checkpoint/stop persistence, reset ve restart testleri mevcut/geçer. Click başına disk yazılmaz; global ve yereldir.
- **Tray:** Initialize guard, selected profile handler, language etiketleri ve dispose testleri/native smoke geçti. Open/Start/Stop/Quit callback'leri ve close/start minimized mevcut; OS menü interaksiyonu manuel kalır.
- **Single instance:** normal Windows kimliği bundle+user tabanlıdır; EXE kaynak yoluna bağlı değildir. Watcher üzerinden activation ve listener/shutdown testleri geçer. Smoke QA için açıkça seçilen ayrı data dizinleri mutex'i de izole eder; normal kimlik değiştirilmedi. Mac file lock configured/managed testlidir; gerçek Mac kontrolü bekler.
- **Startup:** Windows HKCU Run komutu path quoting ve `--startup` ile korunur. Framework-dependent geliştirme kaydı yanlış platform DLL'ini hedefliyordu; artık entry assembly kullanır. Self-contained EXE hedefi değişmez. Startup yalnız açar, otomatik click başlatmaz. Mac mevcut LaunchAgent kimliği korundu. Gerçek registry write/logon bu oturumda yapılmadı.
- **Branding:** canonical graphite mouse kaynağı, 1024 master ve ICO'nun üretim öncesi/sonrası hash'leri aynı; tasarım/recolor yok. ICO 16/24/32/48/64/128/256 frame içerir. EXE, pencere/sidebar/About/tray ve Inno config aynı kaynaklara bağlıdır; branding testleri geçti. Yeni EXE'den ikon çıkarıldı. Taskbar/Explorer cache ve Mac ICNS görünümü manuel kalır.
- **Yerelleştirme:** Türkçe/İngilizce kaynaklarda **354/354 anahtar**, eksik DynamicResource anahtarları için test eklendi. Eksik `Ui_Permissions` tamamlandı. Türkçe grup başlıkları ve doğal birimler korunur; raw CurrentCursor yerine çeviri, Mouse ile Tetikle/Mouse Tuşunu Basılı Tut/Tıklama Sayısı/Grup başına tıklama kullanılır. Kısa tooltip kaynakları mevcut ve kaynak eşliği testlidir. İngilizce enum serialization değişmez.
- **Güvenlik ve paket:** 107 authored metin dosyasındaki token/private-key örüntü taramasında bulgu yok; repo authored dosyalarında private certificate/key bulunmadı. Önceki PDB ve DLL debug kayıtlarında kişisel kaynak yolları bulundu. Paketlemede PDB çıkarıldı; Release PathMap ile PE/debug yolu da nötrleştirildi. Son ZIP'te kullanıcı settings/logları, PDB, private key/certificate yok; AutoClicker EXE/DLL'lerinde kişisel kullanıcı yolu taraması temiz. Bu sınırlı örüntü taraması tam güvenlik garantisi değildir. Uygulama network/analytics eklemedi; local engine telemetry dışarı gönderilmez. Kanıt: `security-source.json`, `security-release.json`.
- **Temizlik:** yalnız bu çalışma için indirilen dört geçici Avalonia kaynak dosyası artifacts/owner-audit içinden silindi. Kaynak, kullanıcı ayarları, eski yayın klasörleri ve ölçüm kanıtları geniş kapsamda silinmedi. QA data/logları artifacts altında ayrı dizinlerde tutulur ve ZIP'e dahil değildir.

Release PathMap davranışı [resmi .NET compiler belgesinden](https://github.com/dotnet/docs/blob/main/docs/csharp/language-reference/compiler-options/advanced.md) doğrulandı. Release için kullanılır; yerel Debug breakpoint yolları korunur.
