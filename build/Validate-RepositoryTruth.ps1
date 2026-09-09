$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'docs/Z2P-CURRENT-STATE.json'

function Fail-RepositoryTruth {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Error "repository-truth: $Message"
    exit 1
}

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    Fail-RepositoryTruth "missing docs/Z2P-CURRENT-STATE.json"
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()

if ($version -ne $state.projectVersion) {
    Fail-RepositoryTruth "VERSION '$version' != repository state '$($state.projectVersion)'"
}

$stateMarker = "<!-- Z2P-CURRENT-STATE: architecture=$($state.architectureVersion); version=$($state.projectVersion); track=#$($state.activeTrack.issue); work=#$($state.currentWorkItem.issue) -->"
$currentDocs = @(
    'README.md',
    'docs/README.md',
    $state.canonicalCanon,
    $state.canonicalArchitecture,
    $state.canonicalRoadmap,
    $state.canonicalStatus
)

foreach ($relativePath in $currentDocs) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Fail-RepositoryTruth "missing current document '$relativePath'"
    }

    $content = Get-Content -LiteralPath $path -Raw
    if (-not $content.Contains($stateMarker, [System.StringComparison]::Ordinal)) {
        Fail-RepositoryTruth "'$relativePath' is not synchronized to the current-state marker"
    }

    if ($content -match 'Master plan:\s*Version 6') {
        Fail-RepositoryTruth "'$relativePath' still claims v6 is the current master plan"
    }
}

$architectureMarker = '<!-- Z2P:CURRENT_MASTER_ARCHITECTURE -->'
$roadmapMarker = '<!-- Z2P:CURRENT_MASTER_ROADMAP -->'
$docsMarkdown = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs') -Filter '*.md' -File -Recurse

$architectureOwners = @($docsMarkdown | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw).Contains($architectureMarker, [System.StringComparison]::Ordinal) })
$roadmapOwners = @($docsMarkdown | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw).Contains($roadmapMarker, [System.StringComparison]::Ordinal) })

if ($architectureOwners.Count -ne 1) {
    Fail-RepositoryTruth "expected exactly one current master architecture marker; found $($architectureOwners.Count)"
}

if ($roadmapOwners.Count -ne 1) {
    Fail-RepositoryTruth "expected exactly one current master roadmap marker; found $($roadmapOwners.Count)"
}

$expectedArchitecturePath = (Join-Path $repoRoot $state.canonicalArchitecture)
$expectedRoadmapPath = (Join-Path $repoRoot $state.canonicalRoadmap)
if ($architectureOwners[0].FullName -ne $expectedArchitecturePath) {
    Fail-RepositoryTruth "master architecture marker is in '$($architectureOwners[0].FullName)', expected '$expectedArchitecturePath'"
}

if ($roadmapOwners[0].FullName -ne $expectedRoadmapPath) {
    Fail-RepositoryTruth "master roadmap marker is in '$($roadmapOwners[0].FullName)', expected '$expectedRoadmapPath'"
}

$historicalV6Path = Join-Path $repoRoot $state.historicalV6Roadmap
if (-not (Test-Path -LiteralPath $historicalV6Path -PathType Leaf)) {
    Fail-RepositoryTruth "missing preserved historical v6 roadmap '$($state.historicalV6Roadmap)'"
}

$legacyV6Pointer = Join-Path $repoRoot 'docs/Z2P-MVP-ROADMAP-2026-07-04-v6.md'
if (-not (Test-Path -LiteralPath $legacyV6Pointer -PathType Leaf)) {
    Fail-RepositoryTruth 'missing v6 compatibility pointer'
}

if (-not (Get-Content -LiteralPath $legacyV6Pointer -Raw).Contains('<!-- Z2P:HISTORICAL_SUPERSEDED -->', [System.StringComparison]::Ordinal)) {
    Fail-RepositoryTruth 'v6 compatibility path is not visibly marked historical/superseded'
}

$rfcPath = Join-Path $repoRoot $state.rfcPointer
if (-not (Test-Path -LiteralPath $rfcPath -PathType Leaf)) {
    Fail-RepositoryTruth "missing RFC pointer '$($state.rfcPointer)'"
}

if (-not (Get-Content -LiteralPath $rfcPath -Raw).Contains('<!-- Z2P:RFC_ARCHIVED -->', [System.StringComparison]::Ordinal)) {
    Fail-RepositoryTruth 'RFC pointer does not state that the proposal is archived behind canonical v7 docs'
}

$nextPath = Join-Path $repoRoot 'docs/Z2P-NEXT.md'
if (-not (Test-Path -LiteralPath $nextPath -PathType Leaf)) {
    Fail-RepositoryTruth 'missing Z2P-NEXT compatibility pointer'
}

$nextContent = Get-Content -LiteralPath $nextPath -Raw
if (-not $nextContent.Contains('<!-- Z2P:NON_CANONICAL_POINTER -->', [System.StringComparison]::Ordinal)) {
    Fail-RepositoryTruth 'Z2P-NEXT.md can still be mistaken for current implementation guidance'
}

if ($nextContent -match '#\d+' -or $nextContent -match '\b0\.\d+\.\d+\b') {
    Fail-RepositoryTruth 'Z2P-NEXT.md must not contain a static issue or version; resolve currentWorkItem from Z2P-CURRENT-STATE.json'
}

Write-Host "repository-truth: OK — architecture=$($state.architectureVersion), version=$($state.projectVersion), track=#$($state.activeTrack.issue), work=#$($state.currentWorkItem.issue)"
