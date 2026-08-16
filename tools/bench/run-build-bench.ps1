<#
.SYNOPSIS
  브랜치별 빌드 실측 자동화 — 각 변형 브랜치를 체크아웃해 IL2CPP 빌드하고, 빌드를 실행해 CSV를 모은다.

.DESCRIPTION
  래더(cs-naive → single → parallel → grid)처럼 **코드가 다른 변형**은 브랜치로 나뉘어 있다.
  이 스크립트는 변형마다: git checkout → Unity 배치모드 빌드 → 빌드 실행(-bench) → CSV 수집.

  ⚠️ 전제
    - Unity 에디터를 **닫아야 한다**(배치모드가 프로젝트 락을 잡는다).
    - 워킹트리가 깨끗해야 한다(체크아웃으로 작업이 날아가지 않게).
    - 프로파일러 카운터(gpu_ms·드로우콜·시스템 마커)는 **Development Build**에서만 나온다 → 빌드 스크립트가 항상 Development로 만든다.
    - 측정끼리 비교는 **같은 기계·같은 세션**에서. 에디터 수치와 직접 비교하지 말 것(오버헤드가 다르다).

.EXAMPLE
  # main(grid)만 재기
  ./tools/bench/run-build-bench.ps1 -Variants "grid=main"

  # 래더 4단 (변형 브랜치를 미리 만들어 둔 경우)
  ./tools/bench/run-build-bench.ps1 -Variants "cs-naive=bench/build-cs-naive","single=bench/build-single","parallel=bench/build-parallel","grid=main" -Counts "1000,2000,3000"
#>
param(
    # "라벨=브랜치" 목록. 라벨은 CSV 첫 열이자 파일명이 된다.
    [string[]]$Variants = @("grid=main"),

    [string]$Counts = "1000,2500,5000,10000",
    [int]$Warmup = 300,
    [int]$Sample = 120,

    [string]$UnityPath = "",
    [string]$OutDir = "docs/benchmarks/build",

    # 빌드를 건너뛰고 기존 빌드로 실행만(같은 브랜치 재측정용)
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $repo

function Find-Unity {
    if ($UnityPath -and (Test-Path $UnityPath)) { return $UnityPath }
    $version = (Select-String -Path "ProjectSettings/ProjectVersion.txt" -Pattern "m_EditorVersion: (.+)").Matches[0].Groups[1].Value.Trim()
    $candidate = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
    if (Test-Path $candidate) { return $candidate }
    throw "Unity 실행 파일을 못 찾았습니다(버전 $version). -UnityPath 로 직접 지정하세요."
}

# ── 사전 점검 ────────────────────────────────────────────────
$dirty = git status --porcelain | Where-Object { $_ -notmatch '^\?\? \.(uloop|claude)/' }
if ($dirty) { throw "워킹트리가 깨끗하지 않습니다. 커밋/스태시 후 다시 실행하세요.`n$($dirty -join "`n")" }

$unity = Find-Unity
$original = (git rev-parse --abbrev-ref HEAD).Trim()
$exe = Join-Path $repo "Builds/Bench/ecs-warriors-bench.exe"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path "Builds/Bench/logs" | Out-Null

Write-Host "Unity   : $unity"
Write-Host "원 브랜치: $original"
Write-Host "변형    : $($Variants -join ', ')"
Write-Host "카운트  : $Counts (warmup=$Warmup sample=$Sample)`n"

$results = @()
try {
    foreach ($v in $Variants) {
        $parts = $v.Split("=", 2)
        if ($parts.Count -ne 2) { throw "변형 형식은 '라벨=브랜치' 입니다: $v" }
        $label = $parts[0]; $branch = $parts[1]

        Write-Host "── [$label] 브랜치 $branch ──────────────────────────"
        git checkout $branch --quiet
        if ($LASTEXITCODE -ne 0) { throw "체크아웃 실패: $branch" }

        if (-not $SkipBuild) {
            Write-Host "  빌드 중... (몇 분 걸립니다)"
            $log = "Builds/Bench/logs/build-$label.log"
            # ⚠️ -nographics 금지: DOTS 엔티티 씬(SubScene) 빌드는 Scriptable Build Pipeline을 타는데,
            #    그래픽 디바이스가 없으면 "Unable to build with the current configuration"으로 실패한다.
            & $unity -quit -batchmode -projectPath $repo `
                     -executeMethod Benchmark.EditorTools.BenchmarkBuild.BuildFromCLI `
                     -logFile $log
            if ($LASTEXITCODE -ne 0) { throw "빌드 실패($label). 로그: $log" }
        }
        if (-not (Test-Path $exe)) { throw "빌드 산출물이 없습니다: $exe" }

        $csv = Join-Path $repo "$OutDir/$label.csv"
        Write-Host "  실행 중... → $csv"
        # -nographics 금지: 렌더 카운터(GPU·드로우콜)를 재려면 실제로 그려야 한다
        $p = Start-Process -FilePath $exe -PassThru -Wait -ArgumentList @(
            "-bench", "-label", $label, "-counts", $Counts,
            "-warmup", "$Warmup", "-sample", "$Sample",
            "-out", $csv, "-quit",
            "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0"
        )
        if (-not (Test-Path $csv)) { throw "CSV가 생성되지 않았습니다($label). 종료코드=$($p.ExitCode)" }

        $rows = (Get-Content $csv).Count - 1
        Write-Host "  완료: $rows 행`n"
        $results += [pscustomobject]@{ Label = $label; Branch = $branch; Csv = $csv; Rows = $rows }
    }
}
finally {
    git checkout $original --quiet
    Write-Host "원 브랜치로 복귀: $original"
}

Write-Host "`n=== 결과 ==="
$results | Format-Table -AutoSize
Write-Host "합치기 예: Get-Content $OutDir/*.csv | Select-Object -Unique | Set-Content $OutDir/all.csv"
