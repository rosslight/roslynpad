using Avalonia.Controls;
using Avalonia.Threading;
using System.Composition;
using Microsoft.VisualStudio.GeometryTests;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.BehaviorTests;

/// <summary>
/// Unaffected paragraphs retain their shaped rows across edits and scrolling.
/// Layout changes distinguish translated rows from newly formatted rows.
/// </summary>
[TestClass]
public sealed class LineCacheTests
{
    [TestMethod]
    public async Task ClassificationRefreshReformatsOnlyParagraphsWhoseFormattingChanged()
    {
        await HeadlessEditor.RunAsync(async () =>
        {
            using var container = HeadlessEditor.CreateContainer();
            var buffer = container.GetExport<ITextBufferFactoryService>().CreateTextBuffer(
                "first\nsecond", container.GetExport<IContentTypeRegistryService>().GetContentType("text"));
            var view = container.GetExport<ITextEditorFactoryService>().CreateTextView(buffer);
            view.DisplayTextLineContainingBufferPosition(new SnapshotPoint(buffer.CurrentSnapshot, 0),
                0, ViewRelativePosition.Top, 800, 600);
            var classifier = view.TextBuffer.Properties.GetProperty<LineCacheClassifier>(typeof(LineCacheClassifier));
            var formats = container.GetExport<IClassificationFormatMapService>().GetClassificationFormatMap(view);
            formats.AddExplicitTextProperties(classifier.ClassificationType, formats.DefaultTextProperties.SetFontRenderingEmSize(25));
            Dispatcher.UIThread.RunJobs();
            object first = view.TextViewLines[0].IdentityTag;
            object second = view.TextViewLines[1].IdentityTag;
            double firstHeight = view.TextViewLines[0].Height;

            await RefreshAsync().ConfigureAwait(true);
            Assert.AreSame(first, view.TextViewLines[0].IdentityTag, "An unchanged classification refresh does not reshape text.");
            Assert.AreSame(second, view.TextViewLines[1].IdentityTag);

            classifier.HighlightFirstLine = true;
            await RefreshAsync().ConfigureAwait(true);
            Assert.AreNotSame(first, view.TextViewLines[0].IdentityTag, "Changed formatting must be rendered.");
            Assert.IsTrue(view.TextViewLines[0].Height > firstHeight);
            Assert.AreSame(second, view.TextViewLines[1].IdentityTag, "File-wide notifications leave unaffected paragraphs intact.");
            view.Close();

            async Task RefreshAsync()
            {
                var laidOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnLayout(object? sender, TextViewLayoutChangedEventArgs e) => laidOut.TrySetResult();
                view.LayoutChanged += OnLayout;
                try
                {
                    classifier.Notify();
                    await laidOut.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                }
                finally
                {
                    view.LayoutChanged -= OnLayout;
                }
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task NewlineAtBlankParagraphBoundaryKeepsEachRowOnItsOwnSnapshotLine()
    {
        await HeadlessEditor.RunAsync(() =>
        {
            var view = HeadlessEditor.CreateView("first\n\nlast\n");
            for (int insertion = 0; insertion < 2; insertion++)
            {
                int position = insertion == 0 ? view.TextSnapshot.GetLineFromLineNumber(1).Start.Position : view.TextSnapshot.Length;
                view.TextBuffer.Insert(position, "\n");
                var snapshot = view.TextBuffer.CurrentSnapshot;
                Assert.AreEqual(snapshot.LineCount, view.TextViewLines.Count);
                for (int i = 0; i < snapshot.LineCount; i++)
                {
                    Assert.AreEqual(snapshot.GetLineFromLineNumber(i).Extent, view.TextViewLines[i].Extent,
                        "A cached empty paragraph must not appear at the newly inserted line's position.");
                }
            }
            view.Close();
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task BufferEditPublishesOneLayoutWithCurrentGeometry()
    {
        await HeadlessEditor.RunAsync(() =>
        {
            var view = HeadlessEditor.CreateView("first\nsecond");
            int layouts = 0;
            view.LayoutChanged += (_, _) => layouts++;

            view.TextBuffer.Insert(3, "x");

            Assert.AreEqual(1, layouts, "The edit and its visual-buffer propagation must share one layout.");
            Assert.AreSame(view.TextBuffer.CurrentSnapshot, view.TextSnapshot);
            Assert.IsTrue(view.TextViewLines.All(line => line.Extent.Snapshot == view.TextSnapshot),
                "Callers can query geometry against the new snapshot as soon as the edit returns.");
            Assert.AreEqual("firxst", view.TextViewLines[0].Extent.GetText());
            view.Close();
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EditingOneParagraphReusesOtherParagraphsWithTranslatedGeometry()
    {
        await HeadlessEditor.RunAsync(() =>
        {
            var view = HeadlessEditor.CreateView("first\nsecond\nthird");
            var window = new Window { Width = 800, Height = 600, Content = view.VisualElement };
            window.Show();
            window.UpdateLayout();
            int detachments = 0;
            ((Control)((IFormattedLine)view.TextViewLines[0]).GetOrCreateVisual()).DetachedFromLogicalTree += (_, _) => detachments++;
            object first = view.TextViewLines[0].IdentityTag;
            object second = view.TextViewLines[1].IdentityTag;
            object third = view.TextViewLines[2].IdentityTag;

            view.TextBuffer.Insert("first\nsec".Length, "x\nnew");

            Assert.AreSame(first, view.TextViewLines[0].IdentityTag, "The unedited paragraph keeps its shaped text.");
            Assert.AreEqual(0, detachments, "Surviving text visuals stay attached instead of reapplying their styles.");
            Assert.AreNotSame(second, view.TextViewLines[1].IdentityTag, "An edited paragraph is reformatted.");
            Assert.AreSame(third, view.TextViewLines[3].IdentityTag, "A surviving paragraph moves to the new line.");
            Assert.AreEqual("third", view.TextViewLines[3].Extent.GetText());
            Assert.AreSame(view.TextSnapshot, view.TextViewLines[3].Start.Snapshot);
            Assert.AreEqual(view.TextSnapshot.GetLineFromLineNumber(3).Start, view.TextViewLines[3].Start);
            Assert.AreEqual(TextViewLineChange.Translated, view.TextViewLines[3].Change);
            Assert.IsTrue(view.TextViewLines[3].GetCharacterBounds(view.TextViewLines[3].Start).Width > 0);
            view.Close();
            window.Close();
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EditingWrappedTextReformatsItsWholeParagraph()
    {
        await HeadlessEditor.RunAsync(() =>
        {
            var view = HeadlessEditor.CreateView("untouched paragraph with wrapping\nchanged paragraph with wrapping", width: 120, wordWrap: true);
            var firstRows = view.TextViewLines.Where(line => line.Start.GetContainingLine().LineNumber == 0)
                .Select(line => line.IdentityTag).ToArray();
            var changedRows = view.TextViewLines.Where(line => line.Start.GetContainingLine().LineNumber == 1)
                .Select(line => line.IdentityTag).ToArray();
            Assert.IsTrue(changedRows.Length > 1, "The regression exercises multiple visual rows in one paragraph.");

            view.TextBuffer.Insert(view.TextSnapshot.GetLineFromLineNumber(1).Start.Position, "more words ");

            CollectionAssert.AreEqual(firstRows, view.TextViewLines.Where(line => line.Start.GetContainingLine().LineNumber == 0)
                .Select(line => line.IdentityTag).ToArray());
            Assert.IsFalse(view.TextViewLines.Any(line => changedRows.Contains(line.IdentityTag)),
                "An edit can change every wrap boundary in its paragraph.");
            Assert.IsTrue(view.TextViewLines.All(line => line.Start.Snapshot == view.TextSnapshot));
            view.Close();
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ScrollingReusesFormattedLinesAndClassifiesChanges()
    {
        await HeadlessEditor.RunAsync(() =>
        {
            string text = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line {i}"));
            var view = HeadlessEditor.CreateView(text, height: 200.0);
            var initialLines = view.TextViewLines.ToDictionary(line => line.Start.Position, line => line.IdentityTag);

            TextViewLayoutChangedEventArgs? args = null;
            view.LayoutChanged += (_, e) => args = e;

            view.DisplayTextLineContainingBufferPosition(
                view.TextSnapshot.GetLineFromLineNumber(2).Start, 0.0, ViewRelativePosition.Top);

            foreach (var line in view.TextViewLines)
            {
                if (initialLines.TryGetValue(line.Start.Position, out var tag))
                {
                    Assert.AreSame(tag, line.IdentityTag, "Scrolling reuses surviving formatted lines.");
                    Assert.AreEqual(TextViewLineChange.Translated, line.Change);
                }
                else
                {
                    Assert.AreEqual(TextViewLineChange.NewOrReformatted, line.Change);
                }
            }

            Assert.IsNotNull(args);
            Assert.IsTrue(args.TranslatedLines.Count > 0, "Surviving lines are translated.");
            Assert.IsTrue(args.NewOrReformattedLines.Count > 0, "Lines scrolled into view are formatted fresh.");
            double lineHeight = view.TextViewLines[0].Height;
            Assert.AreEqual(-2.0 * lineHeight, args.TranslatedLines[0].DeltaY, 0.01, "Translation distance is recorded.");

            // Editing a visible paragraph reformats it, while preserving unaffected rows.
            view.TextBuffer.Insert(view.TextViewLines[0].Start.Position, "x");
            Assert.IsTrue(
                view.TextViewLines[0].Change == TextViewLineChange.NewOrReformatted,
                "The edited paragraph is reformatted.");

            view.Close();
        }).ConfigureAwait(false);
    }
}

[Shared]
[Export(typeof(IClassifierProvider))]
[ContentType("text")]
internal sealed class LineCacheClassifierProvider : IClassifierProvider
{
    private readonly IClassificationType _type;

    [ImportingConstructor]
    public LineCacheClassifierProvider(IClassificationTypeRegistryService registry)
    {
        _type = registry.CreateClassificationType("line-cache-test", [registry.GetClassificationType("text")]);
    }

    public IClassifier GetClassifier(ITextBuffer buffer) => buffer.Properties.GetOrCreateSingletonProperty(
        typeof(LineCacheClassifier), () => new LineCacheClassifier(buffer, _type));
}

internal sealed class LineCacheClassifier : IClassifier
{
    private readonly ITextBuffer _buffer;

    public LineCacheClassifier(ITextBuffer buffer, IClassificationType type)
    {
        _buffer = buffer;
        ClassificationType = type;
    }

    public IClassificationType ClassificationType { get; }
    public bool HighlightFirstLine { get; set; }
    public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged;

    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span) =>
        HighlightFirstLine && span.Start.Position == 0
            ? [new ClassificationSpan(span.Snapshot.GetLineFromLineNumber(0).Extent, ClassificationType)]
            : [];

    public void Notify()
    {
        Assert.IsNotNull(ClassificationChanged, "The native classifier aggregator must subscribe to the test classifier.");
        ClassificationChanged.Invoke(this,
            new ClassificationChangedEventArgs(new SnapshotSpan(_buffer.CurrentSnapshot, 0, _buffer.CurrentSnapshot.Length)));
    }
}
