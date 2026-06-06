using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="MarkdownDiffViewModel"/>. The VM is a thin
/// wrapper around <see cref="DiffViewer.Rendering.MarkdownDiffRenderer"/>;
/// renderer behavior is covered by <c>MarkdownDiffRendererTests</c>, so
/// these tests focus on the VM's contract — construction succeeds, the
/// resulting <c>Document</c> is non-null, and degenerate inputs don't
/// throw.
///
/// <para>Uses <c>[StaFact]</c> because the constructor builds a
/// dispatcher-affine <see cref="System.Windows.Documents.FlowDocument"/>.</para>
/// </summary>
public class MarkdownDiffViewModelTests
{
    [StaFact]
    public void Ctor_PopulatesDocumentFromBothSides()
    {
        var vm = new MarkdownDiffViewModel(
            leftText: "# Title\n",
            rightText: "# Title v2\n");

        vm.Document.Should().NotBeNull();
        vm.Document.Blocks.Should().NotBeEmpty();
    }

    [StaFact]
    public void Ctor_WithIdenticalSides_StillProducesADocument()
    {
        const string md = "# Same\n\nSame paragraph.\n";

        var vm = new MarkdownDiffViewModel(md, md);

        vm.Document.Should().NotBeNull();
        vm.Document.Blocks.Should().NotBeEmpty();
    }

    [StaFact]
    public void Ctor_WithEmptyStrings_DoesNotThrow()
    {
        // Edge: a newly-added or deleted markdown file lands here with
        // one side empty; the renderer should handle that and so should
        // the VM.
        var vm = new MarkdownDiffViewModel("", "");

        vm.Document.Should().NotBeNull();
    }

    [StaFact]
    public void Ctor_WithNullCoercedToEmpty_DoesNotThrow()
    {
        // DiffPaneViewModel passes _cachedLeftText / _cachedRightText
        // which can legitimately be empty strings; the VM accepts null
        // defensively and coerces to empty so a misuse doesn't crash
        // the load path.
        var vm = new MarkdownDiffViewModel(null!, null!);

        vm.Document.Should().NotBeNull();
    }
}
