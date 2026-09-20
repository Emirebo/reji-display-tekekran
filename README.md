# RejiDisplay — LED Output Manager & Presentation Switcher (v0.3)

RejiDisplay, canlı etkinlik ve reji operasyonlarında **Windows Extended Desktop (PowerPoint, PDF, Tarayıcı)** sunum akışlarını GPU hızlandırmalı olarak yakalayan ve birleşik **Master LED Tuvali (NovaStar VX2000 Pro vb.)** üzerine canlı yayınlayan yüksek performanslı bir C# / .NET 8 WPF uygulamasıdır.

---

## 📐 Mimari ve Ekran İlişkileri

Uygulama 3 temel ekran rolü üzerinden çalışır:

1. **Kontrol Ekranı (Operator UI / Display 1)**: Operatörün medya seçimi yaptığı, canlı önizlemeleri izlediği, Draft → TAKE geçişlerini ve LED kalibrasyonlarını yönettiği ana kontrol arayüzüdür.
2. **Sunum Ekranı (Presentation Source / Display 2)**: Windows Genişletilmiş Masaüstü üzerinde çalışan sunum kaynağıdır (PowerPoint, PDF okuyucu veya tarayıcı). Uygulama bu ekranı **WGC GPU (Windows.Graphics.Capture)** motoru ile düşük gecikmeli ve yüksek performanslı olarak yakalar.
3. **Master LED Çıkışı (Unified Output / Display 3)**: Fiziksel LED işlemcisine (örn. NovaStar VX2000 Pro) gönderilen **4301×1720** çözünürlüğündeki birleşik mantıksal LED tuval ekranıdır.

---

## 🖥️ Sistem Gereksinimleri

* **İşletim Sistemi**: Windows 10 (Build 19041+) veya Windows 11 x64 (WinRT Graphics Capture desteği için).
* **Target Framework**: `.NET 8.0` (`net8.0-windows10.0.19041.0`).
* **Runtime**: `.NET 8 Desktop Runtime (x64)`.
* **WebView2**: `Microsoft WebView2 Runtime` (`Microsoft.Web.WebView2` v1.0.4191.47).
* **Ekran Kartı**: Direct3D 11 ve WGC desteğine sahip GPU (NVIDIA, AMD veya Intel).

---

## 🚀 GitHub’dan Sıfırdan Kurulum

