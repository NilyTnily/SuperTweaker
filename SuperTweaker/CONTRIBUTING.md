# Contributing to SuperTweaker

## Quick way to contribute

The easiest and most impactful contribution is **improving the golden profiles**:

1. Edit `SuperTweaker/Data/profiles/golden-win11.json` or `golden-win10.json`
2. Add your tweak following the schema in README.md
3. Run `dotnet test --filter "Category=ReadOnly|Category=DryRun"` — all must pass
4. Open a PR with a description of what the tweak does and a source reference

## Reporting bugs

Open an issue with:
- Windows version + build number
- What you clicked
- What happened vs what you expected
- Log file from `%LOCALAPPDATA%\SuperTweaker\Logs\`

## Adding an app to the catalog

Edit `SuperTweaker/Data/apps/apps-catalog.json`. You only need:
- `Name` — display name
- `WingetId` — find it with `winget search <name>`
- `Category` — existing or new
- `DefaultChecked` — `true` only for universally useful apps

## Code changes

1. Fork → branch from `main`
2. Make changes
3. `dotnet test SuperTweaker.Tests/` — all tests green
4. PR with clear description

## Profile tweak rules

- Every registry tweak **must** have an `UndoValue` (or `null` to delete)
- Every `PowerShellApply` **must** have a `PowerShellUndo`
- `Risk` must be accurate: `Safe` = no visible change to the user; `Moderate` = disables a feature; `Advanced` = may cause instability
- Prefer registry/service tweaks over PowerShell scripts — they're more deterministic
- Test on a VM before submitting
