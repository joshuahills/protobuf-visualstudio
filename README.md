# Protobuf Language Support for Visual Studio

[![Install from the Visual Studio Marketplace](https://img.shields.io/badge/Visual%20Studio%20Marketplace-Install-5C2D91?logo=visualstudio&logoColor=white)](https://marketplace.visualstudio.com/items?itemName=JoshHills.protobuf-language-support)
[![Marketplace version](https://vsmarketplacebadges.dev/version-short/JoshHills.protobuf-language-support.svg)](https://marketplace.visualstudio.com/items?itemName=JoshHills.protobuf-language-support)
[![Marketplace installs](https://vsmarketplacebadges.dev/installs-short/JoshHills.protobuf-language-support.svg)](https://marketplace.visualstudio.com/items?itemName=JoshHills.protobuf-language-support)
[![Build](https://github.com/joshuahills/protobuf-visualstudio/actions/workflows/build.yml/badge.svg)](https://github.com/joshuahills/protobuf-visualstudio/actions/workflows/build.yml)

A Visual Studio 2026 extension that brings `.proto` editing up to the level of the
[Buf extension for VS Code](https://github.com/bufbuild/vscode-buf). It hosts the
[Buf Language Server](https://buf.build/docs/cli/editor-integration/) (`buf lsp serve`) in Visual Studio's
LSP client and adds a Tools > Buf menu for the `buf` CLI.

This is an independent project. It is not affiliated with Buf Technologies.

It is built on the out-of-process
[VisualStudio.Extensibility](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility)
model and targets **.NET 10**.

## Install

Install from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=JoshHills.protobuf-language-support) (Extensions > Manage Extensions in Visual Studio 2026,
search for "Protobuf Language Support"), or download the VSIX from
[GitHub Releases](https://github.com/joshuahills/protobuf-visualstudio/releases).

## Features

In `.proto` files, provided by the Buf Language Server:

- IntelliSense completion for types, fields, options, keywords and import paths.
- Quick Info on hover with doc comments and fully qualified names.
- Go to Definition (F12), Go to Type Definition, Find All References (Shift+F12), and highlighting of other
  uses of the symbol under the caret.
- Rename across the workspace.
- Format Document (Ctrl+K, Ctrl+D) using `buf format` rules.
- Compile errors and lint findings from your `buf.yaml` as you type, shown as squiggles and in the Error List.
- Quick actions: organize imports, mark as deprecated, add a `buf:lint:ignore` comment.
- Navigation bar and document symbols, Go To All symbol search, folding, and clickable imports.

The language server restarts by itself when you change the Buf CLI path or debug logging setting, and after a
crash, up to 4 times in 3 minutes. After that it stays stopped until you use Tools > Buf > Restart Language
Server.

From the **Tools > Buf** menu, each running in every Buf workspace (folder with `buf.yaml` or `buf.work.yaml`)
under the solution:

- Build and Lint, with every finding listed in the **Error List**, including files that aren't open. Results are
  grouped by provider ("Buf Build", "Buf Lint"). A new run replaces the previous one, and a file's entries are
  dropped when you save it (the language server's live diagnostics take over). Clear Error List Results removes
  them all.
- Check Breaking Changes: `buf breaking` against the git default branch (origin/HEAD, then `main`, then
  `master`, using the remote-tracking branch if there is no local one), results in the Error List under
  "Buf Breaking". Workspaces below the repository root are handled with `subdir=`. Set **Breaking change
  baseline** to compare with something else, e.g. `.git#tag=v1.0.0` or `buf.build/owner/module`.
- Format All Proto Files, Generate.
- Update Dependencies, Prune Dependencies, Initialize buf.yaml.
- List Lint Rules, List Breaking Change Rules.
- Restart Language Server, Install or Update Buf CLI.

In **Solution Explorer**, right-click a `.proto` file for **Lint with Buf** and **Format with Buf**, or a folder
or project for **Lint Protos with Buf**. These run in the Buf workspace that contains the item and only replace
that item's Error List entries. (Solution view only for now; Open Folder view has its own menus, see #20.)

Output from all of these goes to **View > Output > Buf**. Findings are also written there as
`path(line,col): error RULE: message`, which can be double-clicked to jump to the problem.

[ROADMAP.md](ROADMAP.md) lists planned work. [docs/buf-vscode-feature-analysis.md](docs/buf-vscode-feature-analysis.md)
compares the extension feature by feature with vscode-buf.

## The Buf CLI

The extension looks for `buf.exe` in this order:

1. Tools > Options > Buf > Buf CLI path (a file or folder; relative paths resolve against the solution folder;
   `%VARIABLES%` are expanded).
2. `PATH`.
3. `%LOCALAPPDATA%\BufForVisualStudio\buf\v<version>\buf.exe`, the extension's own copy.

If none is found and **Download the Buf CLI if it is missing** is on (the default), the latest release is
downloaded from GitHub. The download is checked against the release's `sha256.txt`. The language server needs
buf 1.43.0 or later; 1.59.0 or later uses the stable `buf lsp serve`.

## Settings (Tools > Options > Buf)

| Setting | Default | Purpose |
| --- | --- | --- |
| Buf CLI path | (auto) | Use a specific `buf.exe` |
| Download the Buf CLI if it is missing | on | Auto-install the latest release |
| Language server debug logging | off | Start the server with `--debug` (restart to apply) |
| Breaking change baseline | (git default branch) | Any buf input to compare against for Check Breaking Changes |

## Building, installing and debugging

```powershell
dotnet build src\Protobuf.VisualStudio\Protobuf.VisualStudio.csproj
```

The VSIX is written to `src\Protobuf.VisualStudio\bin\Debug\net10.0-windows\Protobuf.VisualStudio.vsix`.
To install it, close Visual Studio and double-click the VSIX.

To debug, open `ProtobufForVisualStudio.slnx` in Visual Studio and press F5. From a command line you need the
MSBuild that ships with Visual Studio, because `dotnet build` refuses to deploy:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  src\Protobuf.VisualStudio\Protobuf.VisualStudio.csproj -restore -p:DeployExtension=true -p:VSSDKTargetPlatformRegRootSuffix=Exp
devenv /rootsuffix Exp samples\weather
```

The first time after a command-line deploy, the experimental instance lists the extension as "Disabled by
user". Enable it in Extensions > Manage Extensions and restart the experimental instance.

If the extension isn't listed at all, the experimental instance's extension cache is stale. Close the
experimental instance, delete `ExtensionMetadata2.0.mpack` and `ExtensionMetadataCache.mpack` from
`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instance>Exp\Extensions`, run
`devenv /rootsuffix Exp /updateconfiguration`, and start it again.

The language server starts when the first `.proto` file is opened, not when a folder or solution is opened.

`samples\weather` is a small Buf v2 workspace: two packages, a cross-file import, a well-known type import,
and one deliberate lint violation (`windSpeedKph`, FIELD_LOWER_SNAKE_CASE).

## Tests

```powershell
dotnet test --project tests\Protobuf.VisualStudio.Tests                                          # everything
dotnet test --project tests\Protobuf.VisualStudio.Tests -- --filter-not-trait "Category=Integration"   # unit only
```

`global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xUnit v3 needs on the .NET 10 SDK.

- **Unit tests** (`tests/.../Unit`, about 1 s, offline): buf.exe discovery (configured path, PATH, installed
  versions), version parsing and the `lsp serve` / `beta lsp` cut-over, Buf workspace discovery, and the
  installer's download and SHA-256 check against a fake HTTP server.
- **Integration tests** (`tests/.../Integration`, trait `Category=Integration`, about 10 s): run the real Buf CLI
  on `samples\weather` (build, lint in msvs format, rule listing, cancellation), and drive the real language
  server over stdio, started exactly as the extension starts it. The server tests check the advertised
  capabilities, lint diagnostics, go to definition across files, hover, completion, find references,
  document symbols, formatting and semantic tokens.

The integration tests use `BUF_PATH` if set. Otherwise they install the latest buf with the extension's own
installer into `%TEMP%\protobuf-vs-testsuf-cache`, downloaded once and then reused. CI installs buf with
`bufbuild/buf-action` and runs both suites before packaging.

The tests don't run inside Visual Studio itself (see R-18 in the roadmap).

## Layout

```
src/Protobuf.VisualStudio/
  ProtobufExtension.cs                      Metadata (.NET 10 host, VS 18+) and the Tools > Buf menu
  BufSettings.cs                            Tools > Options > Buf
  LanguageServer/BufLanguageServerProvider  .proto document type; starts `buf lsp serve` on stdio
  Services/BufService                       Buf resolution, auto-install, running commands, output pane
  Services/BufCli                           buf.exe discovery, version parsing, process helpers
  Services/BufInstaller                     GitHub release download with SHA-256 check
  Services/WorkspaceRoots                   Finds buf.yaml / buf.work.yaml roots under the solution
  Commands/                                 One class per Tools > Buf command
tests/Protobuf.VisualStudio.Tests/         Unit and integration tests (xUnit v3)
docs/                                       Feature analysis against vscode-buf
samples/weather/                            Buf workspace for manual testing
```

## Releases

Every push to `main` and every pull request builds the VSIX in GitHub Actions
([`.github/workflows/build.yml`](.github/workflows/build.yml)) and keeps it as a workflow artifact. Untagged
builds are versioned `<csproj version>.<run number>`.

To release, bump `<Version>` in the csproj if you like, then push a tag:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The tag's version is stamped into the VSIX, and a GitHub release is created with
`Protobuf.VisualStudio-0.2.0.vsix` attached. If the repository has a `VS_MARKETPLACE_PUBLISHER` variable and
a `VS_MARKETPLACE_PAT` secret, the same VSIX is also published to the Visual Studio Marketplace using
[`marketplace/publishManifest.json`](marketplace/publishManifest.json) and
[`marketplace/overview.md`](marketplace/overview.md).
