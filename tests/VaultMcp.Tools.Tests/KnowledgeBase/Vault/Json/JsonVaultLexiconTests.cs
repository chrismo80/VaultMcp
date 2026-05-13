using Is.Assertions;
using System.Text.Json;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;
using Xunit;

namespace VaultMcp.Tools.Tests.KnowledgeBase.Vault.Json;

public sealed class JsonVaultLexiconTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VaultMcp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CaptureTerm_creates_new_glossary_entry()
    {
        Directory.CreateDirectory(_root);
        using var vault = new JsonVault(_root);

        var result = vault.CaptureTerm(new("Chargenfreigabe", "Fachliche Freigabe einer Charge vor Versand.", ["Batch Release"], "Freigabearten"));

        result.Created.IsTrue();
        result.Path.Is(Path.Combine("glossary", "chargenfreigabe.json"));

        var json = File.ReadAllText(Path.Combine(_root, "glossary", "chargenfreigabe.json"));
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("kind").GetString().Is("term");
        doc.RootElement.GetProperty("aliases")[0].GetString().Is("Batch Release");
        doc.RootElement.GetProperty("scalars").GetProperty("group").GetString().Is("Freigabearten");
    }

    [Fact]
    public void CaptureTerm_merges_aliases_into_existing_entry_via_alias_match()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        File.WriteAllText(
            Path.Combine(_root, "glossary", "chargenfreigabe.json"),
            """
            {
              "schema": "vault-note/v1",
              "id": "term.chargenfreigabe",
              "kind": "term",
              "title": "Chargenfreigabe",
              "summary": "Fachliche Freigabe einer Charge.",
              "aliases": ["Batch Release"],
              "scalars": {
                "group": "Freigabearten"
              }
            }
            """);

        using var vault = new JsonVault(_root);

        var result = vault.CaptureTerm(new("Batch Release", "Fachliche Freigabe einer Charge.", ["Freigabe Charge"], null));

        result.Updated.IsTrue();
        var json = File.ReadAllText(Path.Combine(_root, "glossary", "chargenfreigabe.json"));
        using var doc = JsonDocument.Parse(json);
        var aliases = doc.RootElement.GetProperty("aliases").EnumerateArray().Select(x => x.GetString()).ToArray();
        aliases.Contains("Batch Release").IsTrue();
        aliases.Contains("Freigabe Charge").IsTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
