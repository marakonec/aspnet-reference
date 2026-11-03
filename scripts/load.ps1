<#
.SYNOPSIS
    Серія прогонів навантажувача k6 для одного варіанта і порівняння двох серій.
    Перший прогін серії прогрівальний і в підсумок не входить. Кожен прогін
    записується рядком у файл CSV.
.EXAMPLE
    .\scripts\load.ps1 -Script load\read.js -Csv measurements\no-cache.csv
    .\scripts\load.ps1 -Script load\read.js -Csv measurements\cache.csv -Runs 5 -Vus 10 -Duration 15s
    .\scripts\load.ps1 -Compare measurements\no-cache.csv, measurements\cache.csv
#>
param(
    [string]$Script,
    [string]$Csv,
    [int]$Runs = 5,
    [int]$Vus = 10,
    [string]$Duration = '15s',
    [string[]]$Compare
)
. "$PSScriptRoot\_common.ps1"

$culture = [System.Globalization.CultureInfo]::InvariantCulture

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return $sorted[[int][Math]::Floor($n / 2)] }
    return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2
}

function Get-Series([string]$Path) {
    $rows = @(Import-Csv $Path | Where-Object { $_.warmup -eq 'false' })
    if ($rows.Count -eq 0) { throw "У файлі $Path немає зарахованих прогонів" }
    $values = @($rows | ForEach-Object { [double]::Parse($_.median_ms, $culture) })
    $median = Get-Median $values
    $range = ($values | Measure-Object -Maximum).Maximum - ($values | Measure-Object -Minimum).Minimum
    return [pscustomobject]@{ Path = $Path; Count = $values.Count; M = $median; R = $range }
}

if ($Compare) {
    if ($Compare.Count -ne 2) { throw 'Параметр -Compare приймає рівно два файли CSV' }
    $a = Get-Series $Compare[0]
    $b = Get-Series $Compare[1]
    foreach ($s in @($a, $b)) {
        Write-Host ([string]::Format($culture, '{0}: прогонів {1}, медіана M = {2:F2} мс, розмах R = {3:F2} мс', $s.Path, $s.Count, $s.M, $s.R))
    }
    $difference = [Math]::Abs($a.M - $b.M)
    $limit = [Math]::Max($a.R, $b.R)
    if ($difference -gt $limit) {
        Write-Mark -Status OK -Id 'compare' -Text ([string]::Format($culture, '|M(A) - M(B)| = {0:F2} мс більше за max(R(A), R(B)) = {1:F2} мс, варіанти відрізняються', $difference, $limit))
    } else {
        Write-Mark -Status INFO -Id 'compare' -Text ([string]::Format($culture, '|M(A) - M(B)| = {0:F2} мс не більше за max(R(A), R(B)) = {1:F2} мс, різницю не доведено', $difference, $limit))
    }
    exit 0
}

if (-not $Script -or -not $Csv) { throw 'Потрібні параметри -Script і -Csv або -Compare' }
if ($Runs -lt 2) { throw 'Серія має містити щонайменше два прогони, перший прогрівальний' }

$passport = Read-SelfCheck
$port = ([uri]$passport.baseUrl).Port
$scenario = Join-Path $script:RepoRoot $Script
if (-not (Test-Path $scenario)) { throw "Немає сценарію $scenario" }

try {
    Invoke-WebRequest -Uri ($passport.baseUrl.TrimEnd('/') + '/health/live') -UseBasicParsing -TimeoutSec 10 | Out-Null
} catch {
    Write-Mark -Status FAIL -Id 'k6' -Text "Застосунок не відповідає за адресою $($passport.baseUrl)"
    exit 1
}

$csvPath = Join-Path $script:RepoRoot $Csv
$csvDir = Split-Path -Parent $csvPath
if (-not (Test-Path $csvDir)) { New-Item -ItemType Directory -Path $csvDir | Out-Null }
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('k6-' + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $work | Out-Null
Copy-Item $scenario (Join-Path $work 'scenario.js')

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('run,warmup,vus,duration,requests,rps,median_ms,p95_ms,failed_rate')
Write-Host "Сценарій $Script, прогонів $Runs, віртуальних користувачів $Vus, тривалість $Duration" -ForegroundColor Cyan

try {
    for ($run = 1; $run -le $Runs; $run++) {
        $summary = Join-Path $work 'summary.json'
        if (Test-Path $summary) { Remove-Item $summary }
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        Push-Location $script:RepoRoot
        & docker compose run --rm -v "${work}:/work" k6 run --quiet --vus $Vus --duration $Duration `
            -e "BASE_URL=http://host.docker.internal:$port" --summary-export /work/summary.json /work/scenario.js 2>&1 | Out-Null
        Pop-Location
        $ErrorActionPreference = $previous
        if (-not (Test-Path $summary)) {
            Write-Mark -Status FAIL -Id "run $run" -Text 'Навантажувач не записав підсумок, перевірити Docker і сценарій'
            exit 1
        }
        $m = (Get-Content $summary -Raw | ConvertFrom-Json).metrics
        $requests = [int]$m.http_reqs.count
        $rps = [double]$m.http_reqs.rate
        $median = [double]$m.http_req_duration.med
        $p95 = [double]$m.http_req_duration.'p(95)'
        $failedRate = [double]$m.http_req_failed.value
        $row = [string]::Format($culture, '{0},{1},{2},{3},{4},{5:F1},{6:F2},{7:F2},{8:F4}',
            $run, ($run -eq 1).ToString().ToLowerInvariant(), $Vus, $Duration,
            $requests, $rps, $median, $p95, $failedRate)
        $lines.Add($row)
        $label = 'зараховано'
        if ($run -eq 1) { $label = 'прогрів' }
        Write-Host ([string]::Format($culture, 'прогін {0} ({1}): запитів {2}, медіана {3:F2} мс, p95 {4:F2} мс, частка відмов {5:P1}',
            $run, $label, $requests, $median, $p95, $failedRate))
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

[System.IO.File]::WriteAllLines($csvPath, $lines, (New-Object System.Text.UTF8Encoding $false))
$series = Get-Series $csvPath
Write-Mark -Status OK -Id 'k6' -Text ([string]::Format($culture, 'Серію записано у {0}, зарахованих прогонів {1}, M = {2:F2} мс, R = {3:F2} мс', $Csv, $series.Count, $series.M, $series.R))
