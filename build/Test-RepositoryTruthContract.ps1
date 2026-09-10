$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceStatePath = Join-Path $repoRoot 'docs/Z2P-CURRENT-STATE.json'
$validatorPath = Join-Path $repoRoot 'build/Validate-RepositoryTruth.ps1'
$tempStatePath = Join-Path ([System.IO.Path]::GetTempPath()) ("z2p-current-state-contract-{0}.json" -f [Guid]::NewGuid().ToString('N'))
$tempVersionPath = Join-Path ([System.IO.Path]::GetTempPath()) ("z2p-version-contract-{0}.txt" -f [Guid]::NewGuid().ToString('N'))

try {
    $state = Get-Content -LiteralPath $sourceStatePath -Raw | ConvertFrom-Json

    # Contract probe: every dynamic routing/state value can change without
    # synchronized canonical-Markdown edits. projectVersion still preserves
    # the required VERSION <-> current-state equality via a temporary VERSION.
    $state.architectureVersion = 'contract-probe-vnext'
    $state.projectVersion = '99.99.99-contract-probe'
    $state.activeTrack.issue = 999998
    $state.activeTrack.title = 'synthetic active-track probe'
    $state.currentWorkItem.issue = 999999
    $state.currentWorkItem.title = 'synthetic current-work probe'

    $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempStatePath -Encoding utf8
    $state.projectVersion | Set-Content -LiteralPath $tempVersionPath -Encoding utf8

    & $validatorPath -StatePath $tempStatePath -VersionPath $tempVersionPath
    if (-not $?) {
        throw 'repository-truth single-source probe failed'
    }

    Write-Host 'repository-truth contract: PASS — architectureVersion/projectVersion/activeTrack/currentWorkItem can change in the state contract without Markdown synchronization while VERSION equality remains enforced'
}
finally {
    Remove-Item -LiteralPath $tempStatePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempVersionPath -Force -ErrorAction SilentlyContinue
}
