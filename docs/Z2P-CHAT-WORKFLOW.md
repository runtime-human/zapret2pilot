# Z2P — Architecture / implementation workflow

The repository, not chat history, is authoritative.

Workflow:

```text
verify exact main
 -> read Z2P-CURRENT-STATE.json (sole dynamic state authority)
 -> read canonical architecture/roadmap + issue named by currentWorkItem
 -> design/RED evidence where required
 -> minimal implementation
 -> self-review against retained invariants
 -> repository-truth CI validation + restore/build/tests
 -> PR review
 -> orchestrator updates currentWorkItem in Z2P-CURRENT-STATE.json only
```

Do not mirror `architectureVersion/projectVersion/activeTrack/currentWorkItem` into canonical Markdown. Stable document-role markers identify canonical ownership; an orchestration switch must not require README/canon/roadmap/status churn.

Do not infer current work from old `NEXT`, a versioned plan or historical RFC. Do not silently rewrite old decisions; supersede them in `Z2P-DECISION-LOG.md` and preserve historical text.

v7 execution coordination after #14:

```text
#15 --------+
            +--> #17 -> #18 -> #19 -> #20
#16 --------+
```

#15 and #16 may be executed in parallel. #17 requires both. Selecting either branch is an orchestrator change in `Z2P-CURRENT-STATE.json`; this workflow does not select work automatically.

Repository-truth scripts remain CI validation; enforcement is supplied separately by the active `main-required-ci` repository ruleset (id `22957088`), verified under #26 from GitHub API state. Normal updates to `main` require a pull request and the strict GitHub Actions check `Build and test` (`integration_id=15368`); deletion/non-fast-forward updates are blocked and no bypass actors are configured. Agents may rely on that GitHub-enforced merge gate while the ruleset remains active.

For v7, no implementation agent may introduce a second Runtime Kernel authority, a persistent Windows Service, generic privileged IPC, real `winws2` before #20, or an evidence-free Rust migration.
