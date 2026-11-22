param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-ci\.[1-9][0-9]*)?$')]
    [string] $Version,
    [switch] $UseExistingWebAssets
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Push-Location (Join-Path $PSScriptRoot '..')
try {
    if (!$UseExistingWebAssets) {
        npm ci --prefix web
        npm run build --prefix web
    }

    dotnet tool restore
    $artifactRoot = Join-Path (Get-Location).Path artifacts
    $agent = Join-Path $artifactRoot agent
    $settings = Join-Path $artifactRoot settings
    $releases = Join-Path $artifactRoot releases
    foreach ($directory in @($agent, $settings, $releases)) {
        if (Test-Path -LiteralPath $directory) {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }

    dotnet publish src/WinLayer.Agent -c Release -r win-x64 --self-contained -o $agent "-p:Version=$Version"
    dotnet publish src/WinLayer.Settings -c Release -r win-x64 --self-contained -o $settings "-p:Version=$Version"

    # Both processes use the same runtime files. Reject differing copies of any shared file.
    foreach ($file in Get-ChildItem -LiteralPath $settings -File -Recurse) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = [IO.Path]::GetRelativePath($settings, $file.FullName)
        $target = Join-Path $agent $relative
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                throw "Conflicting package file $relative"
            }
        }
        else {
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }

    dotnet vpk pack --packId WinLayer --packTitle WinLayer --packAuthors WinLayer --packVersion $Version `
        --mainExe WinLayer.exe --packDir $agent --outputDir $releases --runtime win-x64 `
        --icon src/WinLayer.Agent/WinLayer.ico --shortcuts StartMenuRoot --delta None --noPortable

    $setup = Get-ChildItem -LiteralPath $releases -Filter '*-Setup.exe'
    $full = Get-ChildItem -LiteralPath $releases -Filter '*-full.nupkg'
    $feed = Get-Content -LiteralPath (Join-Path $releases 'releases.win.json') -Raw | ConvertFrom-Json
    if (@($setup).Count -ne 1 -or @($full).Count -ne 1 -or $feed.Assets[0].SHA256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Expected one Windows installer, a full package and a SHA-256 update feed.'
    }
    if ((Get-FileHash -LiteralPath $full.FullName).Hash -ne $feed.Assets[0].SHA256) {
        throw 'The update feed checksum does not match the full package.'
    }
}
finally {
    Pop-Location
}
