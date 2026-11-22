param([switch] $Background)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build WinLayer.slnx
    $agent = Resolve-Path 'src/WinLayer.Agent/bin/Debug/net10.0-windows10.0.26100.0/WinLayer.exe'
    $settings = Resolve-Path 'src/WinLayer.Settings/bin/Debug/net10.0-windows10.0.26100.0/WinLayer.Settings.exe'
    $previousSettings = $env:WINLAYER_SETTINGS_EXE
    try {
        $env:WINLAYER_SETTINGS_EXE = $settings.Path
        if ($Background) {
            Start-Process -FilePath $agent.Path -ArgumentList '--background'
        }
        else {
            Start-Process -FilePath $agent.Path
        }
    }
    finally {
        $env:WINLAYER_SETTINGS_EXE = $previousSettings
    }
}
finally {
    Pop-Location
}
