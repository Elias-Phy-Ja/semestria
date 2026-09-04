#Requires -Version 5.1
<#
.SYNOPSIS
    Baut Semestria und packt es als MSIX-Paket.

.DESCRIPTION
    1. dotnet publish (self-contained, x64, Release)
    2. AppxManifest + Assets in Pack-Verzeichnis kopieren
    3. makeappx pack → Semestria_<Version>.msix
    4. Optional: selbstsignieren für lokale Tests

.PARAMETER Sign
    Wenn angegeben, wird das MSIX mit einem selbstsignierten Testzertifikat signiert.
    Nur für lokale Tests — der Microsoft Store signiert selbst.

.EXAMPLE
    .\build-msix.ps1            # MSIX ohne Signatur (für Store-Upload)
    .\build-msix.ps1 -Sign      # MSIX + Testsignatur (für lokale Installation)
#>
param([switch]$Sign)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Pfade ─────────────────────────────────────────────────────────────────────

$root       = Split-Path $PSScriptRoot -Parent              # Applikation/
$uiProj     = "$root\SchulnetzSync.UI\SchulnetzSync.UI.csproj"
$manifest   = "$PSScriptRoot\Package.appxmanifest"
$assetsDir  = "$PSScriptRoot\Assets"

$publishOut = "$PSScriptRoot\_publish"                       # dotnet publish output
$packDir    = "$PSScriptRoot\_pack"                          # MSIX-Inhalt (wird gepackt)
$outDir     = "$PSScriptRoot\_out"                          # Fertige MSIX-Datei

# Version aus dem Manifest lesen
[xml]$mf  = Get-Content $manifest
$version  = $mf.Package.Identity.Version                    # z.B. "1.0.0.0"
$msixName = "Semestria_${version}_x64.msix"
$msixPath = "$outDir\$msixName"

# makeappx und signtool aus dem NuGet-Cache
$sdkTools = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools"
$sdkVer   = (Get-ChildItem $sdkTools | Sort-Object Name -Descending | Select-Object -First 1).Name
$toolsDir = "$sdkTools\$sdkVer\bin\10.0.26100.0\x64"
$makeappx = "$toolsDir\makeappx.exe"
$signtool = "$toolsDir\signtool.exe"

if (-not (Test-Path $makeappx)) {
    Write-Error "makeappx.exe nicht gefunden unter $toolsDir`nFühre zuerst: dotnet restore $PSScriptRoot\helper.csproj"
}

Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Semestria MSIX Builder  v$version" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ── 1. Publish ────────────────────────────────────────────────────────────────

Write-Host "[1/4] dotnet publish (self-contained, win-x64, Release)..."
if (Test-Path $publishOut) { Remove-Item $publishOut -Recurse -Force }

& dotnet publish $uiProj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishOut `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }
Write-Host "  → $publishOut" -ForegroundColor Green

# ── 2. Pack-Verzeichnis aufbauen ──────────────────────────────────────────────

Write-Host ""
Write-Host "[2/4] Pack-Verzeichnis erstellen..."
if (Test-Path $packDir) { Remove-Item $packDir -Recurse -Force }
New-Item -ItemType Directory $packDir | Out-Null

# App-Dateien kopieren
Copy-Item "$publishOut\*" $packDir -Recurse

# AppxManifest.xml (makeappx erwartet diesen Namen)
Copy-Item $manifest "$packDir\AppxManifest.xml"

# Assets kopieren
Copy-Item $assetsDir "$packDir\Assets" -Recurse

Write-Host "  → $packDir" -ForegroundColor Green

# ── 3. MSIX packen ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[3/4] MSIX packen..."
New-Item -ItemType Directory -Force $outDir | Out-Null
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

& $makeappx pack /d $packDir /p $msixPath /nv
if ($LASTEXITCODE -ne 0) { throw "makeappx fehlgeschlagen" }
Write-Host "  → $msixPath" -ForegroundColor Green

# ── 4. Optional: Signieren für lokale Tests ───────────────────────────────────

if ($Sign) {
    Write-Host ""
    Write-Host "[4/4] Selbstsigniertes Testzertifikat erstellen und signieren..."

    # Publisher aus Manifest
    $publisher = $mf.Package.Identity.Publisher   # z.B. "CN=EliasWyss"
    $certPath  = "$outDir\SemestriaTestCert.pfx"
    $certPass  = "SemestriaTest"

    # Altes Testzert entfernen
    Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $cert = New-SelfSignedCertificate `
        -Subject $publisher `
        -Type CodeSigningCert `
        -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(3)

    Export-PfxCertificate `
        -Cert $cert `
        -FilePath $certPath `
        -Password (ConvertTo-SecureString $certPass -AsPlainText -Force) | Out-Null

    # Zertifikat als vertrauenswürdig installieren (für "Lokale Installation")
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        "TrustedPeople", "CurrentUser")
    $store.Open("ReadWrite")
    $store.Add($cert)
    $store.Close()

    & $signtool sign /fd SHA256 /a /f $certPath /p $certPass $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool fehlgeschlagen" }

    Write-Host "  Zertifikat: $certPath  (Passwort: $certPass)" -ForegroundColor Yellow
    Write-Host "  Signiert:   $msixPath" -ForegroundColor Green

    Write-Host ""
    Write-Host "─── Lokale Installation ─────────────────────" -ForegroundColor Yellow
    Write-Host "Das Zertifikat wurde in 'Vertrauenswürdige Personen' installiert." -ForegroundColor Yellow
    Write-Host "Doppelklick auf $msixName zum Installieren." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[4/4] Keine Signatur (für Microsoft Store-Upload nicht nötig)." -ForegroundColor DarkGray
}

# ── Zusammenfassung ───────────────────────────────────────────────────────────

$sizeMb = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
Write-Host "  FERTIG!" -ForegroundColor Green
Write-Host "  $msixName  ($sizeMb MB)" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

if (-not $Sign) {
    Write-Host "Store-Upload:" -ForegroundColor Cyan
    Write-Host "  1. partner.microsoft.com/dashboard → Neue App → 'Semestria' reservieren"
    Write-Host "  2. Produkt-Identität kopieren → Package.appxmanifest aktualisieren"
    Write-Host "  3. build-msix.ps1 nochmals ausführen"
    Write-Host "  4. MSIX hochladen → Partner Center signiert automatisch"
    Write-Host ""
    Write-Host "Lokale Tests:" -ForegroundColor Cyan
    Write-Host "  .\build-msix.ps1 -Sign"
    Write-Host ""
}
