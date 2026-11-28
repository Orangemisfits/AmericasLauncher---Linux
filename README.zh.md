[English](README.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | **中文** | [Español](README.es.md) | [Italiano](README.it.md) | [Polski](README.pl.md) | [Português (BR)](README.pt-BR.md) | [हिन्दी](README.hi.md)

# Americas Launcher

一个现代化的 Windows 启动器，可在 Windows 上自动配置、更新、编译和启动 Xbox Series X|S (GDK) 兼容层，并内置实时移植进度追踪功能。

> 本项目**仅适用于您合法拥有**且已解压到自己电脑上的游戏。Americas Launcher **不**包含、下载或分发任何游戏、密钥或主机 SDK 文件。

![Americas Launcher - Launch View](docs/images/launch-view.png)

## 功能特性

- **现代 720p 界面：** 深色界面，支持 9 个可选 Runner 卡片。
- **一键安装 / 更新：** 通过 `winget` 自动检查并安装缺失的依赖（Git、Visual Studio C++ Build Tools、Rust），克隆并本地编译各个 Runner。
- **集成控制台输出：** Runner 输出和启动前着色器/管线编译日志直接显示在启动器日志框中。
- **Stop Game：** 点击启动后按钮变为 **Stop Game**，可随时停止游戏或管线编译进程。
- **Runner 设置：** 为每个 Runner 单独配置 **Pipeline**、**Xbox Series X/S** 和 **分辨率**（`540p`、`720p`、`1080p`、`2K`、`4K`）。
- **实时移植状态：** 由 [Portwatch VI](https://portwatch-vi.xyz/) 提供实时进度追踪。

## 支持的 Runner

| Runner | 基于 | 最佳适配 | 仓库 |
|---|---|---|---|
| **XCL** | 独立 Xbox Series 兼容层 | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Dexnom - VIctory Based** | VIctory 分支 | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | VIctory 分支 | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | 原版 VIctory 兼容层 | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | VIctory 分支 | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **Nvidia - VIctory Based** | VIctory 分支（基于 AvatarManz） | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | VIctory 分支 | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## 使用方法

1. 打开 **Settings**，选择 **Game Folder** 和 **Game Executable** (`Game.exe`)。
2. 转到 **Launch** 页面并点击想要使用的 Runner 卡片。
3. 点击 **Install / Update** 安装依赖并编译 Runner。
4. 点击 **Launch Game** 启动游戏。

## 许可证

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
