# Production releases

CI runs on beta/main/release PRs and pushes. Current temporary organization policy requires one independent approval from super-maintainers on those branches, without an additional CODEOWNERS review or review bypass. Authors cannot approve themselves. CI/security remain required; Husbandry is separately excluded for custom-policy testing.

Promote tested production work through PRs to release. No complete production package/publishing workflow is introduced here. When publishing is implemented, use reviewed central verifier/package pins and tag the current protected release head; do not publish incomplete game-dependent artifacts. A passing engine-free rules test and Python syntax check do not prove game integration.

Use a temporary promotion/synchronization branch if a PR needs updating; never merge development into a permanent release branch just to unblock reverse history synchronization.

Current interim policy: https://github.com/TheIvoryDisaster/GutenStructure/blob/main/docs/interim-review-policy.md .
