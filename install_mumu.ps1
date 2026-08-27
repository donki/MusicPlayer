<#
.SYNOPSIS
    Compila, instala y lanza Music Player en el emulador MuMu Player (constitucion A.8.1).

.DESCRIPTION
    Resuelve adb y el puerto de MuMu, arranca la instancia si hace falta, instala el APK y
    opcionalmente lo lanza. Con -PushTestAudio genera y copia pistas sinteticas al emulador y
    fuerza el reindexado: sin musica la aplicacion no se puede validar (constitucion A.8.2).

    MuMu ejecuta Android 12 y NO sustituye a la validacion en dispositivo real. En concreto,
    Android Auto no se puede probar aqui: hace falta un dispositivo con la Desktop Head Unit.

.EXAMPLE
    ./install_mumu.ps1 -BuildFirst -Launch -PushTestAudio
#>
[CmdletBinding()]
param(
    [string]$ApkPath,
    [string]$AdbPath,
    [string]$DeviceSerial,
    [string]$PackageName,
    [string]$Configuration = "Debug",
    [string]$Framework = "net10.0-android36.0",
    [switch]$BuildFirst,
    [switch]$Launch,
    [switch]$PushTestAudio
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = $PSScriptRoot
$ProjectPath = Join-Path $ProjectRoot 'MusicPlayer.csproj'
$TestAudioScript = Join-Path $ProjectRoot '..\..\testing\MusicPlayer\make_test_audio.ps1'

$DefaultAdbPaths = @(
    'C:\Program Files\Netease\MuMuPlayer\nx_main\adb.exe',
    'C:\Program Files\Netease\MuMuPlayer\nx_device\12.0\shell\adb.exe',
    'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe')
)

$MuMuManager = 'C:\Program Files\Netease\MuMuPlayer\nx_main\MuMuManager.exe'

# MuMu expone adb en un puerto distinto segun version e instancia.
$MumuPorts = @('127.0.0.1:16384', '127.0.0.1:7555', '127.0.0.1:16416', '127.0.0.1:5555')

function Exit-WithError {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Resolve-AdbPath {
    if (-not [string]::IsNullOrWhiteSpace($AdbPath)) {
        if (Test-Path -LiteralPath $AdbPath) { return (Resolve-Path -LiteralPath $AdbPath).Path }
        Exit-WithError "No existe adb.exe en: $AdbPath"
    }

    foreach ($candidate in $DefaultAdbPaths) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    Exit-WithError 'No se encontro adb.exe. Pasa la ruta con -AdbPath.'
}

function Get-AdbDevices {
    $raw = & $script:AdbPath devices
    if ($LASTEXITCODE -ne 0) { Exit-WithError 'No se pudo listar dispositivos ADB.' }
    return $raw | Where-Object { $_ -match '^\S+\s+device$' } | ForEach-Object { ($_ -split '\s+')[0] }
}

function Resolve-DeviceSerial {
    if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) { return $DeviceSerial }

    $devices = @(Get-AdbDevices)
    foreach ($port in $MumuPorts) {
        if ($devices -contains $port) { return $port }
    }

    # La instancia puede estar apagada: se arranca sin abrir la interfaz.
    if (Test-Path -LiteralPath $MuMuManager) {
        Write-Host 'Arrancando la instancia de MuMu...'
        & $MuMuManager control -v 0 launch | Out-Null

        for ($attempt = 0; $attempt -lt 36; $attempt++) {
            foreach ($port in $MumuPorts) { & $script:AdbPath connect $port | Out-Null }
            $devices = @(Get-AdbDevices)
            $candidate = $devices | Where-Object { $MumuPorts -contains $_ } | Select-Object -First 1
            if ($candidate) {
                $booted = (& $script:AdbPath -s $candidate shell getprop sys.boot_completed) 2>$null
                if ("$booted".Trim() -eq '1') { return $candidate }
            }
            Start-Sleep -Seconds 5
        }
    }

    foreach ($port in $MumuPorts) {
        Write-Host "Intentando conectar a MuMu en $port..."
        & $script:AdbPath connect $port | Out-Host
    }

    $devices = @(Get-AdbDevices)
    foreach ($port in $MumuPorts) {
        if ($devices -contains $port) { return $port }
    }

    if ($devices.Count -eq 1) { return $devices[0] }

    if ($devices.Count -gt 1) {
        Write-Host 'Dispositivos detectados:'
        $devices | ForEach-Object { Write-Host "  $_" }
        Exit-WithError 'Hay varios dispositivos ADB. Usa -DeviceSerial para elegir MuMu.'
    }

    Exit-WithError 'No se detecto MuMu por ADB. Abre MuMu Player y vuelve a ejecutar el script.'
}

