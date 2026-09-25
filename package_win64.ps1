$ErrorActionPreference = 'Stop'
$project = [xml](Get-Content -Raw (Join-Path $PSScriptRoot 'Directory.Build.props'))
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (-not $version) { throw 'Project version is missing.' }
Push-Location $PSScriptRoot
try {
    & "$env:INNO_ROOT\ISCC.exe" "/DMyAppVersion=$version" 'windows_installer.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
} finally {
    Pop-Location
}
