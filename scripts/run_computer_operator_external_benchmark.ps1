param(
    [string]$BaseUrl = "http://localhost:5188",
    [string]$OutputDirectory = "",
    [switch]$ConfirmDesktopActions
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmDesktopActions) {
    throw "Benchmark sẽ điều khiển desktop thật. Chạy lại với -ConfirmDesktopActions sau khi đã đóng dữ liệu nhạy cảm và sẵn sàng quan sát màn hình."
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

Write-Host "[1/6] Kiểm tra server..."
$status = Invoke-PersonalAiJson -Method GET -Path "/api/evaluation/status"
Save-Json -Name "01-status.json" -Value $status | Out-Null

Write-Host "[2/6] Cài/đồng bộ scenario pack..."
$install = Invoke-PersonalAiJson -Method POST -Path "/api/evaluation/computer-operator-benchmark/scenarios/install" -Body @{ confirmed = $true }
Save-Json -Name "02-scenario-install.json" -Value $install | Out-Null

Write-Host "[3/6] Kiểm tra regression readiness..."
$readiness = Invoke-PersonalAiJson -Method GET -Path "/api/evaluation/computer-operator-regression/readiness"
Save-Json -Name "03-readiness.json" -Value $readiness | Out-Null

if (-not $readiness.passed) {
    Write-Host ""
    Write-Host "Regression readiness chưa PASS. Không chạy desktop benchmark." -ForegroundColor Yellow
    Write-Host "Xem: $(Join-Path $OutputDirectory '03-readiness.json')"
    exit 2
}

Write-Host "[4/6] Prepare toàn bộ external benchmark cases..."
$prepare = Invoke-PersonalAiJson -Method POST -Path "/api/evaluation/computer-operator-benchmark/prepare" -Body @{ caseIds = $null; maximumCases = 20 }
Save-Json -Name "04-prepare.json" -Value $prepare | Out-Null

Write-Host "[5/6] Chạy tuần tự benchmark suite trên desktop thật..."
$suite = Invoke-PersonalAiJson -Method POST -Path "/api/evaluation/computer-operator-benchmark/run-suite" -Body @{ confirmed = $true }
Save-Json -Name "05-suite-result.json" -Value $suite | Out-Null

Write-Host "[6/6] Đọc lại latest result từng case..."
$latest = @()
foreach ($item in $suite.results) {
    $caseId = [string]$item.caseId
    $encoded = [Uri]::EscapeDataString($caseId)
    $result = Invoke-PersonalAiJson -Method GET -Path "/api/evaluation/computer-operator-benchmark/latest/$encoded"
    $latest += $result
}

Save-Json -Name "06-latest-results.json" -Value $latest | Out-Null

Write-Host ""
Write-Host "== Kết quả execution =="
$latest | Select-Object caseId, taskCompleted, withinStepBudget, readyForIndependentEvaluation, recordedSteps, verifiedActionCount, verificationFailureCount, verificationInconclusiveCount, recoveryCount, durationMilliseconds | Format-Table -AutoSize

Write-Host ""
Write-Host ("Completed: {0}/{1}" -f $suite.completedCases, $suite.plannedCases)
Write-Host ("Ready for independent evaluation: {0}/{1}" -f $suite.readyForIndependentEvaluationCases, $suite.plannedCases)
Write-Host "Evidence JSON đã lưu tại: $OutputDirectory"
Write-Host ""

if (-not $suite.completed -or $suite.completedCases -ne $suite.plannedCases -or $suite.readyForIndependentEvaluationCases -ne $suite.plannedCases) {
    Write-Host "Suite chưa đủ điều kiện để chuyển sang independent evaluation." -ForegroundColor Yellow
    exit 3
}

Write-Host "Execution suite đã hoàn tất. Bước kế tiếp: review evidence độc lập cho từng case; script này cố ý không tự chấm PASS." -ForegroundColor Green
exit 0
