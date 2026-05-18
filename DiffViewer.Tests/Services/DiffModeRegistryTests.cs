using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public class DiffModeRegistryTests
{
    [Fact]
    public void BuildDefault_ListsFourProvidersInExpectedOrder()
    {
        // Order is part of the contract — it's what the dialog's
        // left-rail ListBox renders top-to-bottom. "Working tree vs
        // HEAD" leads as the cheapest interaction.
        var registry = DiffModeRegistry.BuildDefault();

        registry.Providers.Should().HaveCount(4);
        registry.Providers[0].Id.Should().Be(WorkingTreeVsHeadProvider.ProviderId);
        registry.Providers[1].Id.Should().Be(WorkingTreeVsCommitProvider.ProviderId);
        registry.Providers[2].Id.Should().Be(CommitVsCommitProvider.ProviderId);
        registry.Providers[3].Id.Should().Be(GitHubPullRequestProvider.ProviderId);
    }

    [Fact]
    public void BuildDefault_AllProviderIdsAreUnique()
    {
        // If a future provider accidentally collides with an existing
        // Id, last-used-mode restoration would resolve to the wrong
        // form. Belt-and-suspenders check.
        var registry = DiffModeRegistry.BuildDefault();

        var ids = registry.Providers.Select(p => p.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Providers_AreReadOnly()
    {
        var registry = DiffModeRegistry.BuildDefault();
        registry.Providers.Should().BeAssignableTo<System.Collections.Generic.IReadOnlyList<IDiffModeProvider>>();
    }
}
