[CmdletBinding()]
param(
    [switch]$IncludeArm64,
    [switch]$SkipTests,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path (Join-Path $root 'AutoClicker.sln'))) { throw 'Run this script from the existing AutoClicker repository.' }
Set-Location $root
$version = ([xml](Get-Content -Raw 'Directory.Build.props')).Project.PropertyGroup.Version
if (-not $version) { throw 'Directory.Build.props does not define Version.' }
$publishRoot = Join-Path $root 'artifacts\publish'
$releaseRoot = Join-Path $root 'artifacts\release\windows'
New-Item -ItemType Directory -Force -Path $publishRoot, $releaseRoot | Out-Null
foreach ($path in @((Join-Path $publishRoot 'win-x64'), (Join-Path $releaseRoot 'AutoClicker'))) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean path outside repository artifacts: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
$zipPath = Join-Path $releaseRoot "AutoClicker-$version-win-x64-portable.zip"
$setupPath = Join-Path $releaseRoot "AutoClicker-$version-win-x64-setup.exe"
foreach ($path in @($zipPath, $setupPath, "$zipPath.sha256", "$setupPath.sha256")) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
if ($IncludeArm64) {
    $arm = Join-Path $publishRoot 'win-arm64'
    if (Test-Path -LiteralPath $arm) { Remove-Item -LiteralPath $arm -Recurse -Force }
}

& (Join-Path $PSScriptRoot 'generate-icons.ps1')
if (-not $?) { throw 'Branding icon generation failed.' }
Invoke-DotNet @('restore', 'AutoClicker.sln', '-m:1')
if (-not $SkipTests) {
    Invoke-DotNet @('build', 'AutoClicker.sln', '-c', 'Debug', '-m:1', '--no-restore')
    Invoke-DotNet @('test', 'AutoClicker.sln', '-c', 'Debug', '-m:1', '--no-build')
    Invoke-DotNet @('build', 'AutoClicker.sln', '-c', 'Release', '-m:1', '--no-restore')
    Invoke-DotNet @('test', 'AutoClicker.sln', '-c', 'Release', '-m:1', '--no-build')
}
Invoke-DotNet @('publish', 'AutoClicker.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', 'artifacts/publish/win-x64')
$exe = Join-Path $root 'artifacts\publish\win-x64\AutoClicker.exe'
if (-not (Test-Path $exe)) { throw 'Published Windows executable was not created.' }
$smokeData = Join-Path ([IO.Path]::GetTempPath()) ('autoclicker-smoke-' + [guid]::NewGuid().ToString('N'))
try {
    $smoke = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', ('"--data-dir={0}"' -f $smokeData)) -WindowStyle Hidden -Wait -PassThru
    if ($smoke.ExitCode -ne 0) { throw "Published executable smoke test failed with exit code $($smoke.ExitCode)." }
} finally {
    if (Test-Path -LiteralPath $smokeData) { Remove-Item -LiteralPath $smokeData -Recurse -Force }
}

$zip = $zipPath
$stage = Join-Path $releaseRoot 'AutoClicker'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -Path (Join-Path $root 'artifacts\publish\win-x64\*') -Destination $stage -Recurse -Force
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal -Force
Remove-Item -LiteralPath $stage -Recurse -Force
$files = @($zip)
if ($IncludeArm64) {
    Invoke-DotNet @('publish', 'AutoClicker.csproj', '-c', 'Release', '-r', 'win-arm64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', 'artifacts/publish/win-arm64')
}
if (-not $SkipInstaller) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $isccPath = if ($iscc) { $iscc.Source } else { $null }
    if (-not $isccPath) { $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'; if (Test-Path $candidate) { $isccPath = $candidate } }
    if ($isccPath) { & $isccPath "/DAppVersion=$version" (Join-Path $root 'packaging\windows\AutoClicker.iss'); if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }; $files += $setupPath }
    else { Write-Warning 'Inno Setup is unavailable; installer configuration exists but no setup.exe was generated.' }
}
foreach ($file in $files) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
    Set-Content -NoNewline -Encoding ascii -LiteralPath "$file.sha256" -Value "$hash  $(Split-Path -Leaf $file)`n"
}
$manifest = [ordered]@{ version = $version; artifacts = @($files | ForEach-Object { [ordered]@{ platform = 'windows'; arch = 'x64'; type = if ($_ -like '*portable.zip') { 'portable' } else { 'installer' }; file = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant() } }) }
$manifestPath = Join-Path $releaseRoot 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 -LiteralPath $manifestPath
Write-Host 'Release artifacts:'
$files | ForEach-Object { Write-Host (Resolve-Path $_).Path; Write-Host (Resolve-Path "$_.sha256").Path }
Write-Host (Resolve-Path $manifestPath).Path
