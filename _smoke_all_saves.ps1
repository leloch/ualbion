# Loads every save with --startuponly using direct invocation (Start-Process mangles args).
$saves = @(1,2,3,4,5,6,7,8,9,10,11,12,100)
$exe   = 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0\UAlbion.exe'
$exeDir = Split-Path $exe
$logDir = 'F:\Dev\albion\ualbion\_smoke_logs'
if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Push-Location $exeDir
try {
    $results = foreach ($id in $saves) {
        $log = Join-Path $logDir "load_$id.log"
        $cmd = "load_game $id"
        # Direct invocation - identical to manual run
        & $exe -d3d -c $cmd --startuponly *> $log
        $exit = $LASTEXITCODE
        $body = Get-Content $log -Raw -ErrorAction SilentlyContinue
        if (-not $body) { $body = '' }
        $clean = ($body -match 'Exiting') -and -not ($body -match 'Unhandled exception|Fatal error|0xC0000005')
        $mapLine = ($body -split "`r?`n" | Where-Object { $_ -match 'Loaded map' } | Select-Object -First 1)
        $mapStr = if ($mapLine) { $mapLine.Trim() } else { '<no map loaded>' }
        $excLine = ($body -split "`r?`n" | Where-Object { $_ -match 'Exception:|Fatal error|0xC0000005' } | Select-Object -First 1)
        $excStr  = if ($excLine) { $excLine.Trim() } else { '' }
        [pscustomobject]@{
            Slot     = $id
            Exit     = $exit
            Clean    = $clean
            Map      = $mapStr
            Failure  = $excStr
        }
    }
}
finally { Pop-Location }

$results | Format-Table -AutoSize -Wrap
$results | Export-Csv -NoTypeInformation 'F:\Dev\albion\ualbion\_smoke_summary.csv'
$cleanCount = ($results | Where-Object { $_.Clean }).Count
"Clean: $cleanCount / $($results.Count)"
