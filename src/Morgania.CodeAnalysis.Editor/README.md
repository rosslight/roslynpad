# Morgania.CodeAnalysis.Editor

The Roslyn-powered C# editor for Avalonia, built on the Morgania editor
(`Morgania.Editor`) and the Morgania Roslyn EditorFeatures
(`Morgania.CodeAnalysis.EditorFeatures`).

The package contains the editor-host layer — everything a functioning Roslyn editor
needs beyond the editor platform itself:

- **`EditorComposition`** — builds the single VS-MEF graph shared by Roslyn and the editor
  (Roslyn Workspaces/Features, the Morgania editor, the Morgania Roslyn EditorFeatures, and this
  assembly's services). Returns the `CompositionConfiguration`, so hosts can inspect
  composition diagnostics before creating the export provider.
- **Editor-host services**, composed automatically: classification format definitions,
  diagnostics squiggles, block structure guide lines, the suggested-actions light bulb,
  key bridging, smart indentation, snippet expansion, and image-catalog glyphs.
- **Refactoring dialogs**: Change Signature, Extract Interface, Pick Members.
- **UI helpers**: `Glyph.ToImageSource()` (Roslyn glyphs → Avalonia `DrawingImage`),
  `TaggedText.ToTextBlock()` rich-text rendering, `ImageCatalog` for known image ids.

For an embedded C# script editor, the package includes `ScriptEditorHost` and
`ScriptEditorSession` in `Morgania.CodeAnalysis.Editor.Scripting`. A session owns
the native text view and its buffer-backed Roslyn workspace. Script references,
imports, globals and language version are supplied by the application.

```csharp
// Initialize one shared host on the Avalonia UI thread.
using var host = new ScriptEditorHost();
await host.InitializeAsync();
using var session = await host.CreateSessionAsync(
    code, scriptAssemblies, scriptImports, typeof(ScriptGlobals), LanguageVersion.CSharp12);
session.ApplyTheme(theme);
editorContainer.Content = session.Control;
```

Create sessions and apply themes on the UI thread. Dispose each session when its
view is no longer needed, then dispose the host. Composition and reference loading
run in the background. `Text`, `Buffer`, `View` and `Workspace` expose the native
editing services for binding, options and code actions. Font, line numbers and
outlining preferences belong to the application.

`Morgania.CodeAnalysis.Editor.Theming.ThemeClassificationFormats` applies VS Code
themes to native classification and editor format maps. RoslynPad uses the same
adapter. Consumers need no access to internal editor features.

Applications with their own document model can still bring their own `Workspace`
and open documents over editor buffers, as shown below.

## Getting started

```csharp
// On the UI thread, before the graph composes:
HostServiceExports.InitializeMainThread();

var configuration = EditorComposition.CreateConfiguration();
// Optional: inspect configuration.CompositionErrors / Catalog.DiscoveredParts.DiscoveryErrors.
// Rejected parts are expected (EditorFeatures parts with VS-only imports), so don't ThrowOnErrors().
var exportProvider = configuration.CreateExportProviderFactory().CreateExportProvider();

// Create a buffer, open a Roslyn document over it in your workspace:
var contentType = exportProvider.GetExportedValue<IContentTypeRegistryService>().GetContentType("CSharp");
var buffer = exportProvider.GetExportedValue<ITextBufferFactoryService>().CreateTextBuffer(code, contentType);
// ... add a project + document to your Workspace and open it with buffer.AsTextContainer()

// Create the view:
var editorFactory = exportProvider.GetExportedValue<ITextEditorFactoryService>();
var view = editorFactory.CreateTextView(buffer);
var viewHost = editorFactory.CreateTextViewHost(view, setFocus: true);
// viewHost.HostControl is an Avalonia control — place it anywhere.
```

For a complete, runnable example — including a minimal host workspace backed by the editor
buffer — see the
[Morgania.Demo.EditorFeatures](https://github.com/roslynpad/roslynpad/tree/main/src/Morgania.Demo.EditorFeatures)
project. For a full-featured host (multiple documents, NuGet references, execution), see RoslynPad itself.
