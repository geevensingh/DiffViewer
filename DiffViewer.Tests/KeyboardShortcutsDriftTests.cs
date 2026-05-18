using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Xml.Linq;
using DiffViewer.Models;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

/// <summary>
/// Drift-detection test for the F1 cheat sheet. <see cref="KeyboardShortcutCatalog"/>
/// is the documentation layer; <c>MainWindow.xaml</c>'s
/// <c>&lt;Window.InputBindings&gt;</c> is the execution layer. This
/// test parses the XAML at runtime and asserts a bijection between
/// the two — every <c>&lt;KeyBinding&gt;</c> in the XAML has a row
/// in the catalog, and every keyboard row in the catalog corresponds
/// to a real <c>KeyBinding</c>.
///
/// <para>The XAML file is copied into the test output dir as
/// <c>TestData/MainWindow.xaml</c> via a <c>&lt;None&gt;</c> entry in
/// <c>DiffViewer.Tests.csproj</c>.</para>
/// </summary>
public class KeyboardShortcutsDriftTests
{
    private static readonly XNamespace PresentationNs =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly KeyConverter KeyConverter = new();
    private static readonly ModifierKeysConverter ModifiersConverter = new();

    [Fact]
    public void EveryXamlKeyBinding_AppearsInTheCatalog()
    {
        var xamlBindings = LoadXamlKeyBindings();
        var catalogBindings = CollectCatalogXamlBindings();

        var orphans = xamlBindings.Except(catalogBindings).ToList();

        orphans.Should().BeEmpty(
            because: "every <KeyBinding> in MainWindow.xaml must be " +
                     "documented in KeyboardShortcutCatalog so the F1 " +
                     "cheat sheet never goes stale. Missing entries: " +
                     string.Join(", ", orphans.Select(Format)));
    }

    [Fact]
    public void EveryCatalogXamlBinding_HasAMatchingXamlKeyBinding()
    {
        var xamlBindings = LoadXamlKeyBindings();
        var catalogBindings = CollectCatalogXamlBindings();

        var fictional = catalogBindings.Except(xamlBindings).ToList();

        fictional.Should().BeEmpty(
            because: "the catalog must not document shortcuts that " +
                     "aren't actually wired up in MainWindow.xaml. " +
                     "Stale entries: " +
                     string.Join(", ", fictional.Select(Format)));
    }

    private static HashSet<KeyChord> LoadXamlKeyBindings()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "MainWindow.xaml");

        File.Exists(path).Should().BeTrue(
            because: $"MainWindow.xaml should be copied to '{path}' " +
                     "by the <None>+CopyToOutputDirectory entry in " +
                     "DiffViewer.Tests.csproj");

        var doc = XDocument.Load(path);

        // The KeyBindings live under <Window.InputBindings>. We only
        // want bindings on the Window itself — nested controls can
        // have their own InputBindings that the F1 cheat sheet need
        // not document.
        var inputBindings = doc.Root!
            .Element(PresentationNs + "Window.InputBindings");

        inputBindings.Should().NotBeNull(
            because: "<Window.InputBindings> is where all global " +
                     "shortcuts live; if this disappears the cheat " +
                     "sheet has nothing to compare against");

        var bindings = new HashSet<KeyChord>();
        foreach (var kb in inputBindings!.Elements(PresentationNs + "KeyBinding"))
        {
            string? keyAttr = kb.Attribute("Key")?.Value;
            keyAttr.Should().NotBeNullOrEmpty(
                because: "every <KeyBinding> must carry a Key= attribute");

            string? modifiersAttr = kb.Attribute("Modifiers")?.Value;

            Key key = (Key)KeyConverter.ConvertFromString(keyAttr!)!;
            ModifierKeys modifiers = modifiersAttr is null
                ? ModifierKeys.None
                : (ModifierKeys)ModifiersConverter.ConvertFromString(modifiersAttr)!;

            bindings.Add(new KeyChord(key, modifiers));
        }

        return bindings;
    }

    private static HashSet<KeyChord> CollectCatalogXamlBindings()
    {
        return KeyboardShortcutCatalog.Groups
            .SelectMany(g => g.Entries)
            .SelectMany(e => e.XamlBindings)
            .ToHashSet();
    }

    private static string Format(KeyChord c) =>
        c.Modifiers == ModifierKeys.None ? c.Key.ToString() : $"{c.Modifiers}+{c.Key}";
}
