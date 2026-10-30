# Install the same compiler used for local release validation, from the official release.
$ErrorActionPreference = 'Stop'
if (-not $env:RUNNER_TEMP -or -not $env:GITHUB_ENV) {
    throw 'This script is intended for GitHub Actions runners.'
}
$installer = Join-Path $env:RUNNER_TEMP 'innosetup-6.7.3.exe'
$destination = Join-Path $env:RUNNER_TEMP 'InnoSetup'
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $installer
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Inno Setup checksum mismatch'
}
$process = Start-Process -FilePath $installer -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$destination`""
)
if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed ($($process.ExitCode))" }
$iscc = Join-Path $destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) { throw 'ISCC.exe was not installed' }
"ISCC_PATH=$iscc" >> $env:GITHUB_ENV
