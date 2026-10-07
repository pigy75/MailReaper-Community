# Script di build e pubblicazione Portable per Windows x64
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "$PSScriptRoot\..\dist\portable"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   MailReaper - Build Versione Portable   " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$ProjectDir = "$PSScriptRoot\..\src\GmailToPst.UI"
$ProjectFile = "$ProjectDir\GmailToPst.UI.csproj"

if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "`n[1/3] Compilazione ed esportazione Single-File..." -ForegroundColor Yellow
dotnet publish $ProjectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputDir

if (Test-Path "$OutputDir\GmailToPst.UI.exe") {
    Copy-Item "$OutputDir\GmailToPst.UI.exe" "$OutputDir\MailReaper.exe" -Force
}

$settingsContent = @'
{
  "ArchivesPath": "Archives",
  "DefaultExportPath": "Export",
  "DefaultAttachmentsPath": "Allegati_Gmail"
}
'@
Set-Content -Path "$OutputDir\settings.json" -Value $settingsContent -Encoding UTF8

$ZipFile = "$PSScriptRoot\..\dist\MailReaper_Portable_v1.0.zip"
Write-Host "`n[2/3] Creazione archivio ZIP compresso..." -ForegroundColor Yellow
if (Test-Path $ZipFile) { Remove-Item $ZipFile -Force }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipFile -CompressionLevel Optimal

Write-Host "`n[3/3] Build completata con successo!" -ForegroundColor Green
Write-Host "Eseguibile Portable: $OutputDir\MailReaper.exe" -ForegroundColor White
Write-Host "Archivio ZIP:        $ZipFile" -ForegroundColor White
