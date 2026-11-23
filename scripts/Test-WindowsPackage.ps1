param([Parameter(Mandatory)][string] $Version, [switch] $UpgradeFromV1, [switch] $UpgradeFromV24, [switch] $UpgradeFromV312)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $setup = Get-ChildItem -LiteralPath 'artifacts/releases' -Filter '*-Setup.exe'
    if (@($setup).Count -ne 1) {
        throw 'Expected exactly one Velopack installer.'
    }

    $env:WINLAYER_SMOKE_UPGRADE_PACKAGE = $null
    $env:WINLAYER_SMOKE_UPGRADE_VERSION = $null
    $logName = 'setup-smoke.log'
    if ($UpgradeFromV1 -or $UpgradeFromV24 -or $UpgradeFromV312) {
        $source = if ($UpgradeFromV312) { '3.1.2' } elseif ($UpgradeFromV24) { '2.4.0' } else { '1.0.0' }
        $sourceHash = @{
            '1.0.0' = '2030D5E68AC987DA64DEA39ED97BC01BABCE5FDFe7F86012CC164EEC001405D7'
            '2.4.0' = '3D077EDAC6DC847C30E86DFBE7803CED08C65D6FACD01B6DC4E2D0BE8C0CFCE5'
            '3.1.2' = 'F6EEF6F9B2E20DAF63C653FE41028797ACB79F22D95FECF1E7E28654EA13D8D6'
        }[$source]
        $archive = Join-Path (Get-Location).Path "artifacts/WinLayer-$source-Setup.exe"
        Invoke-WebRequest "https://github.com/thexch/WinLayer/releases/download/v$source/WinLayer-win-Setup.exe" -OutFile $archive
        if ((Get-FileHash -LiteralPath $archive).Hash -ne $sourceHash) {
            throw "The archived $source installer SHA-256 does not match."
        }
        $setup = Get-Item -LiteralPath $archive
        $full = Get-ChildItem -LiteralPath 'artifacts/releases' -Filter '*-full.nupkg'
        if (@($full).Count -ne 1) { throw 'Expected exactly one update package.' }
        $env:WINLAYER_SMOKE_UPGRADE_PACKAGE = $full.FullName
        $env:WINLAYER_SMOKE_UPGRADE_VERSION = $source
        $logName = "setup-upgrade-$source-smoke.log"
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

    try {
        dotnet test WinLayer.slnx -c Release --no-build --logger trx
    }
    catch {
        $agentLog = Join-Path $env:LOCALAPPDATA 'WinLayerData/agent.log'
        if (Test-Path -LiteralPath $agentLog) { Get-Content -LiteralPath $agentLog -Tail 50 }
        $eventFilter = @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-5); Level = 2 }
        Get-WinEvent -FilterHashtable $eventFilter -ErrorAction SilentlyContinue | Where-Object Message -Like '*WinLayer*' |
            Select-Object -First 5 | Format-List
        throw
    }

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
