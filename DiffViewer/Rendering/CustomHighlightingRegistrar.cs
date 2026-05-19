using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace DiffViewer.Rendering;

/// <summary>
/// Registers DiffViewer's hand-authored XSHD highlighting definitions with
/// the AvalonEdit <see cref="HighlightingManager"/>. AvalonEdit's bundled
/// definitions (C#, JavaScript, XML, etc.) are already registered by the
/// library; this class adds TypeScript, YAML, Go, Rust, Ruby, Bash, and TOML
/// on top.
///
/// <para>Registration uses the lazy <see cref="HighlightingManager.RegisterHighlighting(string, string[], Func{IHighlightingDefinition})"/>
/// overload so the XSHD resources are only parsed the first time a file
/// matching one of the extensions is opened. <see cref="RegisterAll"/> is
/// idempotent; calling it multiple times is a no-op after the first.</para>
/// </summary>
internal static class CustomHighlightingRegistrar
{
    private static readonly object _gate = new();
    private static bool _registered;

    /// <summary>One entry per definition this app contributes.</summary>
    internal readonly record struct Entry(string Name, string[] Extensions, string ResourceName);

    /// <summary>The set of definitions this app adds on top of AvalonEdit's
    /// bundled set. Exposed for tests; production callers should use
    /// <see cref="RegisterAll"/>.</summary>
    internal static IReadOnlyList<Entry> Entries { get; } = new[]
    {
        new Entry("TypeScript", new[] { ".ts", ".tsx" }, "DiffViewer.Resources.Highlighting.TypeScript.xshd"),
        new Entry("YAML",       new[] { ".yaml", ".yml" }, "DiffViewer.Resources.Highlighting.Yaml.xshd"),
        new Entry("Go",         new[] { ".go" },           "DiffViewer.Resources.Highlighting.Go.xshd"),
        new Entry("Rust",       new[] { ".rs" },           "DiffViewer.Resources.Highlighting.Rust.xshd"),
        new Entry("Ruby",       new[] { ".rb" },           "DiffViewer.Resources.Highlighting.Ruby.xshd"),
        new Entry("Bash",       new[] { ".sh", ".bash", ".zsh" }, "DiffViewer.Resources.Highlighting.Bash.xshd"),
        new Entry("TOML",       new[] { ".toml" },         "DiffViewer.Resources.Highlighting.Toml.xshd"),
    };

    /// <summary>
    /// Registers every definition in <see cref="Entries"/> with the given
    /// manager. Safe to call from any thread; second and later calls are
    /// no-ops.
    /// </summary>
    public static void RegisterAll(HighlightingManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        lock (_gate)
        {
            if (_registered) return;
            foreach (var entry in Entries)
            {
                // Capture by value so each closure binds the correct entry.
                Entry captured = entry;
                manager.RegisterHighlighting(
                    captured.Name,
                    captured.Extensions,
                    () => Load(captured.ResourceName, manager));
            }
            _registered = true;
        }
    }

    /// <summary>Test hook: force the next <see cref="RegisterAll"/> call to
    /// re-run. Production code never needs this — the manager itself
    /// overwrites definitions by name on subsequent registrations.</summary>
    internal static void ResetForTests()
    {
        lock (_gate) { _registered = false; }
    }

    private static IHighlightingDefinition Load(string resourceName, IHighlightingDefinitionReferenceResolver resolver)
    {
        Assembly asm = typeof(CustomHighlightingRegistrar).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Highlighting resource '{resourceName}' was not embedded in {asm.GetName().Name}. " +
                "Check the <EmbeddedResource> entries in DiffViewer.csproj.");
        }
        using var reader = new XmlTextReader(stream);
        return HighlightingLoader.Load(reader, resolver);
    }
}
