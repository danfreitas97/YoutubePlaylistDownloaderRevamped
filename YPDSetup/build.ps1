# Builds the installer: publishes the app self-contained, then compiles YPDSetup.iss.
# Output: YPDSetup\output\YoutubePlaylistDownloaderRevamped-Setup.exe
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$publish = Join-Path $here 'publish'

if (-not $Version) {
    [xml]$project = Get-Content -LiteralPath "$here\..\YPDRApp\YoutubePlaylistDownloader.csproj"
    $Version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version: $Version" }

$iscc = @($IsccPath, "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { $_ } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

# Restrict cleanup to these generated directories inside YPDSetup.
foreach ($directory in @($publish, (Join-Path $here 'output'))) {
    $resolved = [IO.Path]::GetFullPath($directory)
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetFullPath($here)) {
        throw "Refusing to clean path outside setup directory: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

dotnet publish "$here\..\YPDRApp\YoutubePlaylistDownloader.csproj" -c Release -r win-x64 --self-contained true -o $publish -nologo "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

& $iscc "/DAppVersion=$Version" "$here\YPDSetup.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }

$output = Join-Path $here 'output'
Set-Content -LiteralPath "$output\version.txt" -Value "$Version.0" -Encoding ascii
Copy-Item -LiteralPath "$here\..\YPDRApp\changelog.txt" -Destination "$output\changelog.txt"
$installer = Join-Path $output 'YoutubePlaylistDownloaderRevamped-Setup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$output\SHA256SUMS.txt" -Value "$hash  YoutubePlaylistDownloaderRevamped-Setup.exe" -Encoding ascii
