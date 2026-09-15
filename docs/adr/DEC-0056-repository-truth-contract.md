# DEC-0056 — Repository truth contract

Status: **ACCEPTED**  
Date: 2026-09-09  
Amended: 2026-09-10 after PR #25 orchestration review; GitHub enforcement verified 2026-09-12 under #26

## Decision

Use `docs/Z2P-CURRENT-STATE.json` as the **only dynamic repository-state authority** for:

- `architectureVersion`;
- `projectVersion`;
- `activeTrack`;
- `currentWorkItem`;
- canonical document paths.

Canonical Markdown must not mirror that tuple. Instead, use stable role markers for repository overview, documentation index, canon, implementation status and unique current-master architecture/roadmap ownership.

Changing `currentWorkItem` or the other orchestration-routing values therefore requires editing the JSON contract only, unless the actual architecture/roadmap content also changes.

## CI validation contract

CI runs two scripts:

1. `build/Test-RepositoryTruthContract.ps1` mutates architecture/track/work-item values only in a temporary current-state JSON and requires the repository to remain valid without Markdown synchronization;
2. `build/Validate-RepositoryTruth.ps1` validates:
   - `VERSION` equals `current-state.projectVersion`;
   - all canonical paths exist;
   - stable role markers are present;
   - exactly one current master architecture and roadmap exist at the paths named by current state;
   - the v6 compatibility path is visibly superseded and its historical source exists;
   - the RFC pointer is archived;
   - `Z2P-NEXT.md` is non-canonical and contains no static issue/version guidance.

The validator explicitly rejects the old `<!-- Z2P-CURRENT-STATE: ... -->` dynamic tuple marker in current/canonical Markdown.

## Validation and enforcement boundary

The scripts make CI fail on repository-truth violations; they do **not** themselves make CI non-bypassable. GitHub branch/ruleset protection and required-check enforcement remain separate repository configuration.

#26 verified the enforcement layer from GitHub API state: active repository ruleset `main-required-ci` (id `22957088`) targets the default branch `main`, requires a pull request and the strict GitHub Actions check `Build and test` (`integration_id=15368`), blocks deletion and non-fast-forward updates, and has no configured bypass actors. While that ruleset remains active, normal updates to `main` are GitHub-enforced through the required CI gate. Documentation may therefore describe the repository-truth validation as participating in an enforced repository gate, while continuing to distinguish validation logic from GitHub enforcement configuration.

## RED evidence

- Initial v7-A RED: CI #147 failed the original repository-truth validator before canonical synchronization.
- Single-source redesign RED: CI #158 at `38165c260c627a2080847fbc2d12b3f1cc3a527a` failed `Test repository truth contract` because the reviewed Markdown still depended on the duplicated dynamic-state marker. Restore/build/test were skipped.

## Scope

The validator/contract probe are small PowerShell scripts in `build/`; no custom documentation framework or runtime behavior is introduced.
