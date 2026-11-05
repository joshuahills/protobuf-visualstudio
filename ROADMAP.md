# Roadmap

Status of v0.1.0 and planned work. See [docs/buf-vscode-feature-analysis.md](docs/buf-vscode-feature-analysis.md)
for where each item comes from. IDs are stable so issues and commits can refer to them.

## Done in v0.1.0

- Buf Language Server hosted in Visual Studio's LSP client for `.proto` files. Gives you completion, hover,
  go to definition and type definition, find references, document highlight, rename, Format Document,
  document and workspace symbols, folding, document links, code actions (organize imports, deprecate,
  `buf:lint:ignore`), and compile and lint diagnostics as you type.
- buf.exe discovery: configured path, then PATH, then the extension's own install.
- Automatic download of the latest Buf release with SHA-256 verification, plus Tools > Buf > Install or
  Update Buf CLI.
- Version gate: `lsp serve` from 1.59, `beta lsp` from 1.43, disabled below that.
- Tools > Buf menu: Build, Lint, Format All, Generate, Update/Prune Dependencies, Initialize buf.yaml,
  List Lint/Breaking Rules, Restart Language Server.
- Commands run in every Buf workspace (`buf.yaml` / `buf.work.yaml`) found under the solution folder.
- Tools > Options > Buf: CLI path, auto-download, debug logging.
- "Buf" output pane carrying command output and the server log.

## Planned

| ID   | Feature | Notes |
| ---- | ------- | ----- |
| R-01 | **Format on save** | Option (default on, like vscode-buf). Use the LSP `textDocument/formatting` result or `buf format` on the buffer before save. |
| R-02 | **Grpc.Tools / `<Protobuf>` project support** | Read `Protobuf` items, `ProtoRoot` and `AdditionalImportDirs` from MSBuild projects so imports resolve in solutions without `buf.yaml`. May need a generated `buf.yaml` in a temp folder or a server option. The most important VS-specific gap. |
| R-03 | **Buf config file support** | Route `buf.yaml`, `buf.gen.yaml`, `buf.policy.yaml` and `buf.lock` to the server (hover and completion), plus schemastore validation for `buf.work.yaml` and `buf.plugin.yaml`. Needs a document type that doesn't break VS's YAML editor. |
| R-04 | **Error List for whole-workspace Build / Lint** | Parse `--error-format json` and publish through `DiagnosticsReporter` so problems in closed files show up too. Clear them on the next run. |
| R-05 | **Status bar / server state** | Show buf version and server state (starting, running, errored). Click to restart. |
| R-06 | **Automatic restart on crash** | Restart up to N times like vscode-buf. Restart automatically when Buf settings change, and when `buf.yaml` changes if the server doesn't pick it up. |
| R-07 | **Solution Explorer context menu** | Lint, Format, Build and Generate on a `.proto` file or folder. |
| R-08 | **Breaking change check** | `buf breaking --against .git#branch=<default>`, results in the Error List. Option for the against-ref. |
| R-09 | **Code lens commands** | Verify VS renders the server's `buf.yaml` / `buf.gen.yaml` code lenses and that `workspace/executeCommand` works. |
| R-10 | **Generated code navigation** | From a message or service to the C# type that `Grpc.Tools` or `buf generate` produced, and back. |
| R-11 | **Semantic colouring check** | Confirm VS applies the server's semantic tokens on top of the built-in TextMate `proto` grammar. Map token types to VS classification colours if needed. |
| R-12 | **File icon** for `.proto` in Solution Explorer. |  |
| R-13 | **New Item templates** | `.proto` file, `buf.yaml`, `buf.gen.yaml`. |
| R-14 | **Minisign verification** of the downloaded CLI using `sha256.txt.minisig`. Version pinning option. |  |
| R-15 | **Tests and CI** | Unit tests for `BufCli`, `WorkspaceRoots` and `BufInstaller`. GitHub Actions build that uploads the VSIX. |
| R-16 | **Marketplace publishing** | Icon, screenshots, listing text, `publishManifest.json`. |
| R-17 | **Trusted Publishing (OIDC) for the Marketplace** | Replace the `VS_MARKETPLACE_PAT` secret with a Trusted Publishing policy for `.github/workflows/build.yml` and `VsixPublisher publish -oidc` (job needs `id-token: write`). Blocked until Microsoft.VSSDK.BuildTools 18.12.465+ reaches nuget.org (expected 2026-11-10) and microsoft/vsmarketplace#2310 (VsixPub0047) is fixed. Delete the PAT once an OIDC publish succeeds. |
