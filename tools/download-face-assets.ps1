#Requires -Version 5.1
<#
.SYNOPSIS
    Mengunduh file model dlib yang dibutuhkan FaceRecognitionDotNet, plus gambar
    contoh yang dipakai oleh tools\FaceEncodingProbe.

.DESCRIPTION
    FaceRecognitionDotNet tidak menyertakan file model karena alasan lisensi.
    Keempat file .dat di bawah berasal dari repo ageitgey/face_recognition_models
    (lisensi CC0 1.0 / public domain), disimpan TANPA kompresi, sehingga tidak
    perlu bzip2/7z untuk membukanya.

    Skrip ini idempotent: file yang ukurannya sudah persis sama dengan yang
    diharapkan akan dilewati, jadi menjalankan ulang tidak mengunduh apa pun.

    Total unduhan model +/- 126 MB.

.PARAMETER SkipModels
    Lewati unduhan file model .dat.

.PARAMETER SkipTestImages
    Lewati unduhan gambar uji untuk tools\FaceEncodingProbe.

.PARAMETER Force
    Unduh ulang walaupun file dengan ukuran yang benar sudah ada.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\download-face-assets.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\download-face-assets.ps1 -SkipTestImages
#>
[CmdletBinding()]
param(
    [switch]$SkipModels,
    [switch]$SkipTestImages,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# tools\ -> root repo
$RepoRoot      = Split-Path -Parent $PSScriptRoot
$ModelsDir     = Join-Path $RepoRoot 'FaceModels'
$TestImagesDir = Join-Path $RepoRoot 'tools\FaceEncodingProbe\testdata'

$ModelBaseUrl = 'https://raw.githubusercontent.com/ageitgey/face_recognition_models/master/face_recognition_models/models'
$ImageBaseUrl = 'https://raw.githubusercontent.com/ageitgey/face_recognition/master/examples'

# Ukuran byte diverifikasi dari GitHub API (repos/ageitgey/.../contents) saat skrip ini dibuat.
# Dipakai sebagai pemeriksaan integritas murah: unduhan yang terputus hampir selalu
# menghasilkan file dengan ukuran berbeda.
$Models = @(
    [pscustomobject]@{ Name = 'mmod_human_face_detector.dat';              Size =    729940; Url = "$ModelBaseUrl/mmod_human_face_detector.dat" }
    [pscustomobject]@{ Name = 'shape_predictor_5_face_landmarks.dat';      Size =   9150489; Url = "$ModelBaseUrl/shape_predictor_5_face_landmarks.dat" }
    [pscustomobject]@{ Name = 'shape_predictor_68_face_landmarks.dat';     Size =  99693937; Url = "$ModelBaseUrl/shape_predictor_68_face_landmarks.dat" }
    [pscustomobject]@{ Name = 'dlib_face_recognition_resnet_model_v1.dat'; Size =  22466066; Url = "$ModelBaseUrl/dlib_face_recognition_resnet_model_v1.dat" }
)

$TestImages = @(
    [pscustomobject]@{ Name = 'obama.jpg';      Size = 279922; Url = "$ImageBaseUrl/obama.jpg" }
    [pscustomobject]@{ Name = 'two_people.jpg'; Size = 487722; Url = "$ImageBaseUrl/two_people.jpg" }
    [pscustomobject]@{ Name = 'biden.jpg';      Size = 353723; Url = "$ImageBaseUrl/biden.jpg" }
)

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curl) {
    throw 'curl.exe tidak ditemukan. Windows 10 1803+ menyertakannya di System32; periksa PATH Anda.'
}

$script:Failures = @()

function Get-Asset {
    param(
        [Parameter(Mandatory)] $Asset,
        [Parameter(Mandatory)] [string]$Directory,
        [Parameter(Mandatory)] [switch]$Overwrite
    )

    if (-not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    }

    $dest = Join-Path $Directory $Asset.Name

    if ((Test-Path -LiteralPath $dest) -and -not $Overwrite) {
        $actual = (Get-Item -LiteralPath $dest).Length
        if ($actual -eq $Asset.Size) {
            Write-Host ("  [skip] {0,-42} sudah ada ({1:N0} byte)" -f $Asset.Name, $actual) -ForegroundColor DarkGray
            return $true
        }
        Write-Host ("  [redo] {0,-42} ukuran salah ({1:N0} != {2:N0})" -f $Asset.Name, $actual, $Asset.Size) -ForegroundColor Yellow
    }

    Write-Host ("  [get ] {0,-42} {1,8:N1} MB ... " -f $Asset.Name, ($Asset.Size / 1MB)) -NoNewline

    $tmp = "$dest.part"
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }

    # --fail    : HTTP 4xx/5xx menghasilkan exit code, bukan file berisi halaman error
    # --location: ikuti redirect (raw.githubusercontent kadang mengarahkan ke CDN)
    & curl.exe --fail --location --retry 3 --retry-delay 2 --connect-timeout 30 `
               --silent --show-error --output $tmp $Asset.Url
    $exit = $LASTEXITCODE

    if ($exit -ne 0 -or -not (Test-Path -LiteralPath $tmp)) {
        Write-Host "GAGAL (curl exit $exit)" -ForegroundColor Red
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
        $script:Failures += $Asset.Name
        return $false
    }

    $actual = (Get-Item -LiteralPath $tmp).Length
    if ($actual -ne $Asset.Size) {
        Write-Host "GAGAL (ukuran $actual != $($Asset.Size))" -ForegroundColor Red
        Remove-Item -LiteralPath $tmp -Force
        $script:Failures += $Asset.Name
        return $false
    }

    Move-Item -LiteralPath $tmp -Destination $dest -Force
    Write-Host "OK" -ForegroundColor Green
    return $true
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

if (-not $SkipModels) {
    Write-Host ''
    Write-Host "Model dlib -> $ModelsDir" -ForegroundColor Cyan
    foreach ($m in $Models) { Get-Asset -Asset $m -Directory $ModelsDir -Overwrite:$Force | Out-Null }
}

if (-not $SkipTestImages) {
    Write-Host ''
    Write-Host "Gambar uji -> $TestImagesDir" -ForegroundColor Cyan
    foreach ($i in $TestImages) { Get-Asset -Asset $i -Directory $TestImagesDir -Overwrite:$Force | Out-Null }
}

$stopwatch.Stop()
Write-Host ''
Write-Host ("Selesai dalam {0:n1} detik." -f $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Cyan

if ($script:Failures.Count -gt 0) {
    Write-Host ''
    Write-Host "GAGAL mengunduh: $($script:Failures -join ', ')" -ForegroundColor Red
    Write-Host 'Jalankan ulang skrip ini; file yang sudah lengkap akan dilewati.' -ForegroundColor Red
    exit 1
}

if (-not $SkipModels) {
    $missing = @($Models | Where-Object { -not (Test-Path -LiteralPath (Join-Path $ModelsDir $_.Name)) })
    if ($missing.Count -gt 0) {
        Write-Host ''
        Write-Host "Model masih kurang: $(($missing | ForEach-Object Name) -join ', ')" -ForegroundColor Red
        exit 1
    }
    Write-Host 'Semua model lengkap. FaceRecognition.Create() siap dipakai.' -ForegroundColor Green
}

exit 0

