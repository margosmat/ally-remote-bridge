# Requires Windows, .NET 10 SDK, Windows SDK signtool, Inno Setup 6, and reviewed dependencies.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ViiperDirectory,
    [Parameter(Mandatory)][string]$DriverDirectory,
    [Parameter(Mandatory)][string]$ApprovalFile,
    [Parameter(Mandatory)][string]$SignTool,
    [Parameter(Mandatory)][string]$InnoCompiler
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Version.Build -lt 22000) { throw 'Build the installer on Windows 11 x64.' }
$repo = Split-Path $PSScriptRoot -Parent
$approval = Get-Content -LiteralPath $ApprovalFile -Raw | ConvertFrom-Json
if ($approval.reviewed -ne $true) { throw 'Dependencies require a completed security review.' }
if ($approval.viiper.sourceRevision -ne 'a77bbbb78a6072188fe40526acbcba5d9d70d197') { throw 'Unsupported VIIPER revision.' }
if ($approval.driver.sourceRevision -ne '77a781eaedc7f6b6cb171a92d378dee408ad7408') { throw 'Unsupported driver revision.' }
function Assert-Hash([string]$Path, [string]$Expected) {
    if ($Expected -notmatch '^[a-fA-F0-9]{64}$') { throw "No approved hash for $Path" }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) { throw "Integrity check failed: $Path" }
}
Assert-Hash (Join-Path $ViiperDirectory 'libVIIPER.dll') $approval.viiper.sha256
foreach ($name in @('usbip2_ude.inf', 'usbip2_ude.cat', 'usbip2_ude.sys')) {
    Assert-Hash (Join-Path $DriverDirectory $name) $approval.driver.files.$name
}
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $DriverDirectory 'usbip2_ude.cat')
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $approval.driver.catalogSignerThumbprint) { throw 'Driver catalog signer validation failed.' }
# Check kernel-policy trust and catalog membership. Do not run any upstream installer.
foreach ($name in @('usbip2_ude.inf', 'usbip2_ude.sys')) {
    & $SignTool verify /kp /v /c (Join-Path $DriverDirectory 'usbip2_ude.cat') (Join-Path $DriverDirectory $name)
    if ($LASTEXITCODE -ne 0) { throw "Kernel driver/catalog verification failed: $name" }
}
Push-Location $repo
try {
    dotnet build AllyRemoteBridge.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/AllyRemoteBridge.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    dotnet publish src/AllyRemoteBridge.App -c Release -r win-x64 --self-contained true -o installer/payload
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    New-Item -ItemType Directory -Force installer/payload/native, installer/payload/driver | Out-Null
    Copy-Item -LiteralPath (Join-Path $ViiperDirectory 'libVIIPER.dll') installer/payload/native/
    $approval.viiper | ConvertTo-Json | Set-Content -Encoding utf8 installer/payload/native/manifest.json
    foreach ($name in @('usbip2_ude.inf', 'usbip2_ude.cat', 'usbip2_ude.sys')) { Copy-Item -LiteralPath (Join-Path $DriverDirectory $name) installer/payload/driver/ }
    $driverPolicy = @{ reviewed = $true; sourceRevision = $approval.driver.sourceRevision; catalogSignerThumbprint = $approval.driver.catalogSignerThumbprint; files = $approval.driver.files }
    $driverPolicy | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 installer/payload/driver/approved-driver.json
    Copy-Item LICENSE installer/payload/
    # Corresponding VIIPER and driver source/licenses must accompany public distribution.
    if (-not (Test-Path -LiteralPath (Join-Path $ViiperDirectory 'source-and-licenses'))) { throw 'VIIPER corresponding source and licenses are required.' }
    if (-not (Test-Path -LiteralPath (Join-Path $DriverDirectory 'source-and-licenses'))) { throw 'Driver corresponding source and licenses are required.' }
    Copy-Item -Recurse -LiteralPath (Join-Path $ViiperDirectory 'source-and-licenses') installer/payload/native/
    Copy-Item -Recurse -LiteralPath (Join-Path $DriverDirectory 'source-and-licenses') installer/payload/driver/
    & $InnoCompiler installer/AllyRemoteBridge.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-ChildItem artifacts -Filter '*.exe' | Get-FileHash -Algorithm SHA256 | Format-Table
} finally { Pop-Location }
