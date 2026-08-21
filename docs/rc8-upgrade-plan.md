# DeepSeek Harness rc8 Upgrade Plan

## Scope and guardrails

- Baseline repository: `71aaa/deepseek-harness-desktop`.
- Stable baseline: Desktop `v1.0.0`, commit `5a104b5`, Harness `0.1.0-rc.6`.
- Upgrade branch: `codex/rc8-upgrade`.
- Do not modify `main`, C# source, `HarnessService.cs`, WebView2 logic, or port-detection logic during runtime validation.
- Do not overwrite the rc6 `dsh-runtime` directory. Do not run `npm install` in the stable worktree.

## Current rc6 state

- Desktop version: `1.0.0`.
- Installed Harness runtime: `@deepseek-ai/dsh@0.1.0-rc.6`.
- Desktop starts the bundled launcher at `dsh-runtime\\node_modules\\.bin\\dsh.cmd web`.
- Desktop probes `http://127.0.0.1:3080`, identifies the Harness page, then loads it in WebView2.
- Desktop runtime ownership is guarded by port listener PID, process creation time, process-tree evidence, and `runtime.json` recovery state.
- Baseline environment evidence is recorded in `runtime-manifest.json`.

## Target rc8 state

- Target Harness runtime: `@deepseek-ai/dsh@0.1.0-rc.8`, installed as one coherent and locked dependency tree.
- The Desktop contract is expected to remain `dsh.cmd web` with loopback HTTP service on port `3080`, but this must be verified rather than assumed.
- The existing C# shell remains unchanged unless isolated testing proves a contract incompatibility.

## Runtime replacement approach

1. Create an external, disposable rc8 runtime directory; do not use this repository's `dsh-runtime`.
2. In that directory, create a minimal package manifest that pins `@deepseek-ai/dsh` exactly to `0.1.0-rc.8` and generate its lockfile.
3. Validate the complete runtime tree: `node_modules\\.bin\\dsh.cmd`, `@deepseek-ai\\dsh\\package.json`, and `lib\\bin.js` must exist.
4. Copy the current `publish` directory to an external disposable Desktop test directory.
5. Replace only `dsh-runtime` inside that copied publish directory with the generated rc8 runtime. The source worktree, its rc6 runtime, and the official rc6 publish directory remain untouched.
6. Launch only the copied Desktop executable for integration validation.

## Test steps

1. Ensure no rc6 or other Harness process owns port 3080.
2. Validate the rc8 launcher directly: `dsh.cmd --version`, `dsh.cmd web --help`, then `dsh.cmd web`.
3. Confirm that `127.0.0.1:3080` listens and serves the Harness web page.
4. Launch the copied Desktop app and verify that WebView2 loads the page.
5. Inspect Desktop logs to confirm readiness and Harness-page detection pass.
6. Confirm ownership validation records the listener as `OwnedByDesktop=true` for a Desktop-started runtime.
7. Close the Desktop app and confirm that its owned Harness process tree ends, port 3080 releases, and the test runtime state clears.
8. Start rc8 externally, then launch the test Desktop app; verify external-process detection and non-destructive shutdown.
9. Repeat first with a clean temporary `DSH_HOME`, then with a copy of the user's existing DSH home. Never test rc8 directly against the only live user-data directory.
10. Verify existing provider/API settings, profiles, plugins, and historical sessions in the copied DSH home.

## Rollback plan

- `main` remains anchored at the rc6 baseline commit `5a104b5` and is not changed during this work.
- Keep the current v1.0.0 release ZIP and its recorded SHA-256 as the known-good executable rollback artifact.
- Delete only the external rc8 test directory if validation fails; no stable source, stable runtime, or user data requires restoration.
- If branch changes are later committed, return to the rc6 baseline by switching to `main` or the future rc6 baseline tag, rebuilding from the rc6 runtime lockfile, and distributing the verified v1.0.0 artifact.

## Exit criteria before any production change

- All launcher, port, WebView2, page-detection, ownership, shutdown, and recovery checks pass.
- The copied user DSH home remains compatible with rc8.
- The rc8 dependency tree, Node/npm versions, and test evidence are recorded.
- Any required Desktop source changes are separately reviewed; runtime validation alone does not authorize them.
