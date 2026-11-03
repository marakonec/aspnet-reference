<#
.SYNOPSIS
    Негативні перевірки доступу. Чотири зіпсовані токени мають дати 401,
    власник над чужим ресурсом 403 або 404, власник над своїм 200.
.EXAMPLE
    .\scripts\negative.ps1
    .\scripts\negative.ps1 -NoExpired
#>
param(
    [int]$Lifespan = 63,
    [switch]$NoExpired
)
. "$PSScriptRoot\_common.ps1"

$passport = Read-SelfCheck
$access = $passport.access
if (-not $access -or -not $access.tokenEndpoint) { throw 'У selfcheck.json не заповнено блок access' }
if (-not $access.ownResource) { throw 'У selfcheck.json не названо access.ownResource' }
$base = $passport.baseUrl.TrimEnd('/')
$target = $base + $access.ownResource
$failed = 0

function Test-Rejected {
    param([string]$Id, [string]$Title, [string]$Token)
    $code = Get-HttpStatus -Method 'GET' -Uri $target -Token $Token
    if ($code -eq 401) { Write-Mark -Status OK -Id $Id -Text "$Title, відповідь 401" }
    else { Write-Mark -Status FAIL -Id $Id -Text "$Title, відповідь $code, очікували 401"; $script:failed++ }
}

function Set-BrokenSignature {
    param([string]$Token)
    $parts = $Token.Split('.')
    $replacement = 'A'
    if ($parts[2].Substring(0, 1) -eq 'A') { $replacement = 'B' }
    $parts[2] = $replacement + $parts[2].Substring(1)
    return [string]::Join('.', $parts)
}

Write-Host "Негативні перевірки, ресурс $target" -ForegroundColor Cyan

$expired = $null
if ($NoExpired) {
    Write-Mark -Status SKIP -Id 'N1' -Text 'Прострочений токен пропущено (-NoExpired)'
} elseif (-not $access.expiringClientId) {
    Write-Mark -Status SKIP -Id 'N1' -Text 'access.expiringClientId не названо в selfcheck.json'
} else {
    $expired = Get-PassportToken -Access $access -User $access.privilegedUser -ClientId $access.expiringClientId
}

if ($access.foreignTokenEndpoint -and $access.foreignUser) {
    $foreign = Get-PassportToken -Access $access -User $access.foreignUser `
        -ClientId $access.foreignClientId -TokenEndpoint $access.foreignTokenEndpoint
    Test-Rejected -Id 'N2' -Title 'Токен іншого видавця' -Token $foreign
} else {
    Write-Mark -Status SKIP -Id 'N2' -Text 'access.foreignTokenEndpoint або access.foreignUser не названо в selfcheck.json'
}

if ($access.otherClientId) {
    $wrongAudience = Get-PassportToken -Access $access -User $access.privilegedUser -ClientId $access.otherClientId
    Test-Rejected -Id 'N3' -Title 'Токен для іншої аудиторії' -Token $wrongAudience
} else {
    Write-Mark -Status SKIP -Id 'N3' -Text 'access.otherClientId не названо в selfcheck.json'
}

$valid = Get-PassportToken -Access $access -User $access.privilegedUser
Test-Rejected -Id 'N4' -Title 'Змінений підпис' -Token (Set-BrokenSignature -Token $valid)

if ($expired) {
    Write-Host "Очікування $Lifespan с, поки сплине строк токена N1." -ForegroundColor DarkGray
    Start-Sleep -Seconds $Lifespan
    Test-Rejected -Id 'N1' -Title 'Прострочений токен' -Token $expired
}

if ($access.ownerUser -and $access.foreignResource) {
    $owner = Get-PassportToken -Access $access -User $access.ownerUser
    $code = Get-HttpStatus -Method 'GET' -Uri ($base + $access.foreignResource) -Token $owner
    if ($code -eq 403 -or $code -eq 404) { Write-Mark -Status OK -Id 'N5' -Text "Власник над чужим ресурсом, відповідь $code" }
    else { Write-Mark -Status FAIL -Id 'N5' -Text "Власник над чужим ресурсом, відповідь $code, очікували 403 або 404"; $failed++ }
    $code = Get-HttpStatus -Method 'GET' -Uri $target -Token $owner
    if ($code -eq 200) { Write-Mark -Status OK -Id 'N6' -Text 'Власник над своїм ресурсом, відповідь 200' }
    else { Write-Mark -Status FAIL -Id 'N6' -Text "Власник над своїм ресурсом, відповідь $code"; $failed++ }
} else {
    Write-Mark -Status SKIP -Id 'N5' -Text 'access.ownerUser або access.foreignResource не названо в selfcheck.json'
}

if ($failed -eq 0) { exit 0 } else { exit 1 }
