[English](README.md) | **Türkçe** | [Русский](README.ru.md) | [中文](README.zh.md) | [Español](README.es.md) | [Italiano](README.it.md) | [Polski](README.pl.md) | [Português (BR)](README.pt-BR.md) | [हिन्दी](README.hi.md)

# Americas Launcher

Windows üzerinde Xbox Series X|S (GDK) uyumluluk katmanlarını otomatik olarak kuran, güncelleyen, derleyen ve başlatan modern bir Windows başlatıcısıdır — dahili canlı port ilerleme takibi içerir.

> Bu proje **yalnızca yasal olarak sahip olduğunuz**, kendi PC'nize çıkarılmış oyunlar içindir. Americas Launcher oyun, anahtar veya konsol SDK dosyalarını **içermez, indirmez veya dağıtmaz**.

![Americas Launcher - Launch View](docs/images/launch-view.png)

## Özellikler

- **Modern 720p Arayüz:** Özel pencere kontrollerine ve 9 seçilebilir runner kartına sahip koyu lacivert & patriot temalı arayüz.
- **Tek Tıkla Kurulum / Güncelleme:**
  - Eksik bağımlılıkları `winget` üzerinden otomatik kontrol edip kurar (Git, Visual Studio C++ Build Tools, Rust).
  - Her runner'ın kaynak reposunu `git` ile klonlar, günceller ve yerel olarak derler.
- **Entegre Konsol Çıktısı:** Runner çıktıları ve ön-başlatma shader/pipeline derleme logları doğrudan launcher içindeki log kutusuna akar.
- **Stop Game:** Başlat butonuna bastıktan sonra buton **Stop Game**'e dönüşür; pipeline derlemesi sırasında bile tıklayarak işlemi ve arka plandaki runner sürecini durdurabilirsiniz.
- **Runner Ayarları:** Her runner için ayrı **Pipeline**, **Xbox Series X/S** ve **Çözünürlük** (`540p`, `720p`, `1080p`, `2K`, `4K`) ayarları.
- **Canlı Port Durumu:** [Portwatch VI](https://portwatch-vi.xyz/) üzerinden canlı ilerleme takibi.

## Desteklenen Runnerlar

| Runner | Taban | En İyi GPU | Repo |
|---|---|---|---|
| **XCL** | Bağımsız Xbox Series uyumluluk katmanı | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Belabug - VIctory Based** | VIctory fork | AMD | [Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD) |
| **Dexnom - VIctory Based** | VIctory fork | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | VIctory fork | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | Orijinal VIctory uyumluluk katmanı | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | VIctory fork | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **AvatarManz - VIctory Based** | VIctory fork | AMD | [avatarmanz/VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) |
| **Nvidia - VIctory Based** | VIctory fork (AvatarManz tabanlı) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | VIctory fork | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## Kullanım

1. Sol menüden **Settings**'i açın ve **Game Folder** ile **Game Executable** (`Game.exe`) yollarını seçin.
2. **Launch** sekmesine gidin ve kullanmak istediğiniz runner kartına tıklayın.
3. Bağımlılıkları kurmak ve runner'ı derlemek için **Install / Update** butonuna tıklayın.
4. **Launch Game** butonuna tıklayın. *(İlk kez shader/pipeline derlemesi birkaç dakika sürebilir.)*

## Lisans

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
