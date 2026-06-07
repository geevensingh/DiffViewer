using System;
using DiffViewer.Services;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Verifies the load-bearing registry behavior that the whole CLI-PATH
/// feature depends on: reading the raw (unexpanded) value and preserving
/// <c>REG_EXPAND_SZ</c> vs <c>REG_SZ</c> on write. Runs against a throwaway
/// per-user key, never the real <c>HKCU\Environment</c>.
/// </summary>
public sealed class RegistryPathIoTests : IDisposable
{
    private const string ParentPath = @"Software\DiffViewerTests";
    private readonly string _subKeyPath;
    private readonly RegistryKey _key;

    public RegistryPathIoTests()
    {
        _subKeyPath = $@"{ParentPath}\{Guid.NewGuid():N}";
        _key = Registry.CurrentUser.CreateSubKey(_subKeyPath, writable: true)!;
    }

    public void Dispose()
    {
        _key.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_subKeyPath, throwOnMissingSubKey: false); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Read_AbsentValue_ReturnsNullAndExpandableTrue()
    {
        var (value, isExpandable) = RegistryPathIo.Read(_key, "Path");
        value.Should().BeNull();
        isExpandable.Should().BeTrue();
    }

    [Fact]
    public void Read_ExpandString_ReturnsRawUnexpandedValueAndExpandableTrue()
    {
        _key.SetValue("Path", @"%USERPROFILE%\bin", RegistryValueKind.ExpandString);

        var (value, isExpandable) = RegistryPathIo.Read(_key, "Path");

        value.Should().Be(@"%USERPROFILE%\bin", "the raw value must not be expanded on read");
        value.Should().Contain("%");
        isExpandable.Should().BeTrue();
    }

    [Fact]
    public void Read_String_ReturnsValueAndExpandableFalse()
    {
        _key.SetValue("Path", @"C:\tools", RegistryValueKind.String);

        var (value, isExpandable) = RegistryPathIo.Read(_key, "Path");

        value.Should().Be(@"C:\tools");
        isExpandable.Should().BeFalse();
    }

    [Fact]
    public void Read_NonStringKind_Throws()
    {
        _key.SetValue("Path", 42, RegistryValueKind.DWord);

        var act = () => RegistryPathIo.Read(_key, "Path");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Write_Expandable_PersistsAsRegExpandSz()
    {
        RegistryPathIo.Write(_key, "Path", @"%USERPROFILE%\bin;C:\tools", isExpandable: true);

        _key.GetValueKind("Path").Should().Be(RegistryValueKind.ExpandString);
        _key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            .Should().Be(@"%USERPROFILE%\bin;C:\tools");
    }

    [Fact]
    public void Write_NonExpandable_PersistsAsRegSz()
    {
        RegistryPathIo.Write(_key, "Path", @"C:\tools", isExpandable: false);

        _key.GetValueKind("Path").Should().Be(RegistryValueKind.String);
    }

    [Fact]
    public void RoundTrip_PreservesExpandSzAndRawValue()
    {
        _key.SetValue("Path", @"%USERPROFILE%\bin", RegistryValueKind.ExpandString);

        var (value, isExpandable) = RegistryPathIo.Read(_key, "Path");
        RegistryPathIo.Write(_key, "Path", value + @";C:\tools", isExpandable);

        _key.GetValueKind("Path").Should().Be(RegistryValueKind.ExpandString);
        _key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            .Should().Be(@"%USERPROFILE%\bin;C:\tools");
    }
}
