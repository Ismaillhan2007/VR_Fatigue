# Development workflow

Use one integration branch: `main`. Keep it ready to demonstrate. No separate `develop` branch during the hackathon.

## Branches and commits

1. Start each task from the latest `origin/main` with a clean working tree. Preserve unrelated local work.
2. Create a short-lived branch: `feat/eye-capture`, `feat/gemini-report`, `feat/forest-scene`, `fix/tracking-loss`, or `docs/project-foundation`.
3. Keep one feature or fix per PR. Open a draft early if others need its contract; list dependencies explicitly.
4. Use clear commit subjects such as `feat: add replay eye provider`. Avoid mixing formatting or unrelated changes into feature PRs.
5. Before review, incorporate current `main`. Rebase only your unpublished work; merge `origin/main` into an already shared branch. Do not force-push a shared branch.

Example for a new task:

```sh
git fetch origin
git switch -c feat/eye-capture origin/main
# Implement, check, and commit the focused change.
git push -u origin feat/eye-capture
gh pr create --draft --base main
# When ready, mark ready for review and request one teammate in GitHub.
```

## Pull requests and review requests

- Use the PR template: state the problem, resulting behavior, acceptance criteria, and actual verification.
- Include a screenshot or short capture for visible Unity changes; identify the headset/runtime or desktop simulation used.
- List changes to schemas, SDK versions, scene/prefab ownership, data handling, and API call volume when relevant.
- Mark the PR ready only when its acceptance criteria pass. Request one available teammate who did not author the change; use GitHub's Reviewers field or `gh pr edit --add-reviewer <teammate-login>`.
- Reviewers check behavior, honest source labels, missing-data handling, offline fallback, secret handling, and Unity asset/GUID integrity.
- Address blocking comments and re-request review after significant changes. Do not dismiss reviews merely to merge.
- One human teammate approval is required. The designated human integrator squash-merges and deletes the feature branch after confirming the demo still runs.
- Agents may prepare PRs and fixes; merging remains a human action unless the user explicitly authorizes it.

## Repository settings to enable

A repository admin should add a `main` ruleset/branch protection: require PRs, one approval, resolved review conversations, and block force pushes/deletion. Require meaningful CI checks once implemented; do not select nonexistent checks. These settings are recommendations and are **not configured by these files**. If the GitHub plan does not support enforcement, follow the same review policy manually.

## Unity collaboration

- Pin the team's installed compatible Unity LTS editor in `ProjectVersion.txt`; commit `Packages/manifest.json` and `packages-lock.json` when the Unity project is created.
- Set Asset Serialization to Force Text and Version Control Mode to Visible Meta Files. Commit each asset with its `.meta` file.
- Assign one owner for each `.unity` scene and shared `.prefab` at a time. Other contributors work on separate scripts/prefabs; the owner integrates them.
- Keep licensed third-party assets attributable in `docs/ASSETS.md` when added. Obtain permission before buying or redistributing assets; do not commit paid source assets that teammates cannot legally share.
- Ignore generated Unity folders, builds, local environments, and private session data. Use Git LFS for approved large binaries before their first commit; keep tiny anonymized/synthetic fixtures in `fixtures/`.

## Verification by change type

| Change | Evidence expected |
| --- | --- |
| Capture/metrics | Replay fixtures, missing/invalid samples, timestamp gaps, unit checks for calculations |
| Gemini/backend | Mocked valid/invalid output, blocked response, timeout/429 handling, budget cutoff, cache provenance |
| Unity scene | Desktop play check plus headset check when available; local fallback, exit control, and frame timing |
| Integration | Capture/replay -> report -> scene, including no-network and unsupported-tracking paths |
| Docs only | Review links, consistency, whitespace; no claim of runtime or headset testing |

Document unrun checks honestly. Add automated backend/metric and Unity EditMode tests as those modules arrive; add PlayMode checks for scene loading. CI should not call paid APIs or require private eye data. A live Gemini smoke check is manual and must fit the authorized budget.
