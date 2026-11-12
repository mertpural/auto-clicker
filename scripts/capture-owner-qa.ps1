[CmdletBinding()]
param([string]$Application = 'bin/Release/net8.0/AutoClicker.dll')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
$cases = @(
    @('turkish-night-general','Night','Turkish','General','Profiles'),
    @('turkish-night-advanced','Night','Turkish','Advanced','Profiles'),
    @('turkish-night-position','Night','Turkish','Position','Profiles'),
    @('turkish-night-statistics','Night','Turkish','Statistics','Profiles'),
    @('turkish-night-settings','Night','Turkish','General','Settings'),
    @('turkish-crimson-gold-general','CrimsonGold','Turkish','General','Profiles'),
    @('turkish-crimson-gold-settings','CrimsonGold','Turkish','General','Settings'),
    @('english-graphite-general','Graphite','English','General','Profiles'),
    @('turkish-light-general','Light','Turkish','General','Profiles'),
    @('about','Night','Turkish','General','About')
)
foreach ($case in $cases) {
    $arguments = @('--smoke-test','--preview-size=1000x800',"--preview-theme=$($case[1])","--preview-language=$($case[2])",
        "--preview-tab=$($case[3])","--preview-page=$($case[4])","--data-dir=artifacts/owner-audit/screens/$($case[0])",
        "--screenshot=docs/screenshots/owner-audit/$($case[0]).png")
    if ($Application.EndsWith('.dll')) { & dotnet $Application @arguments; $exitCode = $LASTEXITCODE }
    else { $process = Start-Process -FilePath (Resolve-Path $Application).Path -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru; $exitCode = $process.ExitCode }
    if ($exitCode -ne 0) { throw "Screenshot failed: $($case[0])" }
    Write-Host "Captured: $($case[0])"
}
