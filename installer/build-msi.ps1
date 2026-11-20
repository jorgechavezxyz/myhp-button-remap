# Builds the Windows x64 MSI with its setup wizard. Used by CI; also works locally:
#   pwsh installer/build-msi.ps1 -AppDir <published app folder> -Version 1.0.3 -Output HPButtonRemapGUI-win-x64.msi
param(
    [Parameter(Mandatory)] [string] $AppDir,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Wix = 'wix'
)

$ErrorActionPreference = 'Stop'
$wixVersion = '5.0.2'
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$AppDir = (Resolve-Path $AppDir).Path

# The wizard dialogs and the close-running-app step come from these WiX extensions.
foreach ($extension in 'WixToolset.UI.wixext', 'WixToolset.Util.wixext') {
    & $Wix extension add -g "$extension/$wixVersion"
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

# The wizard's license page needs RTF. Generate it from LICENSE so the two never drift apart.
$license = Get-Content (Join-Path $PSScriptRoot '..\LICENSE') -Raw
$body = $license.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}') -replace "\r?\n", "\par`r`n"
$rtf = "{\rtf1\ansi\deff0{\fonttbl{\f0\fswiss Segoe UI;}}\f0\fs16`r`n$body}"
Set-Content -Path (Join-Path $PSScriptRoot 'License.rtf') -Value $rtf -Encoding ascii

Push-Location $PSScriptRoot
try {
    & $Wix build Package.wxs -arch x64 -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
        -d "AppDir=$AppDir" -d "Version=$Version" -o $Output
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
