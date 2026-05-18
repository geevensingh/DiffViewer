using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

public class RecentsJsonSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields_ForMixedSides()
    {
        var items = new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree()),
                new DiffSide.CommitIsh("main"),
                new DiffSide.WorkingTree(),
                new DateTimeOffset(2026, 5, 14, 18, 0, 0, TimeSpan.Zero)),
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\bar", new DiffSide.CommitIsh("HEAD~3"), new DiffSide.CommitIsh("feature/foo")),
                new DiffSide.CommitIsh("HEAD~3"),
                new DiffSide.CommitIsh("feature/foo"),
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
        };
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, items);

        var json = RecentsJsonSerializer.Serialize(doc);
        var roundTripped = RecentsJsonSerializer.Deserialize(json);

        roundTripped.Version.Should().Be(RecentsDoc.CurrentVersion);
        roundTripped.Items.Should().HaveCount(2);
        roundTripped.Items[0].Should().Be(items[0]);
        roundTripped.Items[1].Should().Be(items[1]);
    }

    [Fact]
    public void Serialize_UsesTypeDiscriminator_ForWorkingTreeAndCommit()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree()),
                new DiffSide.CommitIsh("main"),
                new DiffSide.WorkingTree(),
                DateTimeOffset.UtcNow),
        });

        var json = RecentsJsonSerializer.Serialize(doc);

        json.Should().Contain("\"type\": \"commit\"");
        json.Should().Contain("\"type\": \"workingTree\"");
        json.Should().Contain("\"reference\": \"main\"");
    }

    [Fact]
    public void Serialize_OmitsReferenceField_ForWorkingTree()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.WorkingTree(), new DiffSide.WorkingTree()),
                new DiffSide.WorkingTree(),
                new DiffSide.WorkingTree(),
                DateTimeOffset.UtcNow),
        });

        var json = RecentsJsonSerializer.Serialize(doc);

        json.Should().NotContain("\"reference\"");
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsEmpty()
    {
        RecentsJsonSerializer.Deserialize(string.Empty).Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_Whitespace_ReturnsEmpty()
    {
        RecentsJsonSerializer.Deserialize("   \r\n  ").Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_MalformedJson_ReturnsEmpty()
    {
        RecentsJsonSerializer.Deserialize("{ not valid").Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_UnknownFutureVersion_PreservesKnownRows()
    {
        // Phase 7 softened the policy from "unknown version = empty" to
        // "preserve known rows, drop unknown ones" so a downgraded binary
        // reading a newer file doesn't lose every row. Rows the deserializer
        // can structurally understand are preserved regardless of the
        // version stamp; rows that fail the per-row shape check are dropped.
        var json = """
        {
          "version": 99,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit", "reference": "main" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" }
          ]
        }
        """;
        var doc = RecentsJsonSerializer.Deserialize(json);
        doc.Items.Should().HaveCount(1);
        doc.Items[0].Identity.CanonicalRepoPath.Should().EndWith("foo");
    }

    [Fact]
    public void Deserialize_MissingItems_ReturnsEmpty()
    {
        var json = "{\"version\":1}";
        RecentsJsonSerializer.Deserialize(json).Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public void Deserialize_UnknownSideType_SkipsItem()
    {
        var json = """
        {
          "version": 1,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "mystery" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" },
            { "repoPath": "C:/repos/bar", "left": { "type": "commit", "reference": "main" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" }
          ]
        }
        """;
        var doc = RecentsJsonSerializer.Deserialize(json);
        doc.Items.Should().HaveCount(1);
        doc.Items[0].Identity.CanonicalRepoPath.Should().EndWith("bar");
    }

    [Fact]
    public void Deserialize_MissingReferenceForCommitSide_SkipsItem()
    {
        var json = """
        {
          "version": 1,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" }
          ]
        }
        """;
        var doc = RecentsJsonSerializer.Deserialize(json);
        doc.Items.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_PreservesEmptyItems()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, Array.Empty<RecentLaunchContext>());
        var json = RecentsJsonSerializer.Serialize(doc);
        var rt = RecentsJsonSerializer.Deserialize(json);
        rt.Items.Should().BeEmpty();
        rt.Version.Should().Be(RecentsDoc.CurrentVersion);
    }

    [Fact]
    public void RoundTrip_NormalizesLastUsedUtcToUtc_RegardlessOfInputOffset()
    {
        var local = new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.FromHours(-5));
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(@"C:\repos\foo", new DiffSide.WorkingTree(), new DiffSide.WorkingTree()),
                new DiffSide.WorkingTree(),
                new DiffSide.WorkingTree(),
                local),
        });

        var rt = RecentsJsonSerializer.Deserialize(RecentsJsonSerializer.Serialize(doc));

        rt.Items[0].LastUsedUtc.UtcDateTime.Should().Be(local.UtcDateTime);
        rt.Items[0].LastUsedUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Serialize_NullDoc_Throws()
    {
        Action act = () => RecentsJsonSerializer.Serialize(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -- Phase 7: PR-review feature ----------------------------------

    [Fact]
    public void RoundTrip_PreservesPullRequestField()
    {
        var pr = new PullRequestRef("github.com", "geevensingh", "diffviewer", 42);
        var items = new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(
                    @"C:\repos\diffviewer",
                    new DiffSide.CommitIsh("abc123"),
                    new DiffSide.CommitIsh("def456")),
                new DiffSide.CommitIsh("abc123"),
                new DiffSide.CommitIsh("def456"),
                new DateTimeOffset(2026, 5, 14, 18, 0, 0, TimeSpan.Zero),
                pr),
        };
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, items);

        var json = RecentsJsonSerializer.Serialize(doc);
        var rt = RecentsJsonSerializer.Deserialize(json);

        rt.Items.Should().HaveCount(1);
        rt.Items[0].Review.Should().Be(pr);
        // Verify the on-disk shape carries the expected nested object so a
        // downgraded binary can recognize and either honor or ignore it.
        json.Should().Contain("\"pullRequest\"");
        json.Should().Contain("\"host\": \"github.com\"");
        json.Should().Contain("\"number\": 42");
    }

    [Fact]
    public void RoundTrip_OmitsPullRequestField_WhenNull()
    {
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(
                    @"C:\repos\foo", new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree()),
                new DiffSide.CommitIsh("main"),
                new DiffSide.WorkingTree(),
                DateTimeOffset.UtcNow),
        });

        var json = RecentsJsonSerializer.Serialize(doc);

        // Null PR rows should not bloat the on-disk JSON with the
        // sibling field; deserialization round-trip is covered by
        // the existing RoundTrip_PreservesAllFields_ForMixedSides test.
        json.Should().NotContain("\"pullRequest\"");
    }

    [Fact]
    public void Deserialize_OldVersion1_WithoutPullRequest_HydratesNullPullRequest()
    {
        // A v1 file written by a pre-Phase-7 binary: no pullRequest
        // sibling on any row. The new deserializer must load it with
        // Review = null on each row (no data loss on upgrade).
        var json = """
        {
          "version": 1,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit", "reference": "main" }, "right": { "type": "workingTree" }, "lastUsedUtc": "2026-05-14T18:00:00Z" }
          ]
        }
        """;

        var doc = RecentsJsonSerializer.Deserialize(json);

        doc.Items.Should().HaveCount(1);
        doc.Items[0].Review.Should().BeNull();
    }

    [Fact]
    public void Deserialize_PullRequestWithMissingOrEmptyFields_HydratesNullPullRequest()
    {
        // Defensive: a corrupted / partially-written pullRequest object
        // (e.g., a future binary that wrote an extra-field subset) must
        // not bring down the load. The row's PR is dropped, but the row
        // itself survives.
        var json = """
        {
          "version": 2,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit", "reference": "main" }, "right": { "type": "commit", "reference": "topic" }, "lastUsedUtc": "2026-05-14T18:00:00Z", "pullRequest": { "host": "github.com", "owner": "", "repo": "diffviewer", "number": 1 } }
          ]
        }
        """;

        var doc = RecentsJsonSerializer.Deserialize(json);

        doc.Items.Should().HaveCount(1);
        doc.Items[0].Review.Should().BeNull();
    }

    [Fact]
    public void Deserialize_PullRequestWithZeroOrNegativeNumber_HydratesNullPullRequest()
    {
        var json = """
        {
          "version": 2,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit", "reference": "main" }, "right": { "type": "commit", "reference": "topic" }, "lastUsedUtc": "2026-05-14T18:00:00Z", "pullRequest": { "host": "github.com", "owner": "x", "repo": "y", "number": 0 } }
          ]
        }
        """;

        var doc = RecentsJsonSerializer.Deserialize(json);

        doc.Items.Should().HaveCount(1);
        doc.Items[0].Review.Should().BeNull();
    }

    [Fact]
    public void Serialize_PullRequestRow_EmitsProviderDiscriminator()
    {
        // The IReviewRef abstraction is keyed on a "provider" tag inside
        // the pullRequest object. v1 of the feature only knows "github",
        // but emitting the tag now means a future ADO row will read
        // back on this binary without conditional logic in the loader.
        var pr = new PullRequestRef("github.com", "geevensingh", "diffviewer", 42);
        var doc = new RecentsDoc(RecentsDoc.CurrentVersion, new[]
        {
            new RecentLaunchContext(
                ContextIdentityFactory.Create(
                    @"C:\repos\diffviewer",
                    new DiffSide.CommitIsh("abc"),
                    new DiffSide.CommitIsh("def")),
                new DiffSide.CommitIsh("abc"),
                new DiffSide.CommitIsh("def"),
                DateTimeOffset.UtcNow,
                pr),
        });

        var json = RecentsJsonSerializer.Serialize(doc);

        json.Should().Contain("\"provider\": \"github\"");
    }

    [Fact]
    public void Deserialize_PullRequestWithoutProviderField_DefaultsToGithub()
    {
        // Back-compat: a v2 file written by a pre-IReviewRef binary
        // has no "provider" field. Loader must assume "github" so
        // existing recents.json files keep working unchanged.
        var json = """
        {
          "version": 2,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit", "reference": "main" }, "right": { "type": "commit", "reference": "topic" }, "lastUsedUtc": "2026-05-14T18:00:00Z", "pullRequest": { "host": "github.com", "owner": "octocat", "repo": "hello-world", "number": 17 } }
          ]
        }
        """;

        var doc = RecentsJsonSerializer.Deserialize(json);

        doc.Items.Should().HaveCount(1);
        doc.Items[0].Review.Should().BeOfType<PullRequestRef>()
            .Which.Should().Be(new PullRequestRef("github.com", "octocat", "hello-world", 17));
    }

    [Fact]
    public void Deserialize_PullRequestWithUnknownProvider_KeepsRowDropsReview()
    {
        // Forward-compat: a future binary may write a row with a
        // provider value this binary doesn't understand (e.g.,
        // "ado"). The row's repo identity is still valid — keep it,
        // just drop the review-ness so the recents bar stays usable
        // on the older binary.
        var json = """
        {
          "version": 2,
          "items": [
            { "repoPath": "C:/repos/foo", "left": { "type": "commit", "reference": "main" }, "right": { "type": "commit", "reference": "topic" }, "lastUsedUtc": "2026-05-14T18:00:00Z", "pullRequest": { "provider": "ado", "organization": "myorg", "project": "myproj", "repo": "myrepo", "number": 99 } }
          ]
        }
        """;

        var doc = RecentsJsonSerializer.Deserialize(json);

        doc.Items.Should().HaveCount(1);
        doc.Items[0].Review.Should().BeNull();
        doc.Items[0].Identity.CanonicalRepoPath.Should().NotBeNullOrEmpty();
    }
}
