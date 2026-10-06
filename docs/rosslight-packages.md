# Rosslight editor packages

This fork tracks upstream RoslynPad and publishes its Morgania editor stack for WCP Commander.
Editor fixes come from upstream source. The fork retains its GitHub Packages release pipeline
and exposes script hosting and upstream theme mapping through `Morgania.CodeAnalysis.Editor`,
so consumers do not need copies of application or demo integration code.

Builds on main and pull requests validate and upload package artifacts without publishing.
Publish an immutable version with a `packages/v<version>` tag. Workflow dispatch only validates and uploads artifacts.
Publishing an existing version fails rather than silently retaining packages from another commit.
The workflow publishes only when running in `rosslight/roslynpad`.

The initial `5.9.0-rosslight.1` release predates the tag flow and was published by workflow dispatch.
Do not create a tag for that already published version; use a new version for each release.
If publication fails after some packages have been pushed, leave that version unused and publish the
complete package set under a new version. Published package versions cannot be overwritten.

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
