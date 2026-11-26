[English](README.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [中文](README.zh.md) | [Español](README.es.md) | [Italiano](README.it.md) | **Polski** | [Português (BR)](README.pt-BR.md) | [हिन्दी](README.hi.md)

# Americas Launcher

Nowoczesny launcher dla systemu Windows, który automatycznie konfiguruje, aktualizuje, kompiluje i uruchamia warstwy zgodności Xbox Series X|S (GDK) na Windowsie — z wbudowanym śledzeniem postępu portu na żywo.

> Ten projekt jest przeznaczony **wyłącznie do gier, które legalnie posiadasz**, rozpakowanych na własnym komputerze. Americas Launcher **nie** zawiera, nie pobiera ani nie rozpowszechnia gier, kluczy ani plików SDK konsoli.

![Americas Launcher - Launch View](docs/images/launch-view.png)

## Funkcje

- **Nowoczesny interfejs 720p:** Ciemny interfejs z 9 kartami runnerów do wyboru.
- **Instalacja / Aktualizacja jednym kliknięciem:** Automatyczne sprawdzanie i instalacja brakujących zależności przez `winget` (Git, Visual Studio C++ Build Tools, Rust) oraz lokalna kompilacja runnerów.
- **Zintegrowana konsola:** Wyjście runnera i logi kompilacji shaderów/pipeline są wyświetlane bezpośrednio w oknie launchera.
- **Stop Game:** Przycisk uruchamiania zmienia się w **Stop Game**, pozwalając zatrzymać grę lub kompilację w dowolnym momencie.
- **Ustawienia runnera:** Osobne opcje **Pipeline**, **Xbox Series X/S** oraz **Rozdzielczości** (`540p`, `720p`, `1080p`, `2K`, `4K`) dla każdego runnera.
- **Status portu na żywo:** Śledzenie postępu przez [Portwatch VI](https://portwatch-vi.xyz/).

## Obsługiwane runnery

| Runner | Baza | Najlepszy dla | Repozytorium |
|---|---|---|---|
| **XCL** | Niezależna warstwa zgodności Xbox Series | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Belabug - VIctory Based** | Fork VIctory | AMD | [Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD) |
| **Dexnom - VIctory Based** | Fork VIctory | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | Fork VIctory | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | Oryginalna warstwa zgodności VIctory | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | Fork VIctory | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **AvatarManz - VIctory Based** | Fork VIctory | AMD | [avatarmanz/VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) |
| **Nvidia - VIctory Based** | Fork VIctory (oparty na AvatarManz) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | Fork VIctory | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## Użycie

1. Otwórz **Settings** i wybierz **Game Folder** oraz **Game Executable** (`Game.exe`).
2. Przejdź do **Launch** i wybierz kartę runnera.
3. Kliknij **Install / Update**, aby zainstalować zależności i skompilować runnera.
4. Kliknij **Launch Game**.

## Licencja

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
