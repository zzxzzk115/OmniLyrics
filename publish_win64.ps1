$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    foreach ($app in @('Cli', 'Gui')) {
        $output = "publish/win-x64/$($app.ToLowerInvariant())"
        # Remove native libraries left by older, non-bundled publishes.
        if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
        dotnet publish "src/OmniLyrics.$app" -c Release -r win-x64 -o $output
        if ($LASTEXITCODE -ne 0) { throw "Publishing $app failed." }
        $files = @(Get-ChildItem -LiteralPath $output -Recurse -File -Force)
        $name = "OmniLyrics.$app.exe"
        if ($files.Count -ne 1 -or $files[0].Name -ne $name) {
            throw "Expected only $name; found: $($files.Name -join ', ')"
        }
    }
} finally {
    Pop-Location
}
