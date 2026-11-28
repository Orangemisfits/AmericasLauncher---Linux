[English](README.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [中文](README.zh.md) | [Español](README.es.md) | **Italiano** | [Polski](README.pl.md) | [Português (BR)](README.pt-BR.md) | [हिन्दी](README.hi.md)

# Americas Launcher

Un launcher moderno per Windows che configura, aggiorna, compila e avvia automaticamente i livelli di compatibilità Xbox Series X|S (GDK) su Windows, con monitoraggio in tempo reale dei progressi del port.

> Questo progetto è destinato **solo ai giochi che possiedi legalmente**, estratti sul tuo PC. Americas Launcher **non** include, scarica o distribuisce giochi, chiavi o file SDK della console.

![Americas Launcher - Launch View](docs/images/launch-view.png)

## Funzionalità

- **Interfaccia moderna 720p:** Interfaccia scura con 9 schede runner selezionabili.
- **Installazione / Aggiornamento con un clic:** Controlla e installa le dipendenze mancanti tramite `winget` (Git, Visual Studio C++ Build Tools, Rust), clona e compila localmente ogni runner.
- **Console integrata:** L'output del runner e i log di compilazione shader/pipeline vengono mostrati direttamente nel launcher.
- **Stop Game:** Il pulsante di avvio diventa **Stop Game** per fermare il gioco o la compilazione in qualsiasi momento.
- **Impostazioni per runner:** Opzioni dedicate di **Pipeline**, **Xbox Series X/S** e **Risoluzione** (`540p`, `720p`, `1080p`, `2K`, `4K`) per ogni runner.
- **Stato del port in tempo reale:** Monitoraggio tramite [Portwatch VI](https://portwatch-vi.xyz/).

## Runner supportati

| Runner | Basato su | Ideale per | Repository |
|---|---|---|---|
| **XCL** | Livello di compatibilità Xbox Series indipendente | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Dexnom - VIctory Based** | Fork di VIctory | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | Fork di VIctory | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | Livello di compatibilità VIctory originale | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | Fork di VIctory | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **Nvidia - VIctory Based** | Fork di VIctory (basato su AvatarManz) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | Fork di VIctory | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## Utilizzo

1. Apri **Settings** e seleziona **Game Folder** e **Game Executable** (`Game.exe`).
2. Vai su **Launch** e seleziona il runner desiderato.
3. Clicca su **Install / Update** per installare le dipendenze e compilare.
4. Clicca su **Launch Game**.

## Licenza

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
