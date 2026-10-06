using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;

namespace Morgania.CodeAnalysis.Editor.Scripting;

internal sealed class ScriptEditorWorkspace : Workspace
{
    private readonly ITextBuffer _buffer;

    public ScriptEditorWorkspace(HostServices hostServices, ITextBuffer buffer)
        : base(hostServices, WorkspaceKind.Host)
    {
        _buffer = buffer;
    }

    public override bool CanOpenDocuments => true;

    public override bool CanApplyChange(ApplyChangesKind feature) => feature == ApplyChangesKind.ChangeDocument;

    public void OpenDocument(Solution solution, DocumentId documentId)
    {
        Solution previous = CurrentSolution;
        Solution current = SetCurrentSolution(solution);
        _ = RaiseWorkspaceChangedEventAsync(WorkspaceChangeKind.SolutionChanged, previous, current);
        // Use Roslyn's own buffer container so editor taggers find this workspace through the same bridge.
        OnDocumentOpened(documentId, _buffer.AsTextContainer());
        OnDocumentContextUpdated(documentId);
    }

    protected override void ApplyDocumentTextChanged(DocumentId id, SourceText text)
    {
        SourceText previous = _buffer.CurrentSnapshot.AsText();
        using ITextEdit edit = _buffer.CreateEdit();
        foreach (TextChange change in text.GetTextChanges(previous))
            edit.Replace(new Span(change.Span.Start, change.Span.Length), change.NewText);
        edit.Apply();
    }
}
