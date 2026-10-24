# Builds the NestZone for Discord helper exe into dist\NestZoneDiscord.exe.
# Works from any folder: paths are relative to this script.
#
# Optional signing (Authenticode). Set one of these before running:
#   $env:NZ_SIGN_THUMBPRINT  thumbprint of a code-signing cert in Cert:\CurrentUser\My
#                            (hardware tokens and cloud HSMs like Certum SimplySign
#                            show up there too)
#   $env:NZ_SIGNTOOL         full path to signtool.exe (found automatically if unset)
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc (.NET Framework 4.x is required)." }

$dist = Join-Path $here 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$out      = Join-Path $dist 'NestZoneDiscord.exe'
$icon     = Join-Path $here 'helper\icon.ico'
$manifest = Join-Path $here 'helper\app.manifest'
$sources  = Get-ChildItem (Join-Path $here 'helper\*.cs') | ForEach-Object { $_.FullName }

$cscArgs = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
    '/r:System.Web.Extensions.dll', '/r:System.Windows.Forms.dll',
    ('/win32icon:' + $icon), ('/win32manifest:' + $manifest), ('/out:' + $out)
) + $sources

Write-Host 'Compiling NestZoneDiscord.exe ...'
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
if (-not (Test-Path $out)) { throw "Build reported success but $out is missing." }

if ($env:NZ_SIGN_THUMBPRINT) {
    $signtool = $env:NZ_SIGNTOOL
    if (-not $signtool) {
        $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK or set NZ_SIGNTOOL.' }
    Write-Host 'Signing with certificate' $env:NZ_SIGN_THUMBPRINT
    & $signtool sign /sha1 $env:NZ_SIGN_THUMBPRINT /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d 'NestZone for Discord' $out
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
} else {
    Write-Host 'Not signed (set NZ_SIGN_THUMBPRINT to sign).'
}

$hash = (Get-FileHash $out -Algorithm SHA256).Hash.ToLower()
Write-Host ("Built {0} ({1} bytes)" -f $out, (Get-Item $out).Length)
Write-Host "SHA256 $hash"
