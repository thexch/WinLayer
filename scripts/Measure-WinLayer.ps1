param(
    [Parameter(Mandatory)]
    [int] $AgentPid,
    [ValidateRange(10, 3600)]
    [int] $Seconds = 120,
    [string] $Scenario = 'Agent'
)

$ErrorActionPreference = 'Stop'
$process = Get-Process -Id $AgentPid
if ($process.ProcessName -ne 'WinLayer') {
    throw "PID $AgentPid does not refer to WinLayer.exe."
}

$cpuStart = $process.TotalProcessorTime.TotalSeconds
$clock = [Diagnostics.Stopwatch]::StartNew()
$workingSets = [Collections.Generic.List[double]]::new()
$privateBytes = [Collections.Generic.List[double]]::new()
while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
    $process.Refresh()
    $workingSets.Add($process.WorkingSet64 / 1MB)
    $privateBytes.Add($process.PrivateMemorySize64 / 1MB)
    Start-Sleep -Seconds 1
}
$process.Refresh()
$cpuDelta = $process.TotalProcessorTime.TotalSeconds - $cpuStart
[pscustomobject]@{
    Scenario            = $Scenario
    ElapsedSeconds      = [Math]::Round($clock.Elapsed.TotalSeconds, 2)
    AgentCpuMs          = [Math]::Round($cpuDelta * 1000, 2)
    CpuPctOfOneCore     = [Math]::Round(100 * $cpuDelta / $clock.Elapsed.TotalSeconds, 3)
    WorkingSetMeanMiB   = [Math]::Round(($workingSets | Measure-Object -Average).Average, 2)
    WorkingSetPeakMiB   = [Math]::Round(($workingSets | Measure-Object -Maximum).Maximum, 2)
    PrivateBytesMeanMiB = [Math]::Round(($privateBytes | Measure-Object -Average).Average, 2)
}
