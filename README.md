# winstall

Простой менеджер пакетов winget в духе MInstall. Один exe, одно окно, один список —
никаких вкладок, анимаций и лишних рантаймов.

> **English:** a minimal MInstall-style GUI for winget. Single exe, single window,
> single list — no tabs, no animations, no extra runtimes.

![icon](icon.png)

## Возможности

- Один список вместо страниц: установленные программы, обновляемые — наверху жирным.
- Динамическая галочка: у обновляемого — **обновить** (↑), у свежего — **удалить** (×),
  у найденного в поиске — **установить** (+). Колонка `✦` сразу показывает действие.
- Поиск по всей базе winget прямо из верхней строки; категории:
  `УСТАНОВЛЕННЫЕ` / `ТРЕБУЮТ ОБНОВЛЕНИЯ` / `ПОИСК` (переключается сам).
- Нативные имена и иконки из реестра вместо голых ID (`Steam`, а не `Valve.Steam`).
- Карточка программы: издатель и описание из `winget show`.
- `Обновить всё` в один клик, экспорт/импорт списка (`winget export/import`) через меню ☰.
- Переустановка в один шаг, если winget отказался обновлять из-за смены
  технологии установщика (код `0x8A15008E`, проверено на LLVM).
- Автопроверка обновлений без службы и без админа: автозапуск при входе +
  задача на пробуждение, баллун «Доступно обновлений: N» без списка программ.
- Локализация через `locales/*.json` (в базе русский и английский),
  новый язык — просто положить файл, выбор в меню ☰.

Целевая платформа: **Windows 10 IoT Enterprise LTSC 21H2**, работает из коробки
на встроенном .NET Framework 4.8.

## Быстрый старт

Файлы лежат в [Releases](https://github.com/Yanletov168/winstall-app/releases)
(исходники каждого релиза GitHub прикладывает сам: `Source code (zip)`).

| Файл | Для кого |
|---|---|
| `Setup-winstall-x64.exe` | Установщик, Windows 10/11 64-bit |
| `Setup-winstall-x86.exe` | Установщик, Windows 32-bit |
| `Setup-winstall-arm64.exe` | Установщик, Windows ARM64 (нативно) |
| `winstall-portable-net48.zip` | Portable для LTSC без рантаймов (~100 КБ: exe + переводы) |
| `winstall-portable-win-x64.zip` | Portable .NET 8 self-contained, 64-bit |
| `winstall-portable-win-x86.zip` | Portable .NET 8 self-contained, 32-bit |
| `winstall-portable-win-arm64.zip` | Portable .NET 8 self-contained, ARM64 |

Установщики per-user (без UAC): кладут программу, ярлык в меню Пуск,
деинсталлятор в «Программы и компоненты». Нативного ARM64 у .NET Framework 4.8
не бывает — ARM64-сборки едут на .NET 8 self-contained (рантайм внутри,
ставить ничего не надо).

> На IoT LTSC нет Store и winget по умолчанию: скачай `.msixbundle` со страницы
> [winget-cli/releases](https://github.com/microsoft/winget-cli/releases)
> и поставь через `Add-AppxPackage`.

## Сборка из исходников

Нужен .NET SDK 8+ (ставить рантайм на целевую машину **не** надо —
программа едет на встроенном .NET Framework 4.8):

```bat
dotnet build winstall.csproj -c Release
```

Готовый exe: `bin\Release\net48\winstall.exe` (~70 КБ, иконка вшита).
Рядом должны лежать `locales\en.json` и `locales\ru.json`
(копируются в выходной каталог автоматически).

Тесты (парсинг вывода winget, локализации, цикл задачи планировщика):

```bat
dotnet run --project tests\parsetest\parsetest.csproj
```

## Использование

- Галочка у обновляемого = обновить, у свежего = удалить (с подтверждением),
  у нового из поиска = установить. Затем `▶ Выполнить`.
- `Обновить всё (N)` — один вызов `winget upgrade --all --silent`.
- Меню ☰: экспорт/импорт списка, выбор обновляемых, снятие галочек,
  автопроверка, язык, логи winget.
- Тихий режим для планировщика: `winstall.exe --check-updates`.

## Структура

| Файл | Что делает |
|---|---|
| `Program.cs` | Точка входа, тихий режим `--check-updates` |
| `MainForm.cs` | Весь UI в коде (стиль MInstall, Win32) |
| `WingetRunner.cs` | Запуск `winget.exe` в UTF-8, парсинг таблиц |
| `IconProvider.cs` | Иконки из реестра Uninstall |
| `AutoCheck.cs` | Автозапуск (Run) + задача на пробуждение (ONEVENT) + баллун |
| `Localization.cs` | Локализация через JSON рядом с exe |
| `Models.cs` | `PackageInfo`, логика действия галочки |
| `locales/` | `en.json`, `ru.json` |
| `tests/parsetest/` | Самопроверки (линкуют исходники, без копий) |
| `icon.png` / `icon.ico` | Иконка (исходник + собранная для exe) |

## Лицензия

MIT — см. [LICENSE](LICENSE).
