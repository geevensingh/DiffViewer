using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public class UserPathRegistrarTests
{
    private const string Dir = @"C:\Users\me\AppData\Local\DiffViewer\current";

    private sealed class FakeStore : IEnvironmentPathStore
    {
        public string? Value;
        public bool IsExpandable;
        public int WriteCount;
        public string? LastWritten;
        public bool? LastWrittenExpandable;

        public (string? Value, bool IsExpandable) Read() => (Value, IsExpandable);

        public void Write(string value, bool isExpandable)
        {
            WriteCount++;
            LastWritten = value;
            LastWrittenExpandable = isExpandable;
            Value = value;
            IsExpandable = isExpandable;
        }
    }

    [Fact]
    public void Register_AppendsAndWrites()
    {
        var store = new FakeStore { Value = @"C:\Windows", IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Register(Dir).Should().BeTrue();
        store.WriteCount.Should().Be(1);
        store.LastWritten.Should().Be($@"C:\Windows;{Dir}");
    }

    [Fact]
    public void Register_AlreadyPresent_DoesNotWrite()
    {
        var store = new FakeStore { Value = $@"C:\Windows;{Dir}", IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Register(Dir).Should().BeFalse();
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public void Register_PreservesIsExpandableFlag()
    {
        var expandable = new FakeStore { Value = @"%USERPROFILE%\bin", IsExpandable = true };
        new UserPathRegistrar(expandable).Register(Dir);
        expandable.LastWrittenExpandable.Should().BeTrue();

        var plain = new FakeStore { Value = @"C:\bin", IsExpandable = false };
        new UserPathRegistrar(plain).Register(Dir);
        plain.LastWrittenExpandable.Should().BeFalse();
    }

    [Fact]
    public void Register_NoExistingPath_CreatesIt()
    {
        var store = new FakeStore { Value = null, IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Register(Dir).Should().BeTrue();
        store.LastWritten.Should().Be(Dir);
    }

    [Fact]
    public void Register_BlankDirectory_DoesNothing()
    {
        var store = new FakeStore { Value = @"C:\Windows", IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Register("  ").Should().BeFalse();
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public void Unregister_RemovesAndWrites()
    {
        var store = new FakeStore { Value = $@"C:\Windows;{Dir}", IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Unregister(Dir).Should().BeTrue();
        store.WriteCount.Should().Be(1);
        store.LastWritten.Should().Be(@"C:\Windows");
    }

    [Fact]
    public void Unregister_NotPresent_DoesNotWrite()
    {
        var store = new FakeStore { Value = @"C:\Windows", IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Unregister(Dir).Should().BeFalse();
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public void Unregister_PreservesIsExpandableFlag()
    {
        var store = new FakeStore { Value = $@"%USERPROFILE%\bin;{Dir}", IsExpandable = true };
        new UserPathRegistrar(store).Unregister(Dir);
        store.LastWrittenExpandable.Should().BeTrue();
    }

    [Fact]
    public void Register_IsIdempotentAcrossRepeatedCalls()
    {
        var store = new FakeStore { Value = @"C:\Windows", IsExpandable = true };
        var registrar = new UserPathRegistrar(store);

        registrar.Register(Dir).Should().BeTrue();
        registrar.Register(Dir).Should().BeFalse();
        registrar.Register(Dir).Should().BeFalse();
        store.WriteCount.Should().Be(1);
    }
}
