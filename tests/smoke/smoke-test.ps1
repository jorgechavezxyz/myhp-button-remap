# End-to-end checks on a real Windows machine (run by CI after the build):
#   MSI: upgrade from the previous release, install, start, uninstall while running, reinstall keeping the config.
#   Standalone exe: self-install, start, single instance.
param(
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $Exe,
    [string] $PreviousMsi
)

$ErrorActionPreference = 'Stop'
$Msi = (Resolve-Path $Msi).Path
$Exe = (Resolve-Path $Exe).Path
if ($PreviousMsi) { $PreviousMsi = (Resolve-Path $PreviousMsi).Path }
$upgradeCode = '{92C4C96C-D20F-4C52-8B3A-99D385FD244A}'
$msiDir = Join-Path $env:LOCALAPPDATA 'HP Button Remap GUI'
$msiExe = Join-Path $msiDir 'HPButtonRemap.exe'
$msiConfig = Join-Path $env:LOCALAPPDATA 'HPButtonRemapGUI\config.json'
$programs = [Environment]::GetFolderPath('Programs')
$shortcuts = @('HP Button Remap GUI.lnk', 'Uninstall HP Button Remap GUI.lnk') | ForEach-Object { Join-Path $programs $_ }
$logs = Join-Path ([IO.Path]::GetTempPath()) 'hpremap-smoke'
New-Item -ItemType Directory -Force $logs | Out-Null
$step = 0

function Check([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "FAILED: $Message" }
    Write-Host "ok - $Message"
}

function Invoke-Msi([string[]] $Arguments) {
    $script:step++
    $log = Join-Path $logs "msiexec-$script:step.log"
    $process = Start-Process msiexec.exe -ArgumentList ($Arguments + @('/qn', '/norestart', '/l*v', "`"$log`"")) -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Get-Content $log -Tail 60
        throw "msiexec $($Arguments -join ' ') failed with exit code $($process.ExitCode)"
    }
}

function Get-InstalledVersions {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    @($installer.RelatedProducts($upgradeCode) | ForEach-Object { $installer.ProductInfo($_, 'VersionString') })
}

function Start-App([string] $Path) {
    $process = Start-Process $Path -PassThru
    $null = $process.Handle # keep the handle so the exit code stays readable
    Start-Sleep -Seconds 6
    Check (-not $process.HasExited) "$([IO.Path]::GetFileName($Path)) keeps running after start"
    $process
}

function Stop-AllCopies {
    Get-Process HPButtonRemap -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$expectedVersion = (Get-Item $Exe).VersionInfo.FileVersion -replace '^(\d+\.\d+\.\d+).*$', '$1'
Write-Host "Testing version $expectedVersion"

# 1. Upgrade from the previous release while it is running.
if ($PreviousMsi) {
    Invoke-Msi @('/i', "`"$PreviousMsi`"")
    $old = Start-App $msiExe
    Invoke-Msi @('/i', "`"$Msi`"")
    Check $old.HasExited 'upgrade closes the running previous version'
    Check ((Get-InstalledVersions).Count -eq 1) 'upgrade leaves exactly one installed copy'
}
else {
    Invoke-Msi @('/i', "`"$Msi`"")
}

# 2. Fresh state checks.
$versions = Get-InstalledVersions
Check ($versions.Count -eq 1 -and $versions[0] -eq $expectedVersion) "installed version is $expectedVersion"
Check (Test-Path $msiExe) 'app is installed'
Check (Test-Path (Join-Path $msiDir 'msi-managed.txt')) 'MSI marker is installed'
foreach ($shortcut in $shortcuts) { Check (Test-Path $shortcut) "Start menu shortcut exists: $([IO.Path]::GetFileName($shortcut))" }

# 3. Start the app; it creates its config and keeps running even without HP hardware.
$app = Start-App $msiExe
Check (Test-Path $msiConfig) 'app creates its config on first start'
Add-Content $msiConfig ' ' # mark the config so the reinstall check can tell it was kept
$configBefore = Get-Content $msiConfig -Raw

# 4. Reinstalling the same build while running replaces it in place (no second copy).
Invoke-Msi @('/i', "`"$Msi`"", 'REINSTALL=ALL', 'REINSTALLMODE=vomus')
Check ((Get-InstalledVersions).Count -eq 1) 'reinstall keeps exactly one installed copy'
Stop-AllCopies
$app = Start-App $msiExe

# 5. Uninstall while running: the app closes cleanly, files and shortcuts go, the config stays.
Invoke-Msi @('/x', "`"$Msi`"")
Check $app.HasExited 'uninstall closes the running app'
Check ($app.ExitCode -eq 0) "app exits cleanly when the installer closes it (exit code $($app.ExitCode))"
Check ((Get-InstalledVersions).Count -eq 0) 'uninstall removes the product'
Check (-not (Test-Path $msiExe)) 'uninstall removes the app'
foreach ($shortcut in $shortcuts) { Check (-not (Test-Path $shortcut)) "uninstall removes $([IO.Path]::GetFileName($shortcut))" }
Check (Test-Path $msiConfig) 'uninstall keeps the user config'

# 6. Reinstall after uninstall keeps the previous config.
Invoke-Msi @('/i', "`"$Msi`"")
Check ((Get-Content $msiConfig -Raw) -eq $configBefore) 'reinstall keeps the previous config'
$app = Start-App $msiExe
Invoke-Msi @('/x', "`"$Msi`"")
Check $app.HasExited 'second uninstall closes the app'
Check ((Get-InstalledVersions).Count -eq 0) 'second uninstall removes the product'

# 7. Standalone exe: first run installs itself and relaunches from the install folder.
$selfDir = Join-Path $env:LOCALAPPDATA 'HPButtonRemap'
$selfExe = Join-Path $selfDir 'HPButtonRemap.exe'
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'HP Button Remap.lnk'
$bootstrap = Start-Process $Exe -PassThru
Start-Sleep -Seconds 8
Check $bootstrap.HasExited 'standalone exe hands off to the installed copy'
Check (Test-Path $selfExe) 'standalone exe installs itself'
$running = @(Get-Process HPButtonRemap -ErrorAction SilentlyContinue)
Check ($running.Count -eq 1 -and $running[0].Path -eq $selfExe) 'installed copy is running'
Check (Test-Path (Join-Path $selfDir 'config.json')) 'installed copy creates its config'
Check (Test-Path $startup) 'installed copy adds the startup shortcut'

# 8. Running the exe again does not start a second copy.
$again = Start-Process $Exe -PassThru
Start-Sleep -Seconds 8
Check $again.HasExited 'second launch exits'
Check (@(Get-Process HPButtonRemap -ErrorAction SilentlyContinue).Count -eq 1) 'still exactly one copy running'

Stop-AllCopies
Remove-Item $startup -ErrorAction SilentlyContinue
Remove-Item $selfDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'All smoke tests passed.'
