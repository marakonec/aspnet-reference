# Еталонний проєкт дисципліни «ASP.NET технології»

Предметна область еталона «Прокат вимірювальних приладів». Ключова операція `POST /reservations/{id}/issue` видає прилади за бронюванням. Паспорт предметної області лежить у `docs/DOMAIN.md`, каталог предметних областей для лабораторних робіт у `docs/DOMAINS.md`.

Тег `stage-1-v2026` містить код для лабораторної роботи 1, тег `stage-4-v2026` код для лабораторних робіт 2–4. Різницю між ними показує команда `git diff stage-1-v2026 stage-4-v2026`.

## Що потрібно

Пакет розробника .NET 10 (лінійка 10.0.4xx), Docker Desktop, Git. Версію пакета розробника закріплено у `global.json`.

## Запуск

Команди виконуються з кореня репозиторію в Windows PowerShell.

```powershell
Copy-Item .env.example .env
dotnet run --project tools\SelfCheck -- --preflight
docker compose --profile dash --profile db --profile dep --profile idp up -d --wait
dotnet run --project src\App.Api
```

| Служба композиції | Профіль | Адреса |
|---|---|---|
| дашборд телеметрії | `dash` | `http://localhost:18888` |
| PostgreSQL | `db` | `localhost:15200` |
| заглушка служби сповіщень | `dep` | `http://localhost:15110` |
| Keycloak | `idp` | `http://localhost:15300` |

Застосунок слухає `http://localhost:15100`. Документ контракту `http://localhost:15100/openapi/v1.json`, інтерфейс до нього `http://localhost:15100/scalar`. Профіль запуску `App.Api (memory)` працює без бази.

Зупинити контейнери. Дані бази видаляються разом із контейнером.

```powershell
docker compose --profile "*" down
```

## Самоперевірка

Самоперевірка виконується в другому вікні, поки застосунок працює. Паролі тестових користувачів Keycloak передаються змінними середовища.

```powershell
$env:SELFCHECK_OPERATOR_PASSWORD = 'operator1'
$env:SELFCHECK_CUSTOMER_PASSWORD = 'customer1'
dotnet run --project tools\SelfCheck -- --stage 4
```

Налаштування самоперевірки лежать у `selfcheck.json`.

## Контракт

```powershell
Invoke-WebRequest http://localhost:15100/openapi/v1.json -OutFile contract\openapi.current.json -UseBasicParsing
docker compose run --rm spectral
docker compose run --rm oasdiff
```

## Скрипти

| Скрипт | Що робить |
|---|---|
| `scripts\race.ps1 -Id res-2 -Parallel 4` | кілька одночасних викликів ключової операції |
| `scripts\load.ps1 -Script load\read.js -Csv measurements\a.csv` | серія прогонів k6 із записом у CSV, перший прогін прогрівальний |
| `scripts\load.ps1 -Compare measurements\a.csv, measurements\b.csv` | порівняння двох серій |
| `scripts\negative.ps1` | зіпсовані токени і доступ до чужого ресурсу |

Заглушка служби сповіщень імітує збої через `PUT /_fault`.

```powershell
Invoke-RestMethod -Method Put -Uri http://localhost:15110/_fault -ContentType 'application/json' -Body '{"delayMs":0,"failRate":1}'
```

## Тести

```powershell
dotnet test tests\App.Tests.InMemory
dotnet test tests\App.Tests.Containers
```

Другий набір запускає PostgreSQL у контейнері і потребує Docker.

## Розгортання

Конвеєр `.github/workflows/ci.yml` публікує образ у GitHub Container Registry з основної гілки. Розгортання з реєстру описано файлом `compose.deploy.yaml`, тег образу задає змінна `APP_IMG_APP` у файлі `.env`.

```powershell
docker compose -f compose.deploy.yaml up -d --wait
```

## Структура

| Тека | Що в ній |
|---|---|
| `src/App.Api` | застосунок |
| `src/App.Notifier` | заглушка зовнішньої служби сповіщень |
| `src/App.Shared` | імена подій логу, перемикач сховища |
| `tests` | набір тестів у пам'яті і набір із контейнером бази |
| `tools/SelfCheck` | самоперевірка |
| `scripts`, `load` | скрипти вимірювань і перевірок, сценарій k6 |
| `contract` | збережений контракт, правила лінтера |
| `infra/keycloak` | файли областей Keycloak |
| `docs` | паспорт предметної області, ADR, чекліст OWASP, атрибуція, бланки в `docs/templates` |

## Де живуть версії

| Файл | Що в ньому |
|---|---|
| `global.json` | версія пакета розробника |
| `Directory.Build.props` | цільова платформа і налаштування складання |
| `Directory.Packages.props` | версії пакетів NuGet |
| `dotnet-tools.json` | версія `dotnet-ef` |
| `.env.example` | теги образів контейнерів і порти |
| `src/*/Dockerfile` | базові образи .NET |
