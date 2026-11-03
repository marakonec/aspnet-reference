$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Read-EnvFile {
    $result = @{}
    $path = Join-Path $script:RepoRoot '.env'
    if (-not (Test-Path $path)) { $path = Join-Path $script:RepoRoot '.env.example' }
    foreach ($line in Get-Content $path -Encoding UTF8) {
        $text = $line.Trim()
        if ($text -eq '' -or $text.StartsWith('#')) { continue }
        $i = $text.IndexOf('=')
        if ($i -lt 1) { continue }
        $result[$text.Substring(0, $i).Trim()] = $text.Substring($i + 1).Trim()
    }
    return $result
}

function Write-Mark {
    param(
        [ValidateSet('OK', 'FAIL', 'SKIP', 'INFO')] [string]$Status,
        [string]$Id,
        [string]$Text
    )
    $marks = @{ 'OK' = '[  OK  ]'; 'FAIL' = '[ FAIL ]'; 'SKIP' = '[ SKIP ]'; 'INFO' = '[ INFO ]' }
    $colors = @{ 'OK' = 'Green'; 'FAIL' = 'Red'; 'SKIP' = 'DarkGray'; 'INFO' = 'Cyan' }
    Write-Host ('{0} {1} {2}' -f $marks[$Status], $Id, $Text) -ForegroundColor $colors[$Status]
}

function Read-SelfCheck {
    $path = Join-Path $script:RepoRoot 'selfcheck.json'
    if (-not (Test-Path $path)) { throw "Немає файла selfcheck.json: $path" }
    $lines = Get-Content $path -Encoding UTF8 | Where-Object { -not $_.Trim().StartsWith('//') }
    return ([string]::Join("`n", $lines) | ConvertFrom-Json)
}

function Get-PassportToken {
    param($Access, $User, [string]$ClientId, [string]$TokenEndpoint)
    if (-not $ClientId) { $ClientId = $Access.clientId }
    if (-not $TokenEndpoint) { $TokenEndpoint = $Access.tokenEndpoint }
    $password = [Environment]::GetEnvironmentVariable($User.passwordEnv)
    if (-not $password) { throw "Не задано змінну середовища $($User.passwordEnv) з паролем $($User.username)" }
    $form = @{ grant_type = 'password'; client_id = $ClientId; username = $User.username; password = $password; scope = 'openid' }
    try {
        $response = Invoke-RestMethod -Method Post -Uri $TokenEndpoint -Body $form
    } catch {
        throw "Токен для $($User.username) (клієнт $ClientId) не отримано з ${TokenEndpoint}: $($_.Exception.Message)"
    }
    return $response.access_token
}

function Get-HttpStatus {
    param([string]$Method, [string]$Uri, [string]$Token, [string]$Body)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    try {
        if ($Body) {
            # Тіло передається байтами UTF-8, інакше Windows PowerShell 5.1 псує кирилицю.
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
            $r = Invoke-WebRequest -Method $Method -Uri $Uri -Headers $headers -Body $bytes `
                -ContentType 'application/json; charset=utf-8' -UseBasicParsing -TimeoutSec 30
        } else {
            $r = Invoke-WebRequest -Method $Method -Uri $Uri -Headers $headers -UseBasicParsing -TimeoutSec 30
        }
        return [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return 0
    }
}
