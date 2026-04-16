# Git Bash on Windows — push & scripts

## Wrong user (403: Lola-Palola vs NilyTnily)

Git is using **saved credentials for another GitHub account**. Either:

- **Push with a token (NilyTnily PAT)** from repo root:

  ```bash
  export GITHUB_TOKEN=ghp_YOUR_TOKEN_FROM_NILYTNILY_ACCOUNT
  chmod +x scripts/push-with-token.sh
  ./scripts/push-with-token.sh
  ```

- Or open **Credential Manager** (Windows) → **Windows Credentials** → remove `git:https://github.com`, then `git push` again and sign in as **NilyTnily**.

## PowerShell scripts from Git Bash

Do **not** use `.\scripts\Foo.ps1` in bash (that fails). Use:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/Publish-To-GitHub.ps1
```

## `gh: command not found`

Add GitHub CLI to PATH for this session:

```bash
export PATH="/c/Program Files/GitHub CLI:$PATH"
gh auth login
```

Or install: `winget install GitHub.cli`

## Windows paths in Git Bash

Use Unix-style paths:

```bash
cd /c/Users/User/Desktop/as
```

Not `cd C:\Users\...` (bash eats the backslashes).
