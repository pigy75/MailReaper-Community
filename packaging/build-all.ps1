# Script principale per build, test e packaging completo
param (
    [switch]$SkipTests = $false
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  GmailToPst - Pipeline Completa di Build & Setup " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Esecuzione Test
if (-not $SkipTests) {
    Write-Host "`n[Passo 1/3] Esecuzione Test Unitari e di Integrazione..." -ForegroundColor Yellow
    dotnet test "$PSScriptRoot\..\tests\GmailToPst.Tests\GmailToPst.Tests.csproj" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ I test sono falliti. Interruzione build." -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Tutti i test sono passati con successo!" -ForegroundColor Green
}

# 2. Build Versione Portable
Write-Host "`n[Passo 2/3] Generazione Versione Portable Single-File..." -ForegroundColor Yellow
& "$PSScriptRoot\build-portable.ps1"

# 3. Compilazione Installer Inno Setup (se installato)
Write-Host "`n[Passo 3/3] Verifica e compilazione Installer Setup..." -ForegroundColor Yellow
$InnoCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)

$IsccPath = $null
foreach ($path in $InnoCandidates) {
    if (Test-Path $path) {
        $IsccPath = $path
        break
    }
}

if ($IsccPath) {
    Write-Host "Trovato Inno Setup Compiler: $IsccPath" -ForegroundColor White
    & $IsccPath "$PSScriptRoot\inno-setup.iss"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Installer Setup.exe creato con successo in dist\GmailBackup_Setup_v1.0.exe" -ForegroundColor Green
    }
} else {
    Write-Host "ℹ️ Inno Setup non è installato sul sistema. Per generare Setup.exe puoi installarlo con:" -ForegroundColor Gray
    Write-Host "   winget install JRSoftware.InnoSetup" -ForegroundColor Cyan
}

Write-Host "`n🎉 Pipeline completata con successo! I file generati si trovano in dist\" -ForegroundColor Green
