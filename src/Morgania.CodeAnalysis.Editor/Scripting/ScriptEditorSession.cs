using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Morgania.CodeAnalysis.Editor.Theming;
using RoslynPad.Themes;

namespace Morgania.CodeAnalysis.Editor.Scripting;

public sealed class ScriptEditorSession : IDisposable
{
    private readonly ExportProvider _exports;
    private readonly IWpfTextViewHost _viewHost;

    internal ScriptEditorSession(
        ExportProvider exports,
        ScriptEditorWorkspace workspace,
        ITextBuffer buffer,
        DocumentId documentId
    )
    {
        _exports = exports;
        Workspace = workspace;
        Buffer = buffer;
        DocumentId = documentId;
        var factory = exports.GetExportedValue<ITextEditorFactoryService>();
        View = factory.CreateTextView(buffer);
        _viewHost = factory.CreateTextViewHost(View, setFocus: false);
    }

    public Workspace Workspace { get; }
    public DocumentId DocumentId { get; }
    public ITextBuffer Buffer { get; }
    public IWpfTextView View { get; }
    public Control Control => _viewHost.HostControl;
    public string Text
    {
        get => Buffer.CurrentSnapshot.GetText();
        set
        {
            if (Text != value)
                Buffer.Replace(new Span(0, Buffer.CurrentSnapshot.Length), value);
        }
    }

    public void ApplyTheme(Theme theme)
    {
        var formats = new ThemeClassificationFormats(theme);
        IClassificationFormatMap classifications = _exports
            .GetExportedValue<IClassificationFormatMapService>()
            .GetClassificationFormatMap(View);
        var registry = _exports.GetExportedValue<IClassificationTypeRegistryService>();
        formats.Apply(classifications, registry);
        formats.ApplyInlineDiagnostics(classifications, registry);
        IEditorFormatMap editor = _exports.GetExportedValue<IEditorFormatMapService>().GetEditorFormatMap(View);
        formats.ApplyTextViewBackground(editor);
        formats.ApplyPopup(editor);
        formats.ApplyBackgroundWorkIndicator(editor);
        formats.ApplyOutlining(editor);
        formats.ApplySelection(editor);
        formats.ApplyCaret(editor);
        formats.ApplyFindReplace(editor);
        formats.ApplyBraceMatching(editor);
        formats.ApplyReferenceHighlighting(editor);
        formats.ApplyInlineRename(editor);
        formats.ApplyBlockStructure(editor);
        ImageCatalog.ThemeBackground = formats.Background;
        if (formats.Background is { } background)
            View.Background = new SolidColorBrush(background);
    }

    public void Dispose()
    {
        if (!_viewHost.IsClosed)
            _viewHost.Close();
        Workspace.Dispose();
    }
}
