# PostBank PM API

The PostBank project-management API host (`PostBank.Api`). It contains no Core source: it consumes the
NexusCore platform as NuGet packages (`PackageReference`) and composes the modules this product uses.
Restore works completely **offline** from a local package folder.

| Repo | Role |
|---|---|
| `nexuscore-taskmanagement` (branch `feature/pm-core-gap-phase0`) | NexusCore source; produces the packages |
| `POSTBANKPMAPI` (this repo) | the API host |
| `POSTBANKPMUI` | the React UI, talks to this API |

## 1. Build the Core packages (once per Core release)

In a reviewed checkout of `nexuscore-taskmanagement`:

```powershell
pwsh ./eng/pack-core-modules.ps1 -Configuration Release -Version 0.1.0 -Output artifacts/packages
```

The script prints the package version (for example `0.1.0.1405081302`: base version, Persian date, daily build number).

## 2. Build the offline feed (on a machine that can reach nuget.org)

The host also needs third-party packages (EF Core, Serilog, JWT, ...). This step restores the host once
and gathers **every** package it needs, Core and third-party, into one folder, then proves the folder is complete by
restoring again from it alone.

```powershell
pwsh ./eng/make-offline-feed.ps1 -CorePackages ../nexuscore-taskmanagement/artifacts/packages
# Linux / macOS: ./eng/make-offline-feed.sh ../nexuscore-taskmanagement/artifacts/packages
```

Result: `offline-feed/` with all `.nupkg` files and a `SHA256SUMS` list (record it in the release ticket).

The Core version the host uses is `NexusCoreVersion` in `PostBank.Api/PostBank.Api.csproj`; set it to the version
printed in step 1 (it is `0.1.0.1405081302` now).

## 3. Install on the offline machine

Copy `offline-feed/` next to `nuget.config` (this repo's root), then:

```powershell
dotnet restore PostBank.Api/PostBank.Api.csproj
dotnet build   PostBank.Api/PostBank.Api.csproj -c Release
dotnet publish PostBank.Api/PostBank.Api.csproj -c Release -o publish
```

`nuget.config` clears all public sources, so a missing or incomplete feed fails loudly instead of reaching the internet.
The offline-feed folder is git-ignored on purpose: it holds proprietary packages, so distribute it as an artifact.

## Configuration

`PostBank.Api/appsettings.json` holds development placeholders only. Provide connection strings, the JWT signing key
(`Jwt:SigningKey`) and CORS origins through environment variables or a vault. Keep `Database:SeedOnStartup` off in
production after the controlled initial setup. Database scripts that Core ships (`docs/` and the `*SchemaUpgrade`
twins) are applied by the DBA; see the Core repository's `NEW_MODULES_API.md` for the ordered list.
Seeded roles do not receive permissions added after they were first created: grant them in the admin screen.

## Upgrading Core

1. Build new packages (step 1) and a new offline feed (step 2).
2. Change `NexusCoreVersion`.
3. Restore, build, deploy. Never overwrite a published package version; publish a new one.

## TMPB settings / organization structure

This host now registers `Nexus.OrgStructure` application services, infrastructure, permission catalog,
`/api/org-structure` endpoints (including OrgNode endpoints), and initial schema creation alongside the
organization module. The implementation comes from Core branch `feature/1405-08-07-orgNode`, reviewed
at commit `9f74c04aec1f43971369f2ee888c98d9d4af23df`. No Core business logic is duplicated in this host.

Before deployment, package that Core revision as a **new release**, set `NexusCoreVersion` to the version
actually produced, and rebuild the offline feed. The currently pinned version is retained because no
new package release/feed was supplied with this change; availability and compatibility of the existing
feed have not been verified. Both `Nexus.OrgStructure` and `Nexus.OrgStructure.Infrastructure` must be
present, along with matching foundation packages.

For an existing database, apply the OrgStructure migrations/schema upgrades from that Core release
through the normal deployment process. `SeedOnStartup` is intended for initial setup, not upgrades.
Grant the module's organization/node permissions to the appropriate roles: installing the module does
not automatically broaden existing users' grants. Verify a permitted user and a denied user, and test
company/node creation, placement, links, movement/merge previews, and history against SQL Server.

Validation of this change is limited to source registration checks. This workspace has no .NET SDK,
SQL Server or offline NuGet feed, so API restore/build/database and end-to-end checks remain pending.
