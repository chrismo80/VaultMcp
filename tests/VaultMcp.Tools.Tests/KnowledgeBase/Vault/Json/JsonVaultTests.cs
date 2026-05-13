using Is.Assertions;
using System.Text.Json;
using VaultMcp.Tools.KnowledgeBase;
using VaultMcp.Tools.KnowledgeBase.Vault;
using VaultMcp.Tools.KnowledgeBase.Vault.Json;
using VaultMcp.Tools.KnowledgeBase.Search;
using Xunit;

namespace VaultMcp.Tools.Tests.KnowledgeBase.Vault.Json;

public sealed class JsonVaultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VaultMcp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetStatus_counts_json_files_only()
    {
        Directory.CreateDirectory(_root);
        WriteVaultFile(Path.Combine(_root, "a.json"), "# A");
        WriteVaultFile(Path.Combine(_root, "b.json"), "# B");
        WriteVaultFile(Path.Combine(_root, "c.txt"), "ignore");

        var vault = new JsonVault(_root);

        var status = vault.GetStatus();

        status.Exists.IsTrue();
        status.NoteCount.Is(2);
    }

    [Fact]
    public void ListNotes_uses_title_and_relative_paths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "concepts"));
        WriteVaultFile(Path.Combine(_root, "concepts", "aggregate.json"), "# Aggregate Root\n\nBody");

        var vault = new JsonVault(_root);

        var notes = vault.ListNotes();

        notes.Count.Is(1);
        notes[0].Path.Is(Path.Combine("concepts", "aggregate.json"));
        notes[0].Title.Is("Aggregate Root");
    }

    [Fact]
    public void ListNotes_falls_back_to_file_name_when_title_is_missing()
    {
        Directory.CreateDirectory(_root);
        WriteVaultFile(Path.Combine(_root, "pricing.json"), "No heading here");

        var vault = new JsonVault(_root);

        var notes = vault.ListNotes();

        notes[0].Title.Is("pricing");
    }

    [Fact]
    public void GetNote_returns_content_and_title_for_relative_path()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-flow.json"), "# Invoice Flow\n\nStep 1");

        var vault = new JsonVault(_root);

        var note = vault.GetNote(Path.Combine("workflows", "invoice-flow.json"));

        note.Path.Is(Path.Combine("workflows", "invoice-flow.json"));
        note.Title.Is("Invoice Flow");
        note.Content.Is("# Invoice Flow\n\nStep 1");
    }

    [Fact]
    public void GetNote_accepts_non_native_directory_separators()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-flow.json"), "# Invoice Flow\n\nStep 1");

        var vault = new JsonVault(_root);

        var note = vault.GetNote(WithForeignSeparators(Path.Combine("workflows", "invoice-flow.json")));

        note.Path.Is(Path.Combine("workflows", "invoice-flow.json"));
        note.Title.Is("Invoice Flow");
    }

    [Fact]
    public void GetNote_respects_metadata_and_max_chars_budget()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "order.json"),
            "---\nkind: term\ntags:\n - ordering\naliases:\n - Sales Order\n---\n# Order\n\nThis is a long body section for context budgeting.");

        var vault = new JsonVault(_root);

        var note = vault.GetNote(Path.Combine("glossary", "order.json"), maxChars: 30);

        note.Title.Is("Order");
        note.Kind.Is("term");
        note.Tags!.Contains("ordering", StringComparer.OrdinalIgnoreCase).IsTrue();
        note.Aliases!.Contains("Sales Order", StringComparer.OrdinalIgnoreCase).IsTrue();
        note.IsTruncated.IsTrue();
        note.Content.Length.Is(30);
    }

    [Fact]
    public void GetNote_hides_internal_learning_hash_markers_from_returned_content()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "order.json"),
            "# Order\n\n<!-- vaultmcp:learning-hash=abc123 -->\n\nUseful content.");

        var vault = new JsonVault(_root);

        var note = vault.GetNote(Path.Combine("glossary", "order.json"));

        note.Content.Contains("vaultmcp:learning-hash", StringComparison.OrdinalIgnoreCase).IsFalse();
        note.Content.Contains("Useful content.", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void GetNote_normalizes_crlf_when_stripping_internal_learning_hash_markers()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "order.json"),
            "# Order\r\n\r\n<!-- vaultmcp:learning-hash=abc123 -->\r\n\r\nUseful content.\r\n");

        var vault = new JsonVault(_root);

        var note = vault.GetNote(Path.Combine("glossary", "order.json"));

        note.Content.Contains('\r').IsFalse();
        note.Content.Contains("Useful content.", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void GetNote_rejects_path_traversal()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        var exception = Record.Exception(() => vault.GetNote(Path.Combine("..", "secrets.json")));

        exception.IsNotNull();
        exception.Is<ArgumentException>();
        exception.Message.Contains("escapes the configured vault root", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void SearchNotes_prefers_title_matches_over_body_only_matches()
    {
        Directory.CreateDirectory(Path.Combine(_root, "concepts"));
        Directory.CreateDirectory(Path.Combine(_root, "pitfalls"));
        WriteVaultFile(Path.Combine(_root, "concepts", "order.json"), "# Order\n\nOrder aggregate overview.");
        WriteVaultFile(Path.Combine(_root, "pitfalls", "shipping.json"), "# Shipping\n\nThis flow mentions order twice. Order must be validated.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("order");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("concepts", "order.json"));
        results[1].Path.Is(Path.Combine("pitfalls", "shipping.json"));
    }

    [Fact]
    public void SearchNotes_delegates_to_configured_search_implementation()
    {
        Directory.CreateDirectory(_root);
        WriteVaultFile(Path.Combine(_root, "pricing.json"), "# Pricing\n\nBody");

        var expected = new[]
        {
            new VaultSearchResult("custom.json", "Custom", "Injected", 123)
        };

        var search = new StubSearch(searchNotes: expected);
        var vault = new JsonVault(_root, search);

        var results = vault.SearchNotes("pricing");

        search.SearchNotesCalls.Is(1);
        results.Count.Is(1);
        results[0].Path.Is("custom.json");
    }

    [Fact]
    public void SearchNotes_returns_excerpt_around_match()
    {
        Directory.CreateDirectory(_root);
        WriteVaultFile(Path.Combine(_root, "pricing.json"), "# Pricing\n\nThe surcharge rule applies when the fragile order exceeds the threshold.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("fragile");

        results.Count.Is(1);
        results[0].Excerpt.Contains("fragile order", StringComparison.OrdinalIgnoreCase).IsTrue();
    }

    [Fact]
    public void SearchNotes_omits_internal_learning_hash_markers_from_excerpts()
    {
        Directory.CreateDirectory(_root);
        WriteVaultFile(
            Path.Combine(_root, "pricing.json"),
            "# Pricing\n\n<!-- vaultmcp:learning-hash=abc123 -->\n\nThe fragile order exceeds the threshold.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("fragile");

        results.Count.Is(1);
        results[0].Excerpt.Contains("vaultmcp:learning-hash", StringComparison.OrdinalIgnoreCase).IsFalse();
        results[0].Excerpt.Contains("fragile order", StringComparison.OrdinalIgnoreCase).IsTrue();
    }

    [Fact]
    public void SearchNotes_prefers_section_matches_over_body_only_matches()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-correction.json"), "# Invoice Correction\n\nShort summary.");
        WriteVaultFile(Path.Combine(_root, "notes", "shipping.json"), "# Shipping\n\nInvoice appears here. Much later the correction process is mentioned too.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("invoice correction");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("workflows", "invoice-correction.json"));
    }

    [Fact]
    public void SearchNotes_matches_transliterated_query_against_umlaut_note()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(Path.Combine(_root, "glossary", "fundstück.json"), "# Fundstück\n\nDas Fundstück wird im Archiv erfasst.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("Fundstueck");

        results.Count.Is(1);
        results[0].Path.Is(Path.Combine("glossary", "fundstück.json"));
    }

    [Fact]
    public void SearchNotes_prefers_exact_phrase_over_separate_term_hits()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-correction.json"), "# Billing\n\nThe invoice correction path starts after rejection.");
        WriteVaultFile(Path.Combine(_root, "notes", "invoice-and-correction.json"), "# Billing Retry\n\nAn invoice may fail. A manual correction happens later.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("invoice correction");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("workflows", "invoice-correction.json"));
    }

    [Fact]
    public void ListNotes_refreshes_cached_index_after_external_file_creation()
    {
        Directory.CreateDirectory(_root);
        using var vault = new JsonVault(_root);

        vault.ListNotes().Count.Is(0);

        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(Path.Combine(_root, "glossary", "order.json"), "# Order\n\nCanonical domain term.");

        WaitUntil(() => vault.ListNotes().Any(note => string.Equals(note.Path, Path.Combine("glossary", "order.json"), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SearchNotes_refreshes_cached_index_after_external_file_change()
    {
        Directory.CreateDirectory(_root);
        WriteVaultFile(Path.Combine(_root, "pricing.json"), "# Pricing\n\nalpha token");
        using var vault = new JsonVault(_root);

        vault.SearchNotes("alpha").Count.Is(1);

        WriteVaultFile(Path.Combine(_root, "pricing.json"), "# Pricing\n\nbeta token");

        WaitUntil(() =>
        {
            var betaResults = vault.SearchNotes("beta");
            var alphaResults = vault.SearchNotes("alpha");
            return betaResults.Count == 1 && alphaResults.Count == 0;
        });
    }

    [Fact]
    public void SearchNotes_prefers_tag_matches_over_body_only_matches()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data-flows"));
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        WriteVaultFile(
            Path.Combine(_root, "data-flows", "invoice-export.json"),
            "---\nkind: data-flow\ntags:\n - reporting\n---\n# Invoice Export\n\nExports approved invoices.");
        WriteVaultFile(Path.Combine(_root, "notes", "misc.json"), "# Misc\n\nThis note mentions reporting in passing.");

        var vault = new JsonVault(_root);

        var results = vault.SearchNotes("reporting");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("data-flows", "invoice-export.json"));
    }

    [Fact]
    public void FindTerm_prefers_exact_title_match()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        WriteVaultFile(Path.Combine(_root, "glossary", "order.json"), "# Order\n\nCanonical domain term.");
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice.json"), "# Invoice Flow\n\nOrder gets processed during invoicing.");

        var vault = new JsonVault(_root);

        var results = vault.FindTerm("Order");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("glossary", "order.json"));
        results[0].Title.Is("Order");
    }

    [Fact]
    public void FindTerm_uses_aliases()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "order.json"),
            "---\nkind: term\naliases:\n - Sales Order\n---\n# Order\n\nCanonical domain term.");

        var vault = new JsonVault(_root);

        var results = vault.FindTerm("Sales Order");

        results.Count.Is(1);
        results[0].Path.Is(Path.Combine("glossary", "order.json"));
    }

    [Fact]
    public void FindTerm_prefers_alias_match_on_term_notes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "order.json"),
            "---\nkind: term\naliases:\n - Sales Order\n---\n# Order\n\nCanonical domain term.");
        WriteVaultFile(
            Path.Combine(_root, "workflows", "sales-order.json"),
            "---\nkind: workflow\naliases:\n - Sales Order\n---\n# Sales Order Workflow\n\nDescribes the process.");

        var vault = new JsonVault(_root);

        var results = vault.FindTerm("Sales Order");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("glossary", "order.json"));
    }

    [Fact]
    public void FindTerm_matches_umlaut_query_against_transliterated_note()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "fundstueck.json"),
            "---\nkind: term\n---\n# Fundstueck\n\nDas Fundstueck wird im Archiv erfasst.");

        var vault = new JsonVault(_root);

        var results = vault.FindTerm("Fundstück");

        results.Count.Is(1);
        results[0].Path.Is(Path.Combine("glossary", "fundstueck.json"));
    }

    [Fact]
    public void FindTerm_prefers_transliterated_alias_match_on_term_notes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "glossary"));
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        WriteVaultFile(
            Path.Combine(_root, "glossary", "fundstueck.json"),
            "---\nkind: term\naliases:\n - Fundstueck\n---\n# Fundstueck\n\nCanonical domain term.");
        WriteVaultFile(
            Path.Combine(_root, "workflows", "fundstueck-flow.json"),
            "---\nkind: workflow\naliases:\n - Fundstueck\n---\n# Fundstueck Workflow\n\nDescribes the process.");

        var vault = new JsonVault(_root);

        var results = vault.FindTerm("Fundstück");

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("glossary", "fundstueck.json"));
    }

    [Fact]
    public void FindRelatedNotes_prefers_shared_terms_and_same_directory()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        Directory.CreateDirectory(Path.Combine(_root, "concepts"));
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-flow.json"), "# Invoice Flow\n\nOrder validation happens before invoice correction.");
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-correction.json"), "# Invoice Correction Flow\n\nInvoice correction starts after order validation.");
        WriteVaultFile(Path.Combine(_root, "concepts", "tenant-boundary.json"), "# Tenant Boundary\n\nTenant segregation across exports.");

        var vault = new JsonVault(_root);

        var results = vault.FindRelatedNotes(Path.Combine("workflows", "invoice-flow.json"));

        results.Count.Is(1);
        results[0].Path.Is(Path.Combine("workflows", "invoice-correction.json"));
        (results[0].Score > 0).IsTrue();
    }

    [Fact]
    public void FindRelatedNotes_accepts_non_native_directory_separators()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        Directory.CreateDirectory(Path.Combine(_root, "concepts"));
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-flow.json"), "# Invoice Flow\n\nOrder validation happens before invoice correction.");
        WriteVaultFile(Path.Combine(_root, "workflows", "invoice-correction.json"), "# Invoice Correction Flow\n\nInvoice correction starts after order validation.");
        WriteVaultFile(Path.Combine(_root, "concepts", "tenant-boundary.json"), "# Tenant Boundary\n\nTenant segregation across exports.");

        var vault = new JsonVault(_root);

        var results = vault.FindRelatedNotes(WithForeignSeparators(Path.Combine("workflows", "invoice-flow.json")));

        results.Count.Is(1);
        results[0].Path.Is(Path.Combine("workflows", "invoice-correction.json"));
    }

    [Fact]
    public void FindRelatedNotes_prefers_explicit_related_links_over_shared_terms()
    {
        Directory.CreateDirectory(Path.Combine(_root, "workflows"));
        Directory.CreateDirectory(Path.Combine(_root, "decisions"));
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        WriteVaultFile(
            Path.Combine(_root, "workflows", "invoice-flow.json"),
            "---\nkind: workflow\nrelated:\n - decisions/invoice-policy.json\n---\n# Invoice Flow\n\nInvoice correction starts after validation.");
        WriteVaultFile(
            Path.Combine(_root, "decisions", "invoice-policy.json"),
            "---\nkind: decision\n---\n# Invoice Policy\n\nExplains why invoice correction needs approval.");
        WriteVaultFile(
            Path.Combine(_root, "notes", "invoice-terms.json"),
            "# Invoice Notes\n\nInvoice correction validation invoice correction validation invoice correction validation.");

        var vault = new JsonVault(_root);

        var results = vault.FindRelatedNotes(Path.Combine("workflows", "invoice-flow.json"));

        results.Count.Is(2);
        results[0].Path.Is(Path.Combine("decisions", "invoice-policy.json"));
    }

    [Fact]
    public void CaptureLearning_creates_new_note_in_bucket_directory()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        var result = vault.CaptureLearning(new VaultLearningCapture(
            "term",
            "Order Aggregate",
            "Defines the aggregate boundary for orders.",
            "Used in pricing and fulfillment flows.",
            ["ddd", "orders"],
            Aliases: ["Order Root"],
            Examples: ["Pricing loads the aggregate before discount evaluation."]));

        result.Created.IsTrue();
        result.Path.Is(Path.Combine("glossary", "order-aggregate.json"));
        result.Kind.Is("term");

        var fileContent = File.ReadAllText(Path.Combine(_root, "glossary", "order-aggregate.json"));
        fileContent.Contains("\"kind\": \"term\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"tags\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"aliases\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"hash\":", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Defines the aggregate boundary for orders.", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Order Root", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"examples\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("domain", StringComparison.OrdinalIgnoreCase).IsTrue();
    }

    [Fact]
    public void CaptureLearning_appends_to_existing_note_for_same_title()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        vault.CaptureLearning(new VaultLearningCapture(
            "workflow",
            "Invoice Correction Flow",
            "Starts after invoice rejection.",
            null,
            []));

        var second = vault.CaptureLearning(new VaultLearningCapture(
            "workflow",
            "Invoice Correction Flow",
            "Requires customer notification before resubmission.",
            "Edge case: credit note already issued.",
            [],
            Steps: ["Notify customer", "Re-open invoice", "Re-submit for approval"]));

        second.Appended.IsTrue();
        second.Created.IsFalse();

        var fileContent = File.ReadAllText(Path.Combine(_root, "workflows", "invoice-correction-flow.json"));
        fileContent.Contains("\"learnings\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Requires customer notification before resubmission.", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"steps\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Notify customer", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void CaptureLearning_returns_unchanged_when_same_learning_is_already_present()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        vault.CaptureLearning(new VaultLearningCapture(
            "invariant",
            "Tenant Boundary",
            "Tenant data must never cross account boundaries.",
            "Applies to exports and background jobs.",
            []));

        var second = vault.CaptureLearning(new VaultLearningCapture(
            "rule",
            "Tenant Boundary",
            "Tenant data must never cross account boundaries.",
            "Applies to exports and background jobs.",
            []));

        second.Unchanged.IsTrue();
        second.Created.IsFalse();
        second.Appended.IsFalse();
    }

    [Fact]
    public void CaptureLearning_uses_deterministic_hash_for_equivalent_content()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        vault.CaptureLearning(new VaultLearningCapture(
            "term",
            "Order Aggregate",
            "Defines the aggregate boundary for orders.",
            "Used in pricing and fulfillment flows.",
            [],
            Aliases: ["Order Root", "Sales Aggregate"],
            Examples: ["Pricing loads the aggregate before discount evaluation."]));

        var second = vault.CaptureLearning(new VaultLearningCapture(
            "term",
            "Order Aggregate",
            " defines   the aggregate boundary for orders. ",
            "Used in pricing and fulfillment flows.",
            [],
            Aliases: ["sales aggregate", "order root"],
            Examples: ["Pricing loads the aggregate before discount evaluation."]));

        second.Unchanged.IsTrue();
        second.Appended.IsFalse();
    }

    [Fact]
    public void CaptureLearning_normalizes_legacy_aliases_to_canonical_kind()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        var result = vault.CaptureLearning(new VaultLearningCapture(
            "glossary",
            "Payment Allocation",
            "Canonical meaning of payment allocation.",
            null,
            []));

        result.Kind.Is("term");
        result.Path.Is(Path.Combine("glossary", "payment-allocation.json"));

        var fileContent = File.ReadAllText(Path.Combine(_root, "glossary", "payment-allocation.json"));
        fileContent.Contains("\"kind\": \"term\"", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void CaptureLearning_renders_structured_sections_for_data_flow()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        var result = vault.CaptureLearning(new VaultLearningCapture(
            "data-flow",
            "Invoice Export",
            "Moves approved invoices into the reporting pipeline.",
            null,
            [],
            Steps: ["Read approved invoices", "Map to export DTO", "Publish to reporting queue"],
            Source: "Billing database",
            Sink: "Reporting queue"));

        result.Path.Is(Path.Combine("data-flows", "invoice-export.json"));

        var fileContent = File.ReadAllText(Path.Combine(_root, "data-flows", "invoice-export.json"));
        fileContent.Contains("\"source\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Billing database", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"sink\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Reporting queue", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"steps\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("Read approved invoices", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void CaptureLearning_renders_structured_sections_for_decision()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        vault.CaptureLearning(new VaultLearningCapture(
            "decision",
            "Use Append-Only Learning Notes",
            "Keep learned knowledge reviewable in git.",
            null,
            [],
            Context: "Agents learn small domain facts during implementation.",
            Choice: "Append structured json instead of storing opaque blobs.",
            Consequence: "Humans can review diffs, but note growth must stay controlled."));

        var fileContent = File.ReadAllText(Path.Combine(_root, "decisions", "use-append-only-learning-notes.json"));
        fileContent.Contains("\"context\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"choice\"", StringComparison.Ordinal).IsTrue();
        fileContent.Contains("\"consequence\"", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public void CaptureLearning_rejects_unknown_kind()
    {
        Directory.CreateDirectory(_root);
        var vault = new JsonVault(_root);

        var exception = Record.Exception(() => vault.CaptureLearning(new VaultLearningCapture(
            "random",
            "Something",
            "Summary",
            null,
            [])));

        exception.IsNotNull();
        exception.Is<ArgumentException>();
        exception.Message.Contains("Unsupported learning kind", StringComparison.Ordinal).IsTrue();
        exception.Message.Contains("term, workflow, data-flow, invariant, pitfall, decision", StringComparison.Ordinal).IsTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(50);
        }

        Assert.True(condition());
    }

    private static string WithForeignSeparators(string path)
        => Path.DirectorySeparatorChar == '/'
            ? path.Replace('/', '\\')
            : path.Replace('\\', '/');

    private static void WriteVaultFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) ||
            content.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            File.WriteAllText(path, content);
            return;
        }

        File.WriteAllText(path, BuildJsonNote(path, content));
    }

    private static string BuildJsonNote(string path, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var index = 0;
        string? kind = null;
        var tags = new List<string>();
        var aliases = new List<string>();
        var related = new List<string>();

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            index = 1;
            string? currentList = null;
            for (; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (line == "---")
                {
                    index++;
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("- ", StringComparison.Ordinal) && currentList is not null)
                {
                    AddListValue(currentList, line[2..].Trim());
                    continue;
                }

                currentList = null;
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                var key = line[..separator].Trim().ToLowerInvariant();
                var value = line[(separator + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    currentList = key;
                    continue;
                }

                switch (key)
                {
                    case "kind":
                        kind = value;
                        break;
                    case "tags":
                    case "aliases":
                    case "related":
                        AddListValue(key, value.Trim('"', '\''));
                        break;
                }
            }
        }

        var title = Path.GetFileNameWithoutExtension(path);
        var bodyLines = new List<string>();
        for (; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("<!-- vaultmcp:learning-hash=", StringComparison.OrdinalIgnoreCase))
                continue;

            if (trimmed.StartsWith("# ", StringComparison.Ordinal) && string.Equals(title, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal))
            {
                title = trimmed[2..].Trim();
                continue;
            }

            bodyLines.Add(line);
        }

        var summary = string.Join("\n", bodyLines).Trim();

        var payload = new
        {
            schema = "vault-note/v1",
            id = $"{(kind ?? "note")}.{Path.GetFileNameWithoutExtension(path)}",
            kind,
            title,
            summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
            details = (string?)null,
            tags = tags.Count == 0 ? null : tags,
            aliases = aliases.Count == 0 ? null : aliases,
            related = related.Count == 0 ? null : related,
            confidence = (string?)null,
            scalars = (object?)null,
            lists = (object?)null,
            sections = (object?)null,
            learnings = (object?)null,
            meta = (object?)null
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        void AddListValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            switch (key)
            {
                case "tags":
                    tags.Add(value);
                    break;
                case "aliases":
                    aliases.Add(value);
                    break;
                case "related":
                    related.Add(value);
                    break;
            }
        }
    }
}

internal sealed class StubSearch(
    IReadOnlyList<VaultSearchResult>? searchNotes = null,
    IReadOnlyList<VaultSearchResult>? findTerm = null,
    IReadOnlyList<VaultSearchResult>? findRelatedNotes = null) : ISearch
{
    public int SearchNotesCalls { get; private set; }

    public IReadOnlyList<VaultSearchResult> SearchNotes(IReadOnlyList<VaultIndexedNote> notes, string query, int maxCount = 10)
    {
        SearchNotesCalls++;
        return (searchNotes ?? []).Take(maxCount).ToArray();
    }

    public IReadOnlyList<VaultSearchResult> FindTerm(IReadOnlyList<VaultIndexedNote> notes, string term, int maxCount = 10)
        => (findTerm ?? []).Take(maxCount).ToArray();

    public IReadOnlyList<VaultSearchResult> FindRelatedNotes(IReadOnlyList<VaultIndexedNote> notes, VaultIndexedNote source, int maxCount = 5)
        => (findRelatedNotes ?? []).Take(maxCount).ToArray();
}
