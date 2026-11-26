# Project instructions
After changes, build and verify both Windows x86 and x64 packages. Update the project code on both GitHub (origin) and Gitea (gitea) after verification. Preserve remote history; never force-push. Report explicitly if either remote cannot be updated. Do not publish captures, credentials or live SAP data.

Use the connected GitHub integration and locally protected Gitea publishing credential when available. Prefer API/git publishing over browser uploads; never include credentials in the repository.

Automatically publish the current working branch to both origin and gitea after verification, creating the remote branch and setting upstream tracking when needed. The user has given standing authorization; do not ask for publication confirmation each time. Preserve history, never force-push, and report any publishing failure explicitly.
