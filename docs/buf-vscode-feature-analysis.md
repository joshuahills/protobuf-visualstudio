# Feature analysis: Buf for VS Code

Reference point for this extension: [bufbuild/vscode-buf](https://github.com/bufbuild/vscode-buf) v0.8.19
(May 2026), with the language server from [bufbuild/buf](https://github.com/bufbuild/buf) v1.75.0
(`private/buf/buflsp`). Analysed 2026-11-05 from the extension's `package.json`, `src/`, `CHANGELOG.md` and the
language server's `server.go`.

## How it is built

The VS Code extension is thin. Since 0.8.0 nearly all editor intelligence comes from the **Buf Language
Server**, which ships inside the `buf` CLI:

| Buf CLI version | Launch command                        |
| --------------- | ------------------------------------- |
| < 1.43.0        | no language server (extension disables LSP features) |
| 1.43.0 – 1.58.x | `buf --log-format text beta lsp`      |
| ≥ 1.59.0        | `buf --log-format text lsp serve`     |

`--debug` is added when `buf.debugLogs` is on. The extension talks to it over stdio with
`vscode-languageclient`, restarts it on crash (up to 4 times), and shows its log in a separate
"Buf (server)" output channel.

The extension adds only these things itself:

- a TextMate grammar for `.proto` and for Buf config files,
- language registration and file icons,
- JSON-schema validation of `buf.work.yaml` and `buf.plugin.yaml` (from schemastore.org),
- CLI discovery and auto-download,
- a status bar item,
- ten palette commands that shell out to `buf`.

**What this means for Visual Studio:** VS 2026's LSP client can host the same server, so we get almost
everything with no protobuf parsing of our own. That's the core design decision of this repo.

## Language server capabilities (buf v1.75.0)

From `ServerCapabilities` in `buflsp/server.go`:

| LSP capability                | What it does for `.proto`                                                    | In VS via LSP client? |
| ----------------------------- | ---------------------------------------------------------------------------- | --------------------- |
| Diagnostics (push)            | Compile errors plus `buf lint` results as you type, using `buf.yaml` lint config | Yes: squiggles and Error List |
| Completion (+ resolve)        | Types, fields, options, import paths, keywords; triggers on `.` `"` `/`      | Yes: IntelliSense     |
| Hover                         | Doc comments, fully qualified names, well-known types                        | Yes: Quick Info       |
| Go to definition              | Messages, enums, services, imports (including BSR deps and WKTs)             | Yes: F12              |
| Go to type definition         | From a field to its message or enum type                                     | Yes                   |
| Find references               | Indexed across the workspace (`reference_index.go`)                          | Yes: Shift+F12        |
| Document highlight            | Highlights other uses of the symbol under the caret                          | Yes                   |
| Rename (+ prepare)            | Workspace-wide symbol rename                                                 | Yes: Ctrl+R, Ctrl+R   |
| Formatting                    | `buf format` style                                                           | Yes: Format Document  |
| Semantic tokens (full)        | Accurate colouring of types, fields, options, CEL                            | Yes, if the VS client honours it (to verify) |
| Document symbols              | Outline of messages, enums, services                                         | Yes: navigation bar, Document Outline |
| Workspace symbols             | Search symbols by name across the workspace                                  | Yes: Ctrl+T / Go To All |
| Folding ranges                | Blocks and comment runs                                                      | Yes                   |
| Document links                | Clickable `import` paths and URLs                                            | Yes                   |
| Code actions                  | `source.organizeImports`; `refactor.rewrite` (mark as `deprecated`); quick fix `buf:lint:ignore` comment | Yes: lightbulb |
| Code lens                     | On `buf.yaml` / `buf.gen.yaml`: update deps, check for updates, run generate, check plugin updates | Partially: VS shows LSP code lenses, but the commands must round-trip through `workspace/executeCommand` |
| Execute command               | `updateAllDeps`, `checkUpdates`, `runGenerate`, `checkPluginUpdates`         | Yes (see code lens)   |
| CEL support                   | Completion, hover, definition and semantic tokens inside `protovalidate` CEL expressions | Yes (same server)  |
| Buf config files              | Hover and completion in `buf.yaml`, `buf.gen.yaml`, `buf.policy.yaml`; hover in `buf.lock` (since vscode-buf 0.8.16) | Needs a YAML document filter: planned |

## Extension-level features

| VS Code feature | Details | Status here |
| --- | --- | --- |
| Syntax highlighting | TextMate `source.proto` grammar | VS 2026 already ships a `proto` TextMate grammar; semantic tokens from the server add to it. |
| Buf config language | `buf-config` language for `buf.yaml`, `buf.gen.yaml`, `buf.policy.yaml`, `buf.lock` | Planned (R-03) |
| File icons | Light and dark `.proto` icons | Planned (R-12) |
| Schema validation | `buf.work.yaml` and `buf.plugin.yaml` via schemastore | Planned (R-03); VS's JSON/YAML editor can use schemastore |
| Format on save | `configurationDefaults` turns on `editor.formatOnSave` for `[proto]` | Planned (R-01); VS has no generic format-on-save for LSP languages |
| CLI resolution | `buf.commandLine.path` (relative to workspace, `${workspaceRoot}`/`${env:}`/`${userHome}` substitution), else `$PATH`, else auto-download | **Done**: Options > Buf > Buf CLI path (relative to the solution, `%ENV%` expansion), PATH, auto-download |
| Auto-download | Latest GitHub release into extension storage | **Done**, plus SHA-256 verification against `sha256.txt` |
| Version gate | Warns and disables LSP below 1.43.0 | **Done** |
| Status bar | `Buf (1.x.y)` with starting, running, stopped and errored states; click to restart or show commands | Planned (R-05) |
| Output channels | "Buf" and "Buf (server)" | **Done** (one "Buf" pane; server stderr prefixed `[lsp]`) |
| Debug logs setting | `buf.debugLogs` restarts the server | **Done** (takes effect on restart) |
| Crash restart | Up to 4 automatic restarts | Planned (R-06); today, Tools > Buf > Restart Language Server |

### Commands

| VS Code command | Runs | Here (Tools > Buf) |
| --- | --- | --- |
| Start / Stop Buf Language Server | | **Restart Language Server** |
| Build | `buf build` per workspace root | **Build** (`--error-format msvs`) |
| Init | `buf config init` | **Initialize buf.yaml** |
| List lint rules | `buf config ls-lint-rules` | **List Lint Rules** |
| List breaking change rules | `buf config ls-breaking-rules` | **List Breaking Change Rules** |
| Prune module dependencies | `buf dep prune` | **Prune Dependencies** |
| Update module dependencies | `buf dep update` | **Update Dependencies** |
| Generate | `buf generate` | **Generate** |
| Show Buf Output | | View > Output > Buf |
| (none) | | **Lint** (`buf lint`, whole workspace) |
| (none) | | **Format All Proto Files** (`buf format -w`) |
| (none) | | **Install or Update Buf CLI...** |

### Dropped upstream

- Breaking-change detection in the editor was deprecated in vscode-buf 0.8.11, and most settings were removed
  then too. We still want a breaking check against git (R-08), because Visual Studio users rarely have a
  terminal open.
- The separate `buf` language id was dropped in 0.8.12.

## Gaps worth closing for Visual Studio specifically

These features don't exist in vscode-buf but matter more in Visual Studio:

1. **Grpc.Tools / `<Protobuf>` items.** Most VS users compile protos through `Grpc.Tools` in a `.csproj`, not
   `buf generate`. Those projects have no `buf.yaml`, so import roots come from `ProtoRoot` and
   `AdditionalImportDirs` metadata. Without them the server reports unresolved imports. (R-02)
2. **Error List integration for whole-workspace lint and build.** The LSP only reports open files. (R-04)
3. **Generated code navigation.** Jump from a message to its generated C# class and back. (R-10)
4. **Solution Explorer context menu** on `.proto` files and folders. (R-07)
