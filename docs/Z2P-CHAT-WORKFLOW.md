# Z2P — Architecture / implementation workflow

The repository, not chat history, is authoritative.

Workflow:

```text
verify exact main
 -> read Z2P-CURRENT-STATE.json
 -> read canonical architecture/roadmap + active issue
 -> design/RED evidence where required
 -> minimal implementation
 -> self-review against retained invariants
 -> repository-truth + build + tests
 -> PR review
 -> orchestrator updates currentWorkItem
```

Do not infer current work from old `NEXT`, versioned plan or historical RFC files. Do not silently rewrite old decisions; supersede them in `Z2P-DECISION-LOG.md` and preserve historical text.

For v7, no implementation agent may introduce a second Runtime Kernel authority, a persistent Windows Service, generic privileged IPC, real `winws2` before #20, or an evidence-free Rust migration.
