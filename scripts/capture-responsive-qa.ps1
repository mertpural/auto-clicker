[CmdletBinding()]
param([string]$Application = 'bin/Release/net8.0/AutoClicker.dll')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
foreach ($size in @('980x760', '1000x800', '1200x800', '1920x1080')) {
    foreach ($language in @('Turkish', 'English')) {
        foreach ($page in @('Profiles', 'Settings')) {
            $tab = if ($language -eq 'Turkish') { 'Advanced' } else { 'General' }
            $name = "$size-$language-$page"
            $arguments = @('--smoke-test', "--preview-size=$size", '--preview-theme=Night',
                "--preview-language=$language", "--preview-tab=$tab", "--preview-page=$page",
                "--data-dir=artifacts/owner-audit/responsive/$name",
                "--screenshot=docs/screenshots/owner-audit/responsive/$name.png")
            if ($Application.EndsWith('.dll')) { & dotnet $Application @arguments; $exitCode = $LASTEXITCODE }
            else { $process = Start-Process -FilePath (Resolve-Path $Application).Path -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru; $exitCode = $process.ExitCode }
            if ($exitCode -ne 0) { throw "Responsive screenshot failed: $name" }
            Write-Host "Captured: $name"
        }
    }
}
