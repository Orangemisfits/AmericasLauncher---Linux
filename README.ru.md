[English](README.md) | [Türkçe](README.tr.md) | **Русский** | [中文](README.zh.md) | [Español](README.es.md) | [Italiano](README.it.md) | [Polski](README.pl.md) | [Português (BR)](README.pt-BR.md) | [हिन्दी](README.hi.md)

# Americas Launcher

Современный лаунчер для Windows, который автоматически устанавливает, обновляет, собирает и запускает слои совместимости Xbox Series X|S (GDK) на Windows — со встроенным отслеживанием прогресса порта в реальном времени.

> Этот проект предназначен **только для игр, которыми вы владеете на законных основаниях**, распакованных на вашем ПК. Americas Launcher **не** включает, не скачивает и не распространяет игры, ключи или файлы SDK консоли.

![Americas Launcher - Launch View](docs/images/launch-view.png)

## Возможности

- **Современный интерфейс 720p:** Тёмный интерфейс с 9 выбираемыми карточками раннеров.
- **Установка и обновление в один клик:** Автоматическая проверка зависимостей через `winget` (Git, Visual Studio C++ Build Tools, Rust), клонирование и локальная сборка раннеров.
- **Встроенная консоль:** Вывод раннера и логи компиляции шейдеров/пайплайнов отображаются прямо в окне лаунчера.
- **Stop Game:** Кнопка запуска превращается в **Stop Game**, позволяя остановить игру или процесс компиляции в любой момент.
- **Настройки раннера:** Отдельные настройки **Pipeline**, **Xbox Series X/S** и **Разрешения** (`540p`, `720p`, `1080p`, `2K`, `4K`) для каждого раннера.
- **Статус порта:** Отслеживание прогресса в реальном времени через [Portwatch VI](https://portwatch-vi.xyz/).

## Поддерживаемые раннеры

| Раннер | Основа | Лучше для | Репозиторий |
|---|---|---|---|
| **XCL** | Независимый слой совместимости Xbox Series | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Dexnom - VIctory Based** | Форк VIctory | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | Форк VIctory | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | Оригинальный слой совместимости VIctory | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | Форк VIctory | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **Nvidia - VIctory Based** | Форк VIctory (на базе AvatarManz) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | Форк VIctory | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## Использование

1. Откройте **Settings** и выберите **Game Folder** и **Game Executable** (`Game.exe`).
2. Перейдите во вкладку **Launch** и выберите нужный раннер.
3. Нажмите **Install / Update** для установки зависимостей и сборки.
4. Нажмите **Launch Game**.

## Лицензия

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
