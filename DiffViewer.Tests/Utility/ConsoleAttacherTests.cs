using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

/// <summary>
/// Tests for <see cref="ConsoleAttacher"/> — Win32 AttachConsole wrapper
/// added for CLI integration (issue #5). The actual Win32 call's effects
/// can't be unit-tested portably (the test runner already owns its own
/// console state), so coverage here is limited to the contract pieces
/// that don't require a particular console attachment outcome:
/// idempotency and exception-safety.
/// </summary>
public class ConsoleAttacherTests
{
    [Fact]
    public void AttachToParent_IsIdempotent()
    {
        // Two consecutive calls must not throw and must return a consistent
        // attached state. We don't assert true/false because the answer
        // depends on the test runner (xUnit's console runner vs Test
        // Explorer's hosted runner produce different parent processes).
        var firstResult = ConsoleAttacher.AttachToParent();
        var secondResult = ConsoleAttacher.AttachToParent();

        secondResult.Should().Be(firstResult);
        ConsoleAttacher.IsAttached.Should().Be(firstResult);
    }

    [Fact]
    public void IsAttached_MatchesLastAttachResult()
    {
        var result = ConsoleAttacher.AttachToParent();

        ConsoleAttacher.IsAttached.Should().Be(result);
    }
}
