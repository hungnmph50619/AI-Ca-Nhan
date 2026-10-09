param(
    [string]$BaseUrl = "http://localhost:5188",
    [string]$OutputDirectory = "",
    [switch]$ConfirmDesktopActions
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmDesktopActions) {
    throw "Benchmark will control the real desktop. Re-run with -ConfirmDesktopActions after closing sensitive data and preparing to observe the screen."
}

# Keep every run in one predictable, user-facing location.
$benchmarkStartedAt = Get-Date
$runId = "TEST-" + $benchmarkStartedAt.ToString("yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 6)
$reportsRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "BENCHMARK-REPORTS"
$runFolder = Join-Path $reportsRoot $runId
$evidenceFolder = Join-Path $runFolder "visual-evidence"
New-Item -ItemType Directory -Force -Path $runFolder | Out-Null
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runFolder "benchmark-json"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$transcriptStarted = $false
try {
    Start-Transcript -Path (Join-Path $runFolder "powershell-transcript.txt") -Force -ErrorAction Stop | Out-Null
    $transcriptStarted = $true
} catch {
    Write-Warning "Unable to start transcript: $($_.Exception.Message)"
}
$benchmarkOutcome = "FAILED"


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

try {
# Fail early if the local checkout predates the grounding/verification/recovery fixes.
# Keep this inside the main try so even a rejected run produces a diagnostic ZIP.
$repoRoot = Split-Path -Parent $PSScriptRoot
$requiredBaseline = "1cf946b27f6d3a46b101c461f50a646261450ad7"
$requiredBranch = "experiment/computer-operator-v4-4-0-local-visual-sensors"
$localBranch = [string](& git -C $repoRoot branch --show-current 2>$null | Select-Object -First 1)
$localBranch = $localBranch.Trim()
if ($LASTEXITCODE -ne 0 -or $localBranch -cne $requiredBranch) {
    throw "BENCHMARK_VERSION_MISMATCH: Expected branch $requiredBranch, got '$localBranch'. Switch branch and git pull --ff-only before testing."
}
$localCommit = (& git -C $repoRoot rev-parse HEAD 2>$null | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($localCommit)) {
    throw "BENCHMARK_VERSION_MISMATCH: Cannot resolve local HEAD."
}
& git -C $repoRoot merge-base --is-ancestor $requiredBaseline HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "BENCHMARK_VERSION_MISMATCH: HEAD $localCommit does not include required baseline $requiredBaseline. Run git fetch and git pull --ff-only before testing."
}
Write-Host "Version preflight PASS: $localBranch @ $localCommit" -ForegroundColor Green

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
    throw "Regression readiness did not pass; see 03-readiness.json"
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
    throw "Suite is not ready for independent evaluation"
}

Write-Host "Execution suite completed. Next step: independently review evidence for each case; this script intentionally does not self-grade PASS." -ForegroundColor Green
$benchmarkOutcome = "SUCCEEDED"
} catch {
    $errorRecord = $_
    $responseBody = $null
    if ($errorRecord.ErrorDetails -and $errorRecord.ErrorDetails.Message) {
        $responseBody = [string]$errorRecord.ErrorDetails.Message
    } elseif ($errorRecord.Exception.Response) {
        try {
            $stream = $errorRecord.Exception.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                try { $responseBody = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
        } catch { $responseBody = "Could not extract HTTP response body: $($_.Exception.Message)" }
    }
    $httpStatus = $null
    if ($errorRecord.Exception.Response) {
        try { $httpStatus = [int]$errorRecord.Exception.Response.StatusCode } catch {}
    }
    @{
        runId = $runId
        timestampUtc = [DateTime]::UtcNow.ToString("o")
        message = [string]$errorRecord.Exception.Message
        category = [string]$errorRecord.CategoryInfo.Category
        scriptStackTrace = [string]$errorRecord.ScriptStackTrace
        httpStatus = $httpStatus
        responseBody = $responseBody
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $runFolder "error-details.json") -Encoding UTF8
    Write-Host "Benchmark FAILED: $($errorRecord.Exception.Message)" -ForegroundColor Red
    if ($responseBody) { Write-Host "Server response: $responseBody" -ForegroundColor Yellow }
    $benchmarkOutcome = "FAILED"
} finally {
    # Recorder is best effort; preserve original benchmark outcome if evidence export fails.
    try {
        $sourceEvidence = Join-Path (Split-Path -Parent $PSScriptRoot) "benchmark-results\operator-diagnostics"
        if (Test-Path $sourceEvidence) {
            $recent = @(Get-ChildItem -LiteralPath $sourceEvidence -Directory -ErrorAction Stop |
                Where-Object { $_.LastWriteTime -ge $benchmarkStartedAt.AddMinutes(-2) })
            if ($recent.Count -gt 0) {
                New-Item -ItemType Directory -Force -Path $evidenceFolder | Out-Null
                foreach ($item in $recent) {
                    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $evidenceFolder $item.Name) -Recurse -Force -ErrorAction Stop
                }
            }
        }
        if (-not (Test-Path $evidenceFolder) -or
            @(Get-ChildItem -Path $evidenceFolder -File -Recurse -ErrorAction SilentlyContinue).Count -eq 0) {
            "No recorded images were found. Recorder might not have started or its capture failed. Source path: $sourceEvidence" |
                Set-Content (Join-Path $runFolder "VISUAL-EVIDENCE-MISSING.txt") -Encoding UTF8
        }
    } catch {
        [string]$_.Exception.Message | Set-Content (Join-Path $runFolder "evidence-export-warning.txt") -Encoding UTF8
    }
    try {
        $commitHash = $null
        try { $commitHash = (& git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD 2>$null | Select-Object -First 1) } catch {}
        @{
            runId = $runId
            startedAt = $benchmarkStartedAt.ToString("o")
            endedAt = (Get-Date).ToString("o")
            outcome = $benchmarkOutcome
            repositoryCommit = $commitHash
            outputDirectory = $OutputDirectory
            evidenceRuns = @(if (Test-Path $evidenceFolder) { Get-ChildItem $evidenceFolder -Directory | Select-Object -ExpandProperty Name })
            notice = "Evidence runs are selected by modification time and might include other overlapping executions."
        } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $runFolder "manifest.json") -Encoding UTF8
    } catch { Write-Warning "Could not save benchmark manifest: $($_.Exception.Message)" }
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
    try {
        $zipPath = Join-Path $reportsRoot ($runId + ".zip")
        Compress-Archive -LiteralPath $runFolder -DestinationPath $zipPath -Force -ErrorAction Stop
        Write-Host "DIAGNOSTIC ZIP: $zipPath" -ForegroundColor Cyan
        Write-Host "Open folder: $reportsRoot" -ForegroundColor Cyan
        try { Set-Content -LiteralPath (Join-Path $reportsRoot "LATEST-ZIP.txt") -Value $zipPath -Encoding UTF8 } catch {}
    } catch {
        Write-Warning "ZIP packaging failed; uncompressed run remains at $runFolder. $($_.Exception.Message)"
    }
}

if ($benchmarkOutcome -ne "SUCCEEDED") { exit 1 }
exit 0
