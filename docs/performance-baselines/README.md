# Checked-In Baselines

Store human-reviewed baseline pairs here when you want a stable comparison target in git.

Recommended naming:

- `windows-x64.json`
- `linux-x64.json`
- `gh-actions-windows-x64.json`

Each JSON baseline should keep its sibling Markdown file with the same stem.

Update both files together when:

- an intentional performance change lands,
- the runtime or SDK changes materially,
- or the baseline machine or CI runner class changes.

Use `docs/performance.md` for the capture and compare workflow.