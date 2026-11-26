[English](README.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [中文](README.zh.md) | **Español** | [Italiano](README.it.md) | [Polski](README.pl.md) | [Português (BR)](README.pt-BR.md) | [हिन्दी](README.hi.md)

# Americas Launcher

Un lanzador moderno para Windows que configura, actualiza, compila y ejecuta automáticamente capas de compatibilidad de Xbox Series X|S (GDK) en Windows, con seguimiento en vivo del progreso del port.

> Este proyecto está destinado **únicamente a juegos que poseas legalmente**, descomprimidos en tu propia PC. Americas Launcher **no** incluye, descarga ni distribuye juegos, claves ni archivos SDK de consola.

![Americas Launcher - Launch View](docs/images/launch-view.png)

## Características

- **Interfaz moderna 720p:** Interfaz oscura con 9 tarjetas de runners seleccionables.
- **Instalación / Actualización en un clic:** Verifica e instala dependencias faltantes con `winget` (Git, Visual Studio C++ Build Tools, Rust), clona y compila localmente cada runner.
- **Consola integrada:** La salida del runner y los registros de compilación de shaders/pipelines se muestran directamente en el lanzador.
- **Stop Game:** El botón de inicio cambia a **Stop Game** para detener el juego o la compilación en cualquier momento.
- **Ajustes por runner:** Opciones individuales de **Pipeline**, **Xbox Series X/S** y **Resolución** (`540p`, `720p`, `1080p`, `2K`, `4K`).
- **Estado del port en vivo:** Seguimiento en tiempo real con [Portwatch VI](https://portwatch-vi.xyz/).

## Runners compatibles

| Runner | Basado en | Mejor con | Repositorio |
|---|---|---|---|
| **XCL** | Capa de compatibilidad independiente Xbox Series | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Belabug - VIctory Based** | Fork de VIctory | AMD | [Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD) |
| **Dexnom - VIctory Based** | Fork de VIctory | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | Fork de VIctory | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | Capa de compatibilidad VIctory original | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | Fork de VIctory | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **AvatarManz - VIctory Based** | Fork de VIctory | AMD | [avatarmanz/VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) |
| **Nvidia - VIctory Based** | Fork de VIctory (basado en AvatarManz) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | Fork de VIctory | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## Uso

1. Abre **Settings** y selecciona **Game Folder** y **Game Executable** (`Game.exe`).
2. Ve a **Launch** y selecciona el runner que desees.
3. Haz clic en **Install / Update** para instalar dependencias y compilar.
4. Haz clic en **Launch Game**.

## Licencia

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
