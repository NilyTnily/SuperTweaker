#!/usr/bin/env bash
# Push to NilyTnily/SuperTweaker using a token (avoids wrong saved Windows credentials).
#
# 1) GitHub → Settings → Developer settings → PAT (classic) → repo scope
# 2) Create token while logged in as NilyTnily
# 3) From repo root in Git Bash:
#      export GITHUB_TOKEN=ghp_your_token_here
#      ./scripts/push-with-token.sh
#
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [[ -z "${GITHUB_TOKEN:-}" ]]; then
  echo "Set GITHUB_TOKEN first (PAT from the NilyTnily account, repo scope):"
  echo "  export GITHUB_TOKEN=ghp_..."
  exit 1
fi

REMOTE="https://x-access-token:${GITHUB_TOKEN}@github.com/NilyTnily/SuperTweaker.git"

echo "Pushing main..."
git push -u "$REMOTE" main

echo "Pushing tag v1.0.0 (create tag locally if missing: git tag -a v1.0.0 -m release)..."
if git rev-parse v1.0.0 >/dev/null 2>&1; then
  git push "$REMOTE" v1.0.0 || git push "$REMOTE" v1.0.0 --force
else
  echo "No tag v1.0.0 — skipping tag push."
fi

echo "Updating origin to HTTPS (no token stored in URL)..."
git remote set-url origin "https://github.com/NilyTnily/SuperTweaker.git"
echo "Done."