function Get-ProjectValue {
    param(
        [Parameter(Mandatory = $true)][xml]$ProjectXml,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($node in $ProjectXml.SelectNodes("//*[local-name()='$Name']")) {
        if (-not [string]::IsNullOrWhiteSpace($node.InnerText)) { return $node.InnerText.Trim() }
    }

    return $null
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    Exit-WithError "No existe el proyecto: $ProjectPath"
}

$projectXml = [xml](Get-Content -LiteralPath $ProjectPath -Raw)
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = Get-ProjectValue $projectXml 'ApplicationId'
}
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    Exit-WithError 'No se pudo resolver ApplicationId desde el csproj.'
}

$script:AdbPath = Resolve-AdbPath

Write-Host '========================================'
Write-Host '   Music Player - Instalar en MuMu'
Write-Host '========================================'
Write-Host "ADB: $script:AdbPath"
Write-Host "Package: $PackageName"

if ($BuildFirst) {
    Write-Host "`nCompilando APK ($Configuration)..." -ForegroundColor Cyan
    dotnet build $ProjectPath -c $Configuration -f $Framework
    if ($LASTEXITCODE -ne 0) { Exit-WithError 'Fallo el build del APK.' }
}

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $latestApk = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'bin') -Filter '*-Signed.apk' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $latestApk) {
        Exit-WithError 'No se encontro ningun *-Signed.apk. Ejecuta con -BuildFirst.'
    }
    $ApkPath = $latestApk.FullName
}

if (-not (Test-Path -LiteralPath $ApkPath)) { Exit-WithError "No existe el APK: $ApkPath" }
$ApkPath = (Resolve-Path -LiteralPath $ApkPath).Path

$DeviceSerial = Resolve-DeviceSerial
Write-Host "Dispositivo MuMu: $DeviceSerial"
Write-Host "APK: $ApkPath`n"

# -d permite reinstalar sobre una version con versionCode mayor durante las pruebas.
& $script:AdbPath -s $DeviceSerial install -r -d $ApkPath | Out-Host
if ($LASTEXITCODE -ne 0) { Exit-WithError 'Fallo la instalacion del APK en MuMu.' }

if ($PushTestAudio) {
    Write-Host "`nGenerando y copiando pistas de prueba..." -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $TestAudioScript)) {
        Exit-WithError "No existe el generador de pistas: $TestAudioScript"
    }

    & $TestAudioScript | Out-Host
    $audioFolder = Join-Path (Split-Path -Parent $TestAudioScript) 'audio'

    & $script:AdbPath -s $DeviceSerial shell mkdir -p /sdcard/Music/Prueba | Out-Null
    Get-ChildItem -LiteralPath $audioFolder -Filter '*.mp3' | ForEach-Object {
        & $script:AdbPath -s $DeviceSerial push $_.FullName "/sdcard/Music/Prueba/$($_.Name)" | Out-Null
    }

    # Sin reindexar, MediaStore no ve los ficheros recien copiados y la app aparece vacia.
    & $script:AdbPath -s $DeviceSerial shell "content call --uri content://media --method scan_volume --arg external_primary" | Out-Null
    Write-Host 'Pistas de prueba copiadas e indexadas.'
}

if ($Launch) {
    Write-Host "`nLanzando app..."
    # Se lanza la actividad por nombre, no con monkey: monkey inyecta un evento aleatorio ademas
    # de abrir la app y falsea la pantalla que se valida (constitucion A.8.1).
    $activity = (& $script:AdbPath -s $DeviceSerial shell cmd package resolve-activity --brief $PackageName |
        Select-Object -Last 1).Trim()

    if ([string]::IsNullOrWhiteSpace($activity)) {
        Exit-WithError 'La app se instalo, pero no se pudo resolver su actividad principal.'
    }

    & $script:AdbPath -s $DeviceSerial shell am start -n $activity | Out-Host
    if ($LASTEXITCODE -ne 0) { Exit-WithError 'La app se instalo, pero no se pudo lanzar.' }

    Start-Sleep -Seconds 6
    $crashes = & $script:AdbPath -s $DeviceSerial logcat -d -t 500 | Select-String -Pattern 'FATAL EXCEPTION'
    if ($crashes) {
        Write-Host "`nAVISO: el arranque registro una excepcion no controlada:" -ForegroundColor Yellow
        $crashes | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    }
}

Write-Host "`nInstalacion completada en MuMu." -ForegroundColor Green
