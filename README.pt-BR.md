[English](README.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [中文](README.zh.md) | [Español](README.es.md) | [Italiano](README.it.md) | [Polski](README.pl.md) | **Português (BR)** | [हिन्दी](README.hi.md)

# Americas Launcher

Um launcher moderno para Windows que configura, atualiza, compila e executa automaticamente camadas de compatibilidade do Xbox Series X|S (GDK) no Windows — com acompanhamento ao vivo do progresso do port.

> Este projeto destina-se **apenas a jogos que você possui legalmente**, extraídos no seu próprio PC. O Americas Launcher **não** inclui, baixa ou distribui jogos, chaves ou arquivos SDK de console.

![Americas Launcher - Launch View](docs/images/launch-view.png)

## Recursos

- **Interface moderna 720p:** Visual escuro com 9 cartões de runners selecionáveis.
- **Instalação / Atualização em um clique:** Verifica e instala dependências via `winget` (Git, Visual Studio C++ Build Tools, Rust), clona e compila localmente cada runner.
- **Console integrado:** A saída do runner e os logs de compilação de shaders/pipelines aparecem diretamente no launcher.
- **Stop Game:** O botão de iniciar vira **Stop Game** para encerrar o jogo ou a compilação a qualquer momento.
- **Configurações por runner:** Opções individuais de **Pipeline**, **Xbox Series X/S** e **Resolução** (`540p`, `720p`, `1080p`, `2K`, `4K`).
- **Status do port ao vivo:** Acompanhamento em tempo real pelo [Portwatch VI](https://portwatch-vi.xyz/).

## Runners suportados

| Runner | Baseado em | Melhor com | Repositório |
|---|---|---|---|
| **XCL** | Camada de compatibilidade Xbox Series independente | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Belabug - VIctory Based** | Fork do VIctory | AMD | [Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD) |
| **Dexnom - VIctory Based** | Fork do VIctory | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | Fork do VIctory | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | Camada de compatibilidade VIctory original | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | Fork do VIctory | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **AvatarManz - VIctory Based** | Fork do VIctory | AMD | [avatarmanz/VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) |
| **Nvidia - VIctory Based** | Fork do VIctory (baseado no AvatarManz) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | Fork do VIctory | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## Como usar

1. Abra **Settings** e selecione **Game Folder** e **Game Executable** (`Game.exe`).
2. Vá para **Launch** e clique no runner desejado.
3. Clique em **Install / Update** para instalar as dependências e compilar.
4. Clique em **Launch Game**.

## Licença

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
