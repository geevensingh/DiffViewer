using System.Linq;
using DiffViewer.Rendering;
using FluentAssertions;
using ICSharpCode.AvalonEdit.Highlighting;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class CustomHighlightingRegistrarTests
{
    public CustomHighlightingRegistrarTests()
    {
        // RegisterAll is idempotent and the manager overwrites by name on
        // re-registration, so it's safe to call once per test instance.
        CustomHighlightingRegistrar.RegisterAll(HighlightingManager.Instance);
    }

    [Theory]
    [InlineData(".ts", "TypeScript")]
    [InlineData(".tsx", "TypeScript")]
    [InlineData(".yaml", "YAML")]
    [InlineData(".yml", "YAML")]
    [InlineData(".go", "Go")]
    [InlineData(".rs", "Rust")]
    [InlineData(".rb", "Ruby")]
    [InlineData(".sh", "Bash")]
    [InlineData(".bash", "Bash")]
    [InlineData(".zsh", "Bash")]
    [InlineData(".toml", "TOML")]
    public void RegisterAll_RegistersDefinition_ForExtension(string extension, string expectedName)
    {
        var def = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        def.Should().NotBeNull($"extension {extension} should resolve to a registered definition");
        def!.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData(".TS", "TypeScript")]
    [InlineData(".YAML", "YAML")]
    [InlineData(".Rs", "Rust")]
    [InlineData(".TOML", "TOML")]
    public void RegisterAll_LookupIsCaseInsensitive(string extension, string expectedName)
    {
        var def = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        def.Should().NotBeNull();
        def!.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData(".cs", "C#")]
    [InlineData(".js", "JavaScript")]
    [InlineData(".xml", "XML")]
    [InlineData(".json", "Json")]
    [InlineData(".py", "Python")]
    public void RegisterAll_DoesNotClobber_BundledDefinitions(string extension, string expectedName)
    {
        var def = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        def.Should().NotBeNull($"bundled extension {extension} must still resolve");
        def!.Name.Should().Be(expectedName);
    }

    [Fact]
    public void RegisterAll_IsIdempotent()
    {
        // Constructor already called RegisterAll; calling again must not throw.
        var act = () => CustomHighlightingRegistrar.RegisterAll(HighlightingManager.Instance);
        act.Should().NotThrow();
        HighlightingManager.Instance.GetDefinitionByExtension(".ts").Should().NotBeNull();
    }

    [Theory]
    [InlineData("TypeScript")]
    [InlineData("YAML")]
    [InlineData("Go")]
    [InlineData("Rust")]
    [InlineData("Ruby")]
    [InlineData("Bash")]
    [InlineData("TOML")]
    public void RegisterAll_XshdResource_ParsesAndExposesMainRuleSet(string definitionName)
    {
        var def = HighlightingManager.Instance.GetDefinition(definitionName);
        def.Should().NotBeNull();
        // Accessing MainRuleSet forces the lazy XSHD load, which is where
        // a malformed XSHD would surface a parse exception.
        def!.MainRuleSet.Should().NotBeNull();
    }

    [Fact]
    public void Entries_CoverAllAdvertisedLanguages()
    {
        CustomHighlightingRegistrar.Entries
            .Select(e => e.Name)
            .Should()
            .BeEquivalentTo(new[] { "TypeScript", "YAML", "Go", "Rust", "Ruby", "Bash", "TOML" });
    }
}