### 1. Bağımlılıkların Hazırlanması
Bilgisayarınızda aşağıdaki araçların kurulu olduğundan emin olun:
* [Git](https://git-scm.com/)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Microsoft WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### 2. Depoyu Klonlama
PowerShell veya Terminal açarak depoyu klonlayın:
```powershell
git clone https://github.com/Emirebo/reji-display-tekekran.git
cd reji-display-tekekran
```

### 3. Bağımlılıkları Geri Yükleme ve Derleme
```powershell
dotnet restore
dotnet build .\src\RejiDisplay\RejiDisplay.csproj --configuration Release
```

---

## ▶️ Uygulamayı Çalıştırma

Ana proje kök dizininden Release modunda uygulamayı başlatmak için:

```powershell
dotnet run --project .\src\RejiDisplay\RejiDisplay.csproj --configuration Release
```

---

## 🧪 Otomatik Testleri Çalıştırma

Projeye ait birim testlerini (59 adet doğrulama testi) çalıştırmak için:

```powershell
dotnet test --configuration Release
```

---

## 🖥️ Windows Ekran Yapılandırması

1. **Genişletilmiş Masaüstü Modu**:
   - Windows ayarlarında ekranları **"Bu ekranları genişlet"** moduna alın (`Win + P` → **Genişlet**).
   - Ekranların yinelenmediğinden (Duplicate) emin olun.

2. **Ekran Numaralandırma**:
   - Ekran isimleri (`Ekran 1`, `Ekran 2`, `Ekran 3`) Windows işletim sisteminin ekran takma sırasına göre değişebilir.
   - Uygulama içi header alanındaki **`🔍 EKRANLARI NUMARALANDIR`** butonuna basarak fiziksel ekranlarda 3.5 saniye görünen numaralandırma katmanıyla ekran kimliklerini teyit edin.
   - **Hedef Ekran** menüsünden Operatör ekranını, Sunum ekranını ve Master LED ekranını doğru monitörlerle eşleştirin.

---

## 🎨 LED Tuval Yerleşimi (Master Canvas Geometry)

Master LED Çıkış Penceresi toplam **4301 × 1720** çözünürlüğünde mantıksal bir tuvaldir:

* **Sol LED Alanı (Left Region)**: `860 × 1720` piksel (X: 0, Y: 0).
* **Orta Canlı Sunum (Middle Region)**: `2581 × 1376` piksel (X: 860, Y: 172 px varsayılan dikey merkezleme offseti ile).
* **Sağ LED Alanı (Right Region)**: `860 × 1720` piksel (X: 3441, Y: 0).

> 💡 **Not**: Bu çözünürlükler uygulama içerisindeki birleşik mantıksal tuval boyutlarıdır; NovaStar LED işlemcisindeki fiziksel kablolama ve piksel haritalamasına göre ayarlanır.

---

## 🎛️ İlk Kullanım ve İş Akışı

1. **Master Çıkışını Başlatma**:
   - `▶ MASTER YAYINI BAŞLAT` butonuna basarak Master penceresini fiziksel LED ekranında borderless fullscreen olarak açın.
2. **Sunum Kaynağını Yakalama**:
   - `Sunum Ekranı` açılır menüsünden yakalamak istediğiniz monitörü seçin. Orta alanda canlı sunum görüntüsü belirecektir.
3. **Sol ve Sağ Kanallara İçerik Atama**:
   - Sol ve Sağ LED alanları için **Görsel**, **Video**, **Web Sayfası (WebView2)** veya **Test Deseni** seçin.
4. **Draft (Taslak) → TAKE (Canlı) İş Akışı**:
   - Yapılan içerik ve yerleşim değişiklikleri önce **Draft (Taslak)** alanında önizlenir.
   - **`TAKE`** butonuna basıldığında taslak hazırlık doğrudan canlı **Master Çıkışına (Live)** aktarılır.
   - **Master Blackout (`⚫ SİYAH`)** butonu ile yayını kesmeden Master ekranını anında siyaha çekebilirsiniz.

---

## 📁 Log ve Yapılandırma Dosyaları

* **Teşhis Log Dosyası (`diagnostics.log`)**:
  - Konum: `%APPDATA%\RejiDisplay-TekEkran\diagnostics.log`
  - PowerShell ile canlı log takibi:
    ```powershell
    Get-Content "$env:APPDATA\RejiDisplay-TekEkran\diagnostics.log" -Tail 50
    ```
* **Kullanıcı Ayarları (`settings.json`)**:
  - Konum: `%APPDATA%\RejiDisplay-TekEkran\settings.json`

---

## ❓ Sorun Giderme (Troubleshooting)

* **Siyah Sunum Görüntüsü**:
  - Sunum kaynağı olarak seçilen ekranın simge durumuna küçültülmediğinden emin olun.
  - WGC (Windows.Graphics.Capture) motoru ekran kartı ve tazeleme hızına bağlı olarak 60 FPS'e kadar GPU yakalaması sağlar. GPU erişimi kaybolursa uygulama otomatik olarak DXGI / GDI yedek katmanına geçer.
* **Derleme Sırasında Dosya Kilidi Hatası (MSB3026 / RejiDisplay.exe kilitli)**:
  - Arka planda veya açık kalan bir `RejiDisplay.exe` süreci dosya kilidini tutuyor olabilir. `dotnet run` veya `dotnet build` yapmadan önce açık olan uygulamayı kapatın.
* **Master Penceresi Yanlış Monitörde Açılıyor**:
  - `🔍 EKRANLARI NUMARALANDIR` butonunu kullanarak `Hedef Ekran` seçimini kontrol edin.
* **WebView2 Sayfası Yüklenmiyor**:
  - Bilgisayarda *Microsoft Edge WebView2 Runtime* kurulu olduğunu kontrol edin.
