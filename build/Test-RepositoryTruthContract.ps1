$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceStatePath = Join-Path $repoRoot 'docs/Z2P-CURRENT-STATE.json'
$validatorPath = Join-Path $repoRoot 'build/Validate-RepositoryTruth.ps1'
$tempStatePath = Join-Path ([System.IO.Path]::GetTempPath()) ("z2p-current-state-contract-{0}.json" -f [Guid]::NewGuid().ToString('N'))

try {
    $state = Get-Content -LiteralPath $sourceStatePath -Raw | ConvertFrom-Json

    # Contract probe: orchestration/architecture routing changes must not require
    # synchronized edits to canonical Markdown. projectVersion is intentionally
    # excluded because VERSION <-> current-state remains a required two-file check.
    $state.architectureVersion = 'contract-probe-vnext'
    $state.activeTrack.issue = 999998
    $state.activeTrack.title = 'synthetic active-track probe'
    $state.currentWorkItem.issue = 999999
    $state.currentWorkItem.title = 'synthetic current-work probe'

    $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempStatePath -Encoding utf8

    & $validatorPath -StatePath $tempStatePath
    if (-not $?) {
        throw 'repository-truth single-source probe failed'
    }

    Write-Host 'repository-truth contract: PASS — architectureVersion/activeTrack/currentWorkItem can change in the JSON contract without Markdown synchronization'
}
finally {
    Remove-Item -LiteralPath $tempStatePath -Force -ErrorAction SilentlyContinue
}
