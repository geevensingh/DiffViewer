using System.Linq;
using System.Windows.Input;
using DiffViewer.Models;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

/// <summary>
/// Shape-level tests on <see cref="KeyboardShortcutCatalog"/>. Companion
/// to <see cref="KeyboardShortcutsDriftTests"/>, which verifies the
/// catalog's <see cref="ShortcutEntry.XamlBindings"/> matches the
/// actual <c>&lt;KeyBinding&gt;</c>s in <c>MainWindow.xaml</c>.
/// </summary>
public class KeyboardShortcutCatalogTests
{
    [Fact]
    public void Groups_HasExpectedSections()
    {
        var names = KeyboardShortcutCatalog.Groups.Select(g => g.Name).ToList();
        names.Should().Contain(new[] { "View", "Navigation", "App", "Mouse actions" });
    }

    [Fact]
    public void Groups_AreNonEmpty()
    {
        foreach (var group in KeyboardShortcutCatalog.Groups)
        {
            group.Entries.Should().NotBeEmpty(
                because: $"every group should have at least one row (group: {group.Name})");
        }
    }

    [Fact]
    public void EveryEntry_HasNonBlankGestureAndDescription()
    {
        foreach (var group in KeyboardShortcutCatalog.Groups)
        {
            foreach (var entry in group.Entries)
            {
                entry.Gesture.Should().NotBeNullOrWhiteSpace(
                    because: $"row in '{group.Name}' is missing a gesture string");
                entry.Description.Should().NotBeNullOrWhiteSpace(
                    because: $"row '{entry.Gesture}' in '{group.Name}' is missing a description");
            }
        }
    }

    [Fact]
    public void XamlBindingTuples_AreUniqueAcrossTheCatalog()
    {
        // The XAML side enforces this anyway (a duplicate KeyBinding
        // would fire ambiguous WPF input routing), but catching it
        // here gives a clearer failure message than the drift test
        // would.
        var allBindings = KeyboardShortcutCatalog.Groups
            .SelectMany(g => g.Entries)
            .SelectMany(e => e.XamlBindings)
            .ToList();

        allBindings.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void F1_AppearsInAppGroup_SoTheCheatSheetDocumentsItself()
    {
        var appGroup = KeyboardShortcutCatalog.Groups.Single(g => g.Name == "App");
        appGroup.Entries.Should().Contain(e =>
            e.XamlBindings.Any(b => b.Key == Key.F1 && b.Modifiers == ModifierKeys.None));
    }

    [Fact]
    public void MouseActionsGroup_HasNoKeyboardBindings()
    {
        // Mouse-only entries deliberately have an empty XamlBindings
        // list; the drift test relies on this to exclude them from
        // the keyboard-binding bijection.
        var mouseGroup = KeyboardShortcutCatalog.Groups.Single(g => g.Name == "Mouse actions");
        foreach (var entry in mouseGroup.Entries)
        {
            entry.XamlBindings.Should().BeEmpty(
                because: $"mouse-action row '{entry.Description}' has a stray XAML binding");
        }
    }
}
