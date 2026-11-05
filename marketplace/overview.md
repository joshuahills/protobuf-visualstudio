# Protobuf Language Support for Visual Studio

Protocol Buffers IntelliSense for Visual Studio 2026, powered by the
[Buf Language Server](https://buf.build/docs/cli/editor-integration/).

This is an independent project. It is not affiliated with or endorsed by Buf Technologies.

## In `.proto` files

- **IntelliSense**: types, fields, options, keywords and import paths.
- **Quick Info** on hover with doc comments.
- **Go to Definition** (F12), **Find All References** (Shift+F12) and **Rename** across the workspace.
- **Format Document** with `buf format` rules.
- **Build and lint errors as you type**, using the lint rules in your `buf.yaml`.
- **Quick actions**: organize imports, mark as deprecated, suppress a lint rule.

## Tools > Buf

Build, Lint, Format All, Generate, Update and Prune Dependencies, Initialize buf.yaml, List Lint and Breaking
Change Rules, Restart Language Server, and Install or Update Buf CLI.

## Requirements

- **Visual Studio 2026** (18.0 or later). The extension runs on .NET 10, which Visual Studio 2022 cannot
  host, so it installs on VS 2022 but does not load there.
- The [Buf CLI](https://buf.build/docs/cli/installation/) 1.43 or later. If it isn't on `PATH`, the
  extension downloads the latest release from GitHub and verifies its checksum. Configure this under
  Tools > Options > Buf.
