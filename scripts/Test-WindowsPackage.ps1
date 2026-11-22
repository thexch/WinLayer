param([Parameter(Mandatory)][string] $Version)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $setup = Get-ChildItem -LiteralPath 'artifacts/releases' -Filter '*-Setup.exe'
    if (@($setup).Count -ne 1) {
        throw 'Expected exactly one Velopack installer.'
    }

    $installTo = Join-Path ([IO.Path]::GetTempPath()) "WinLayer-smoke-$([Guid]::NewGuid().ToString('N'))"
    $setupLog = Join-Path (Get-Location).Path 'artifacts/setup-smoke.log'
    $start = [Diagnostics.ProcessStartInfo]::new($setup.FullName)
    foreach ($argument in @('--silent', '--installto', $installTo, '--log', $setupLog)) {
        $start.ArgumentList.Add($argument)
    }
    $installer = [Diagnostics.Process]::Start($start)
    try {
        if (!$installer.WaitForExit(90000)) {
            $installer.Kill()
            throw 'The silent installation timed out.'
        }
        if ($installer.ExitCode -ne 0) {
            if (Test-Path -LiteralPath $setupLog) { Get-Content -LiteralPath $setupLog -Tail 40 }
            throw "The installer exited with code $($installer.ExitCode)."
        }
    }
    finally {
        $installer.Dispose()
    }

    $env:WINLAYER_SMOKE_EXE = Join-Path $installTo 'current/WinLayer.exe'
    $env:WINLAYER_SMOKE_VERSION = $Version
    foreach ($binary in @($env:WINLAYER_SMOKE_EXE, (Join-Path $installTo 'current/WinLayer.Settings.exe'))) {
        if (!(Test-Path -LiteralPath $binary -PathType Leaf)) {
            throw "Expected packaged executable at $binary"
        }
    }

    dotnet test WinLayer.slnx -c Release --no-build --logger trx
}
finally {
    Pop-Location
}
