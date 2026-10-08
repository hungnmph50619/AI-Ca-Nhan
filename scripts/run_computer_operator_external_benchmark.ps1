param(
    [string]$BaseUrl = "http://localhost:5188",
    [string]$OutputDirectory = "",
    [switch]$ConfirmDesktopActions
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmDesktopActions) {
    throw "Benchmark will control the real desktop. Re-run with -ConfirmDesktopActions after closing sensitive data and preparing to observe the screen."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputDirectory = Join-Path $PWD "benchmark-results\computer-operator-$timestamp"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Invoke-PersonalAiJson {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST")]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [object]$Body = $null
    )

    $uri = "$($BaseUrl.TrimEnd('/'))$Path"

    if ($Method -eq "GET") {
        return Invoke-RestMethod -Method Get -Uri $uri
    }

    $json = if ($null -eq $Body) { "{}" } else { $Body | ConvertTo-Json -Depth 20 }
    return Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json; charset=utf-8" -Body $json
}

function Save-Json {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $path = Join-Path $OutputDirectory $Name
    $Value | ConvertTo-Json -Depth 30 | Set-Content -Path $path -Encoding utf8
    return $path
}

Write-Host "== PersonalAI Computer Operator External Benchmark =="
Write-Host "Base URL: $BaseUrl"
Write-Host "Output:   $OutputDirectory"
Write-Host ""

Write-Host "[1/6] Check server..."
$status = Invoke-PersonalAiJson -Method GET -Path "/api/evaluation/status"
Save-Json -Name "01-status.json" -Value $status | Out-Null

Write-Host "[2/6] Install/sync scenario pack..."
$install = Invoke-PersonalAiJson -Method POST -Path "/api/evaluation/computer-operator-benchmark/scenarios/install" -Body @{ confirmed = $true }
Save-Json -Name "02-scenario-install.json" -Value $install | Out-Null

Write-Host "[3/6] Check regression readiness..."
$readiness = Invoke-PersonalAiJson -Method GET -Path "/api/evaluation/computer-operator-regression/readiness"
Save-Json -Name "03-readiness.json" -Value $readiness | Out-Null

if (-not $readiness.passed) {
    Write-Host ""
    Write-Host "Regression readiness has not passed. Desktop benchmark will not run." -ForegroundColor Yellow
    Write-Host "See: $(Join-Path $OutputDirectory '03-readiness.json')"
    exit 2
}

Write-Host "[4/6] Prepare all external benchmark cases..."
$prepare = Invoke-PersonalAiJson -Method POST -Path "/api/evaluation/computer-operator-benchmark/prepare" -Body @{ caseIds = $null; maximumCases = 20 }
Save-Json -Name "04-prepare.json" -Value $prepare | Out-Null

Write-Host "[5/6] Run benchmark suite sequentially on the real desktop..."
$suite = Invoke-PersonalAiJson -Method POST -Path "/api/evaluation/computer-operator-benchmark/run-suite" -Body @{ confirmed = $true }
Save-Json -Name "05-suite-result.json" -Value $suite | Out-Null

Write-Host "[6/6] Read latest result for each case..."
$latest = @()
foreach ($item in $suite.results) {
    $caseId = [string]$item.caseId
    $encoded = [Uri]::EscapeDataString($caseId)
    $result = Invoke-PersonalAiJson -Method GET -Path "/api/evaluation/computer-operator-benchmark/latest/$encoded"
    $latest += $result
}

Save-Json -Name "06-latest-results.json" -Value $latest | Out-Null

Write-Host ""
Write-Host "== Execution results =="
$latest | Select-Object caseId, taskCompleted, withinStepBudget, readyForIndependentEvaluation, recordedSteps, verifiedActionCount, verificationFailureCount, verificationInconclusiveCount, recoveryCount, durationMilliseconds | Format-Table -AutoSize

Write-Host ""
Write-Host ("Completed: {0}/{1}" -f $suite.completedCases, $suite.plannedCases)
Write-Host ("Ready for independent evaluation: {0}/{1}" -f $suite.readyForIndependentEvaluationCases, $suite.plannedCases)
Write-Host "Evidence JSON saved at: $OutputDirectory"
Write-Host ""

if (-not $suite.completed -or $suite.completedCases -ne $suite.plannedCases -or $suite.readyForIndependentEvaluationCases -ne $suite.plannedCases) {
    Write-Host "Suite is not ready for independent evaluation." -ForegroundColor Yellow
    exit 3
}

Write-Host "Execution suite completed. Next step: independently review evidence for each case; this script intentionally does not self-grade PASS." -ForegroundColor Green
exit 0
