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
docker compose --profile dash up -d --wait
dotnet run --project src\App.Api
```

Застосунок слухає `http://localhost:15100`. Документ контракту `http://localhost:15100/openapi/v1.json`, інтерфейс до нього `http://localhost:15100/scalar`, дашборд телеметрії `http://localhost:18888`.

Самоперевірка виконується в другому вікні, поки застосунок працює.

```powershell
dotnet run --project tools\SelfCheck -- --stage 1
```

Зупинити контейнери.

```powershell
docker compose --profile dash down
```

## Контракт

Документ контракту береться із запущеного застосунку, перевіряється лінтером і порівнюється зі збереженою версією `contract/openapi.baseline.json`.

```powershell
Invoke-WebRequest http://localhost:15100/openapi/v1.json -OutFile contract\openapi.current.json -UseBasicParsing
docker compose run --rm spectral
docker compose run --rm oasdiff
```

Правила лінтера лежать у `contract/.spectral.yaml`. Файл `contract/openapi.broken.json` порушує кожне з трьох правил курсу, на ньому видно, як спрацьовує кожне.

```powershell
docker compose run --rm spectral lint --ruleset .spectral.yaml openapi.broken.json
```

## Структура

| Тека | Що в ній |
|---|---|
| `src/App.Api` | застосунок |
| `src/App.Shared` | імена подій логу |
| `tools/SelfCheck` | самоперевірка, її налаштування в `selfcheck.json` |
| `contract` | збережений контракт, правила лінтера |
| `docs` | паспорт предметної області, ADR, атрибуція, бланки в `docs/templates` |

## Де живуть версії

| Файл | Що в ньому |
|---|---|
| `global.json` | версія пакета розробника |
| `Directory.Build.props` | цільова платформа і налаштування збирання |
| `Directory.Packages.props` | версії пакетів NuGet |
| `.env.example` | теги образів контейнерів і порти |
