using System.Reflection;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Morgania.CodeAnalysis.Editor.Scripting;

public sealed class ScriptEditorHost : IDisposable
{
    private static readonly string[] s_analyzerAssemblyNames =
    [
        "Microsoft.CodeAnalysis",
        "Microsoft.CodeAnalysis.CSharp",
        "Microsoft.CodeAnalysis.Features",
        "Microsoft.CodeAnalysis.CSharp.Features",
    ];

    private ExportProvider? _exports;
    private Task? _initialization;
    private bool _isDisposed;

    public ExportProvider Exports =>
        _exports ?? throw new InvalidOperationException("Initialize the editor host before creating a session.");

    public Task InitializeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return _initialization ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        HostServiceExports.InitializeMainThread();
        ExportProvider exports = await Task.Run(CreateExports).ConfigureAwait(true);
        if (_isDisposed)
            await exports.DisposeAsync().ConfigureAwait(false);
        else
            _exports = exports;
    }

    public async Task<ScriptEditorSession> CreateSessionAsync(
        string text,
        IEnumerable<Assembly> assemblies,
        IEnumerable<string> imports,
        Type? globalsType,
        LanguageVersion languageVersion
    )
    {
        Dispatcher.UIThread.VerifyAccess();
        var (references, analyzers) = await Task.Run(() =>
            (GetReferences(assemblies, globalsType).ToArray(), GetAnalyzers().ToArray())
        ).ConfigureAwait(true);
        ExportProvider exports = Exports;
        IContentType contentType = exports.GetExportedValue<IContentTypeRegistryService>().GetContentType("CSharp");
        ITextBuffer buffer = exports.GetExportedValue<ITextBufferFactoryService>().CreateTextBuffer(text, contentType);
        var workspace = new ScriptEditorWorkspace(MorganiaMefHostServices.Create(exports), buffer);
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        string workingDirectory = Directory.GetCurrentDirectory();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            usings: imports,
            allowUnsafe: true,
            sourceReferenceResolver: new SourceFileResolver([], workingDirectory),
            metadataReferenceResolver: ScriptMetadataResolver.Default.WithBaseDirectory(workingDirectory),
            nullableContextOptions: NullableContextOptions.Enable
        ).WithScriptClassName("Program");
        var project = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Program",
            "Program",
            LanguageNames.CSharp,
            isSubmission: true,
            parseOptions: new CSharpParseOptions(languageVersion, kind: SourceCodeKind.Script),
            compilationOptions: options,
            hostObjectType: globalsType,
            metadataReferences: references
        );
        Solution solution = workspace
            .CurrentSolution.WithAnalyzerReferences(analyzers)
            .AddProject(project)
            .AddDocument(
                documentId,
                "Program.csx",
                SourceText.From(text),
                filePath: Path.Combine(workingDirectory, "Program.csx")
            );
        workspace.OpenDocument(solution, documentId);
        return new ScriptEditorSession(exports, workspace, buffer, documentId);
    }

    private static ExportProvider CreateExports()
    {
        // VS-only parts are rejected by design. Resolve the required editor exports instead of ThrowOnErrors.
        return EditorComposition
            .CreateConfiguration()
            .CreateExportProviderFactory(HostServiceExports.MainThreadJoinableTaskFactory)
            .CreateExportProvider();
    }

    private static IEnumerable<MetadataReference> GetReferences(IEnumerable<Assembly> assemblies, Type? globalsType)
    {
        // Let the script compiler resolve implicit framework/global references exactly as the runner does.
        return CSharpScript
            .Create(string.Empty, ScriptOptions.Default.WithReferences(assemblies), globalsType)
            .GetCompilation()
            .References.Select(reference =>
                reference is PortableExecutableReference { FilePath: { } path } portable
                && File.Exists(Path.ChangeExtension(path, ".xml"))
                    ? MetadataReference.CreateFromFile(
                        path,
                        portable.Properties,
                        XmlDocumentationProvider.CreateFromFile(Path.ChangeExtension(path, ".xml"))
                    )
                    : reference
            );
    }

    private static IEnumerable<AnalyzerReference> GetAnalyzers()
    {
        var loader = new EditorAnalyzerLoader();
        return s_analyzerAssemblyNames.Select(name => new AnalyzerFileReference(Assembly.Load(name).Location, loader));
    }

    public void Dispose()
    {
        _isDisposed = true;
        _exports?.Dispose();
        _exports = null;
    }

    private sealed class EditorAnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }

        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }
}
