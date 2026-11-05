# Protobuf Language Support for Visual Studio

A Visual Studio 2026 extension that brings `.proto` editing up to the level of the
[Buf extension for VS Code](https://github.com/bufbuild/vscode-buf). It hosts the
[Buf Language Server](https://buf.build/docs/cli/editor-integration/) (`buf lsp serve`) in Visual Studio's
LSP client and adds a Tools > Buf menu for the `buf` CLI.

This is an independent project. It is not affiliated with Buf Technologies.

It is built on the out-of-process
[VisualStudio.Extensibility](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility)
model and targets **.NET 10**.

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

From the **Tools > Buf** menu, each running in every Buf workspace (folder with `buf.yaml` or `buf.work.yaml`)
under the solution:

- Build, Lint, Format All Proto Files, Generate.
- Update Dependencies, Prune Dependencies, Initialize buf.yaml.
- List Lint Rules, List Breaking Change Rules.
- Restart Language Server, Install or Update Buf CLI.

Output from all of these goes to **View > Output > Buf**. Lines in `--error-format msvs` form can be
double-clicked to jump to the problem.

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
