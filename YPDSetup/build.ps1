# Builds the installer: publishes the app self-contained, then compiles YPDSetup.iss.
# Output: YPDSetup\output\YoutubePlaylistDownloaderRevamped-Setup.exe
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$publish = Join-Path $here 'publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$here\..\YoutubePlaylistDownloader\YoutubePlaylistDownloader.csproj" -c Release -r win-x64 --self-contained true -o $publish -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc "$here\YPDSetup.iss"
exit $LASTEXITCODE
