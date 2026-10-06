# Rosslight editor packages

This fork tracks upstream RoslynPad and publishes its Morgania editor stack for WCP Commander.
The fork-specific change is the GitHub Packages build/release pipeline; editor fixes come from upstream source.

Builds on main and pull requests validate and upload package artifacts without publishing.
Publish an immutable version with a `packages/v<version>` tag or the workflow's version input.
The workflow publishes only when running in `rosslight/roslynpad`.

The package set is `Morgania.Editor.Abstractions`, `Morgania.Editor`,
`Morgania.CodeAnalysis.EditorFeatures`, `Morgania.CodeAnalysis.Editor`, and `RoslynPad.Themes`.
Consumers must map `Morgania.*` and `RoslynPad.Themes` to
`https://nuget.pkg.github.com/rosslight/index.json` and authenticate with package read access.
Retain upstream's Visual Studio SDK feed for the editor's VS dependencies.

The libraries provide net10.0 assets, while the source build uses the upstream-selected .NET 11 SDK.
Check out the Roslyn submodule recursively and keep Roslyn runtime dependencies aligned with the vendored editor features.

The pipeline runs both target frameworks. IntelliSense cases run in individual processes because
the upstream suite shares a static headless UI/JTF host and suggestion completion fails after
another completion test in one process. All discovered cases remain required; failures are not skipped.
