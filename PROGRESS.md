# Прогресс текущего запроса

Запрос: всё бесплатно, карта 8×8 км, гонки по маршрутам, LAN-онлайн (свободная езда и гонки), сборки APK и IPA.

| # | Задача | Статус |
|---|---|---|
| 1 | Задача на 20:51 «продолжить» | done |
| 2 | Тюнинг бесплатный, деньги убраны | done |
| 3 | Карта 8×8 км: рельеф, дороги, города, подгрузка | done (код) |
| 4 | Гонки по маршрутам (спринт, круг), чекпоинты, боты | done (код) |
| 5 | LAN-онлайн: хост/поиск, свободная езда, гонки (только LAN, интернет-онлайн не нужен) | done (код) |
| 6 | Скрипты сборки APK, workflow GitHub Actions для IPA | done (Assets/Editor/BuildScript.cs, .github/workflows/build.yml) |
| 7 | Попытка собрать APK и IPA | APK DONE: D:\game\Builds\Android\OpenRoad.apk (36,9 МБ, 15.11). Модуль Android ставился через Unity CLI (D:\Tools\UnityCLI\bin\unity.exe install-modules --editor-version 6000.3.28f1 -m android --cm --accept-eula). IPA не собран: нужен Mac / GitHub, пользователь отказался от репозитория. Старая заметка: | ставится модуль Android (`Unity Hub -- --headless install-modules --version 6000.3.28f1 -m android --cm`), потом `BuildScript.BuildAndroid`. IPA — только через GitHub Actions (нужен репозиторий пользователя) |
| 8 | Проверка кода, README, тесты | done: проект компилируется без ошибок, 19/19 EditMode-тестов прошли |

- Лицензия: Hub установлен как MSIX и хранит лицензию в `%LOCALAPPDATA%\Packages\UnityTechnologies.UnityHub_2vrhnee42bhxm\LocalCache\Local\Unity\licenses`. Её скопировали в `%LOCALAPPDATA%\Unity\licenses`, иначе редактор её не видит.

## Заметки
- Unity Hub установлен (MSIX): `C:\Program Files\WindowsApps\UnityTechnologies.UnityHub_3.22.6.65535_x64__2vrhnee42bhxm\app\Unity Hub.exe`.
  CLI: `"Unity Hub.exe" -- --headless install --version 6000.3.28f1 -m android -m ios --cm` (запущено в фоне 15.11 ~18:20).
- Для сборки нужна лицензия: пользователь должен войти в Unity Hub (Personal). Пароли не вводить.
- Архитектура карты: генерация в рантайме из seed (MapLayout), тайлы 1×1 км подгружаются вокруг игрока (WorldStreamer).
- Сеть: свой LAN-протокол на .NET сокетах (TCP 47800 надёжные сообщения, UDP 47801 состояния машин, UDP 47777 broadcast-поиск). На iOS поиск может не работать без multicast entitlement — есть ручной ввод IP.

- 15.11 ~19:00: проект перенесён в D:\game, редактор Unity — в D:\Unity\Editors\6000.3.28f1 (зарегистрирован в Hub, Hub ставит редакторы на D). Модуль Android нужно поставить заново: на C кончилось место.

