param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$AssetLicense,
    [Parameter(Mandatory=$true)][string]$MakeNsis,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)

# NSIS may be used from its portable ZIP; no machine-wide compiler installation is required.
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$AssetLicense = (Resolve-Path -LiteralPath $AssetLicense).Path
$MakeNsis = (Resolve-Path -LiteralPath $MakeNsis).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$icon = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\assets\GoldWidget.ico')).Path
$output = Join-Path $OutputDirectory '鲸小金-1.2.2-安装版.exe'
& $MakeNsis /V2 ('/DAPP_EXE=' + $Executable) ('/DASSET_LICENSE=' + $AssetLicense) ('/DAPP_ICON=' + $icon) ('/DOUTPUT_EXE=' + $output) (Join-Path $PSScriptRoot 'WhaleGold.nsi')
if ($LASTEXITCODE -ne 0) { throw 'NSIS compilation failed.' }
Write-Output $output
