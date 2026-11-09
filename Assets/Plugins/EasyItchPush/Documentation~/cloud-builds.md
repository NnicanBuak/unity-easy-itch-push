# GitHub Actions macOS IL2CPP builds

Easy Itch Push can build macOS IL2CPP using a macOS GitHub Actions runner, while other Build Profiles continue to build locally.

## Prerequisites

1. Keep your complete Unity game project in a GitHub repository. Commit `Assets/`, `Packages/`, `ProjectSettings/`, Unity Build Profiles and `Assets/CHANGELOG.md`.
2. Install [GitHub CLI](https://cli.github.com/) and authenticate once in a terminal with `gh auth login`. The plugin uses that credential store and never puts an API token into the Unity project.
3. In GitHub repository Actions secrets, configure GameCI's `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` using [GameCI licensing instructions](https://game.ci/docs/github/activation/). A compatible Unity license is required for a CI runner.
4. Make a macOS **player** Build Profile (not a Dedicated Server profile) with **IL2CPP** selected in its Player Settings. Commit the profile.
5. Open **Easy Itch Push → Settings**, specify GitHub `owner/repo`, branch, and workflow filename.
6. Click **Generate workflow**, then **commit and push** the generated `.github/workflows/easy-itch-push-macos.yml` to the repository's **default branch**. GitHub requires the workflow to exist there before workflow_dispatch can run.
7. Under **Platforms**, select **GitHub Actions (macOS)** for that macOS Build Profile, and leave other profiles set to **This computer**.

## Build & publish

Use **Publish → Build & Publish**, or **Build only**. The plugin checks for a clean local Git worktree and verifies that its HEAD equals the configured GitHub branch tip. The workflow runs on a macOS host with GameCI and invokes the same Unity Build Profile pipeline used for local builds.

The generated macOS ZIP archive is downloaded into the usual platform channel folder, validated with the local archives, and only then uploaded to itch.io using the existing Butler workflow. If any required profile fails, the complete publish is aborted.

macOS packages are zipped on the runner with native `zip -y` to retain symlinks and executable permissions inside `.app` bundles.

## Notes and limitations

- The first cloud backend supports only macOS Build Profiles. Other platforms remain local.
- You must push changes before clicking Build & Publish. The plugin will not silently commit or upload your source.
- GitHub Actions takes billable macOS runner minutes depending on repository and plan.
- CI build duration and GitHub CLI authentication require a working network and local `gh` executable.
- The GitHub workflow runs in the **game** repository, not this Unity Package Manager package repository.
- Unity macOS IL2CPP output is not automatically signed/notarized. Apple Developer signing and notarization are separate release tasks.
- The UI blocks while the CLI waits for the remote run. Build status and logs can be inspected in GitHub Actions.
- Prefer private repositories for proprietary game source.
