<#
.SYNOPSIS
    Кілька одночасних викликів ключової операції над одним ресурсом.
    Очікуваний стан після виправлення: один успіх, решта 409 в описі проблеми.
.EXAMPLE
    .\scripts\race.ps1 -Id res-2
    .\scripts\race.ps1 -Id res-2 -Parallel 4
#>
param(
    [Parameter(Mandatory = $true)] [string]$Id,
    [int]$Parallel = 2,
    [switch]$WithToken
)
. "$PSScriptRoot\_common.ps1"

$passport = Read-SelfCheck
if (-not $passport.keyOperation) { throw 'У selfcheck.json не названо keyOperation' }
$method = $passport.keyOperation.method
$path = [regex]::Replace($passport.keyOperation.path, '\{[^}/]+\}', $Id)
$uri = $passport.baseUrl.TrimEnd('/') + $path
$body = '{}'
if ($passport.keyOperationBody) { $body = ($passport.keyOperationBody | ConvertTo-Json -Compress -Depth 10) }

$token = $null
if ($WithToken) { $token = Get-PassportToken -Access $passport.access -User $passport.access.privilegedUser }

Write-Host "Одночасних викликів $Parallel, $method $uri" -ForegroundColor Cyan

$jobs = @()
for ($i = 0; $i -lt $Parallel; $i++) {
    $jobs += Start-Job -ScriptBlock {
        param($m, $u, $b, $t)
        $headers = @{}
        if ($t) { $headers['Authorization'] = "Bearer $t" }
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $r = Invoke-WebRequest -Method $m -Uri $u -Headers $headers -Body $b `
                -ContentType 'application/json' -UseBasicParsing -TimeoutSec 60
            $type = [string]::Join(',', @($r.Headers['Content-Type']))
            return '{0}|{1}|{2}' -f [int]$r.StatusCode, $sw.ElapsedMilliseconds, $type
        } catch {
            $code = 0; $type = ''
            $resp = $_.Exception.Response
            if ($resp) {
                $code = [int]$resp.StatusCode
                if ($resp.ContentType) { $type = $resp.ContentType }
                elseif ($resp.Content -and $resp.Content.Headers.ContentType) { $type = $resp.Content.Headers.ContentType.MediaType }
            }
            return '{0}|{1}|{2}' -f $code, $sw.ElapsedMilliseconds, $type
        }
    } -ArgumentList $method, $uri, $body, $token
}

$results = @($jobs | Wait-Job | Receive-Job)
$jobs | Remove-Job

$ok = 0; $conflicts = 0; $conflictsAsProblem = 0; $other = @()
foreach ($line in $results) {
    $parts = $line.Split('|')
    $code = [int]$parts[0]
    Write-Host ('{0,4} {1,6} мс  {2}' -f $code, $parts[1], $parts[2])
    if ($code -ge 200 -and $code -lt 300) { $ok++ }
    elseif ($code -eq 409) {
        $conflicts++
        if ($parts[2] -like 'application/problem+json*') { $conflictsAsProblem++ }
    }
    else { $other += $code }
}

Write-Host ''
if ($ok -eq 1 -and $conflicts -eq ($Parallel - 1) -and $conflictsAsProblem -eq $conflicts) {
    Write-Mark -Status OK -Id 'race' -Text "Один успіх, конфліктів $conflicts, усі в описі проблеми"
    exit 0
}
if ($ok -gt 1) {
    Write-Mark -Status INFO -Id 'race' -Text "Успішних викликів $ok із $Parallel, оновлення могло бути втрачено. Перевірити інваріант у базі"
} elseif ($conflicts -gt $conflictsAsProblem) {
    Write-Mark -Status FAIL -Id 'race' -Text 'Конфлікт повернуто не в описі проблеми (application/problem+json)'
} elseif ($ok -eq 0 -and $other.Count -eq 0) {
    Write-Mark -Status FAIL -Id 'race' -Text "Успіхів 0, конфліктів ${conflicts}. Ресурс $Id, імовірно, вже змінено попереднім викликом, дані треба повернути в початковий стан"
} else {
    Write-Mark -Status FAIL -Id 'race' -Text "Успіхів $ok, конфліктів $conflicts, інші коди: $($other -join ', ')"
}
exit 1
