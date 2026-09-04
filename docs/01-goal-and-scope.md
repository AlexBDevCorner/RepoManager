# 01 — Goal and Scope

## Goal

Build a Windows desktop application that monitors a manually selected set of local Git repositories and makes it easy to understand their current state.

The application should answer questions such as:

- Which branch is currently checked out?
- Is the working tree clean?
- Is my current branch ahead of or behind its upstream branch?
- How far is my current work from the repository's remote default branch?
- Has the repository recently been fetched?
- Can this repository safely be updated automatically?
- Which repositories can be updated in one operation?

The application should also allow:

- Refreshing repository information locally.
- Fetching one repository.
- Fetching all repositories.
- Safely updating one repository.
- Safely updating all eligible repositories.
- Opening a repository folder.
- Adding/removing repositories.
- Discovering Git repositories under a parent folder.

Key principle:

> The application may inspect freely, fetch freely, but mutate the checked-out branch only when it can prove that the operation is a safe fast-forward.

## Non-goals for version 1

Do NOT implement any of the following:

- Commit.
- Push.
- Checkout.
- Branch creation.
- Branch deletion.
- Merge.
- Rebase.
- Reset.
- Automatic stash.
- Conflict resolution.
- Repository cloning.
- GitHub integration.
- Azure DevOps integration.
- Pull-request integration.
- File diff viewer.
- File editor.
- Git history browser.

This is not intended to become another GitKraken or SourceTree.

The application is a repository status dashboard and safe updater.
