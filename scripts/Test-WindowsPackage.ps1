param([Parameter(Mandatory)][string] $Version, [switch] $UpgradeFromV1)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $setup = Get-ChildItem -LiteralPath 'artifacts/releases' -Filter '*-Setup.exe'
    if (@($setup).Count -ne 1) {
        throw 'Expected exactly one Velopack installer.'
    }

    $env:WINLAYER_SMOKE_UPGRADE_PACKAGE = $null
    $logName = 'setup-smoke.log'
    if ($UpgradeFromV1) {
        $v1 = Join-Path (Get-Location).Path 'artifacts/WinLayer-1.0.0-Setup.exe'
        Invoke-WebRequest 'https://github.com/thexch/WinLayer/releases/download/v1.0.0/WinLayer-win-Setup.exe' -OutFile $v1
        if ((Get-FileHash -LiteralPath $v1).Hash -ne '2030D5E68AC987DA64DEA39ED97BC01BABCE5FDFe7F86012CC164EEC001405D7') {
            throw 'The archived 1.0.0 installer SHA-256 does not match.'
        }
        $setup = Get-Item -LiteralPath $v1
        $full = Get-ChildItem -LiteralPath 'artifacts/releases' -Filter '*-full.nupkg'
        if (@($full).Count -ne 1) { throw 'Expected exactly one update package.' }
        $env:WINLAYER_SMOKE_UPGRADE_PACKAGE = $full.FullName
        $logName = 'setup-upgrade-smoke.log'
    }

    $installTo = Join-Path ([IO.Path]::GetTempPath()) "WinLayer-smoke-$([Guid]::NewGuid().ToString('N'))"
    $setupLog = Join-Path (Get-Location).Path (Join-Path 'artifacts' $logName)
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

    # The renderer must leave the dedicated user-data folder when its host closes.
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $browser = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
            Where-Object { $_.CommandLine -like '*WinLayerData*WebView2*' }
        if ($browser) { Start-Sleep -Milliseconds 500 }
    } while ($browser -and [DateTime]::UtcNow -lt $deadline)
    if ($browser) { throw 'WinLayer WebView2 processes remained after closing its settings window.' }
}
finally {
    Pop-Location
}
