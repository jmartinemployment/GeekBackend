using System.Text.Json;
using GeekApplication.Models.Crm;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using GeekRepository.Repositories.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// Custom properties: what a definition and a value must be (CrmPropertyRules), and the store's
/// operations against an in-memory database: values are checked before any is written, a sensitive
/// property is hidden from a caller who may not see it, another tenant's record and definitions are
/// invisible, and a retired property keeps its values but hides them. The SQL itself (the unique
/// keys, the jsonb columns) is proved by the integration tests against Postgres.
/// </summary>
public class CrmPropertyTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    // ----- the rules -----

    [Theory]
    [InlineData("pms")]
    [InlineData("a")]
    [InlineData("practice_software_2")]
    public void A_key_starts_with_a_letter_and_uses_lower_case_digits_and_underscores(string key) =>
        Assert.True(CrmPropertyRules.IsKey(key));

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("Has Space")]
    [InlineData("UPPER")]
    [InlineData("dash-ed")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")]
    public void A_key_that_breaks_the_rule_is_refused(string key) => Assert.False(CrmPropertyRules.IsKey(key));

    [Fact]
    public void A_definition_needs_a_known_object_type_data_type_and_label()
    {
        Assert.Null(CrmPropertyRules.ValidateDefinition("organization", "pms", "Practice software", "text", null));
        Assert.NotNull(CrmPropertyRules.ValidateDefinition("company", "pms", "Practice software", "text", null));
        Assert.NotNull(CrmPropertyRules.ValidateDefinition("organization", "pms", "Practice software", "money", null));
        Assert.NotNull(CrmPropertyRules.ValidateDefinition("organization", "pms", "  ", "text", null));
        Assert.NotNull(CrmPropertyRules.ValidateDefinition("organization", "Bad Key", "Label", "text", null));
    }

    [Fact]
    public void Only_enum_properties_have_options_and_they_must_be_distinct_and_present()
    {
        Assert.NotNull(CrmPropertyRules.ValidateOptions("enum", null));
        Assert.NotNull(CrmPropertyRules.ValidateOptions("multi_enum", []));
        Assert.NotNull(CrmPropertyRules.ValidateOptions("enum", ["a", "a"]));
        Assert.NotNull(CrmPropertyRules.ValidateOptions("enum", ["a", " "]));
        Assert.NotNull(CrmPropertyRules.ValidateOptions("text", ["a"]));
        Assert.Null(CrmPropertyRules.ValidateOptions("enum", ["a", "b"]));
        Assert.Null(CrmPropertyRules.ValidateOptions("text", null));
    }

    [Theory]
    [InlineData("text", "\"hello\"", true)]
    [InlineData("text", "5", false)]
    [InlineData("number", "12.5", true)]
    [InlineData("number", "\"12\"", false)]
    [InlineData("date", "\"2026-11-11\"", true)]
    [InlineData("date", "\"11/11/2026\"", false)]
    [InlineData("date", "\"2026-13-01\"", false)]
    [InlineData("bool", "true", true)]
    [InlineData("bool", "\"true\"", false)]
    public void A_value_must_have_its_data_types_shape(string dataType, string raw, bool valid) =>
        Assert.Equal(valid, CrmPropertyRules.ValidateValue(dataType, null, Json(raw)) is null);

    [Fact]
    public void An_enum_value_is_one_option_and_a_multi_enum_is_a_list_of_distinct_options()
    {
        string[] options = ["dentrix", "eaglesoft"];
        Assert.Null(CrmPropertyRules.ValidateValue("enum", options, Json("\"dentrix\"")));
        Assert.NotNull(CrmPropertyRules.ValidateValue("enum", options, Json("\"other\"")));
        Assert.NotNull(CrmPropertyRules.ValidateValue("enum", options, Json("[\"dentrix\"]")));
        Assert.Null(CrmPropertyRules.ValidateValue("multi_enum", options, Json("[\"dentrix\",\"eaglesoft\"]")));
        Assert.NotNull(CrmPropertyRules.ValidateValue("multi_enum", options, Json("[\"dentrix\",\"dentrix\"]")));
        Assert.NotNull(CrmPropertyRules.ValidateValue("multi_enum", options, Json("[\"other\"]")));
        Assert.NotNull(CrmPropertyRules.ValidateValue("multi_enum", options, Json("[]")));
        Assert.NotNull(CrmPropertyRules.ValidateValue("multi_enum", options, Json("\"dentrix\"")));
    }

    [Fact]
    public void Text_longer_than_the_limit_is_refused()
    {
        Assert.Null(CrmPropertyRules.ValidateValue("text", null, Json(JsonSerializer.Serialize(new string('x', CrmPropertyRules.MaxTextLength)))));
        Assert.NotNull(CrmPropertyRules.ValidateValue("text", null, Json(JsonSerializer.Serialize(new string('x', CrmPropertyRules.MaxTextLength + 1)))));
    }

    // ----- domain matching in an import -----

    [Theory]
    [InlineData("https://www.acme.com/about", "acme.com", true)]
    [InlineData("acme.com", "http://ACME.com", true)]
    [InlineData("https://notacme.com", "acme.com", false)]
    [InlineData("https://acme.com.au", "acme.com", false)]
    [InlineData("https://shop.acme.com", "acme.com", false)]
    [InlineData(null, "acme.com", false)]
    [InlineData("acme.com", null, false)]
    public void Two_websites_are_the_same_site_only_when_their_domains_are_equal(string? a, string? b, bool same) =>
        Assert.Equal(same, CrmText.SameDomain(a, b));

    // ----- the store, in memory -----

    private static readonly Guid TenantA = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly Guid TenantB = Guid.Parse("6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2aff");

    /// <summary>No database, so no transaction and no tenant setting: the work runs, and the in-memory store keeps what it saved.</summary>
    private sealed class NoTenantScope : ICrmTenantScope
    {
        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
    }

    private sealed class Store : IDisposable
    {
        public CrmDbContext Db { get; } = new(new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase("crm-properties-" + Guid.NewGuid()).Options);
        public CrmRepository Repository { get; }
        public Guid OrganizationA { get; } = Guid.NewGuid();
        public Guid PersonB { get; } = Guid.NewGuid();

        public Store()
        {
            Repository = new CrmRepository(Db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new NoTenantScope());
            Db.Tenants.AddRange(
                new CrmTenantRow { Id = TenantA, Name = "A", Slug = "a" },
                new CrmTenantRow { Id = TenantB, Name = "B", Slug = "b" });
            Db.Participants.AddRange(
                new CrmParticipantRow { Id = OrganizationA, TenantId = TenantA, Kind = "organization", DisplayName = "Boca Dental", Source = "import" },
                new CrmParticipantRow { Id = PersonB, TenantId = TenantB, Kind = "person", DisplayName = "Other", Source = "import" });
            Db.SaveChanges();
        }

        public async Task<CrmPropertyDefinition> DefineAsync(string key, string dataType, string[]? options = null, bool sensitive = false, Guid? tenant = null, string objectType = "organization")
        {
            var made = await Repository.CreatePropertyDefinitionAsync(new(tenant ?? TenantA, objectType, key, key + " label", dataType, options, sensitive));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public Task<GeekApplication.Results.Result<IReadOnlyList<CrmPropertyValue>>> SetAsync(string key, string raw, bool includeSensitive = false, Guid? tenant = null, Guid? record = null) =>
            Repository.SetPropertyValuesAsync(new(tenant ?? TenantA, "organization", record ?? OrganizationA, new Dictionary<string, JsonElement> { [key] = Json(raw) }, includeSensitive));

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task A_value_round_trips_and_a_null_clears_it()
    {
        using var store = new Store();
        await store.DefineAsync("pms", "enum", ["dentrix", "eaglesoft"]);

        var set = await store.SetAsync("pms", "\"dentrix\"");
        Assert.True(set.IsSuccess, set.Error);
        var value = Assert.Single(set.Value!);
        Assert.Equal(("pms", "dentrix"), (value.Key, value.Value.GetString()));

        var read = await store.Repository.GetPropertyValuesAsync(TenantA, "organization", store.OrganizationA, false);
        Assert.Equal("dentrix", Assert.Single(read.Value!).Value.GetString());

        var cleared = await store.SetAsync("pms", "null");
        Assert.True(cleared.IsSuccess, cleared.Error);
        Assert.Empty(cleared.Value!);
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_is_refused_and_names_the_key()
    {
        using var store = new Store();
        await store.DefineAsync("seats", "number");

        var refused = await store.SetAsync("seats", "\"many\"");

        Assert.Equal(CrmFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.StartsWith("seats:", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_bad_value_means_none_of_the_values_are_written()
    {
        using var store = new Store();
        await store.DefineAsync("seats", "number");
        await store.DefineAsync("pms", "text");

        var refused = await store.Repository.SetPropertyValuesAsync(new(TenantA, "organization", store.OrganizationA,
            new Dictionary<string, JsonElement> { ["pms"] = Json("\"Dentrix\""), ["seats"] = Json("\"many\"") }, false));

        Assert.Equal(CrmFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Empty(store.Db.PropertyValues);
    }

    [Fact]
    public async Task An_unknown_key_is_not_found_and_so_is_another_tenants_definition()
    {
        using var store = new Store();
        await store.DefineAsync("theirs", "text", tenant: TenantB);

        Assert.Equal(CrmFailureCodes.NotFound, (await store.SetAsync("nothing", "\"x\"")).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.SetAsync("theirs", "\"x\"")).ErrorCode);
    }

    [Fact]
    public async Task Another_tenants_record_is_not_found_for_reads_and_writes()
    {
        using var store = new Store();
        await store.DefineAsync("pms", "text", objectType: "person");

        var read = await store.Repository.GetPropertyValuesAsync(TenantA, "person", store.PersonB, false);
        var write = await store.Repository.SetPropertyValuesAsync(new(TenantA, "person", store.PersonB,
            new Dictionary<string, JsonElement> { ["pms"] = Json("\"x\"") }, false));

        Assert.Equal(CrmFailureCodes.NotFound, read.ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, write.ErrorCode);
    }

    [Fact]
    public async Task A_record_of_the_wrong_kind_is_not_found()
    {
        using var store = new Store();
        await store.DefineAsync("pms", "text", objectType: "person");

        var read = await store.Repository.GetPropertyValuesAsync(TenantA, "person", store.OrganizationA, false);

        Assert.Equal(CrmFailureCodes.NotFound, read.ErrorCode);
    }

    [Fact]
    public async Task A_sensitive_property_is_hidden_and_unsettable_without_the_right_and_visible_with_it()
    {
        using var store = new Store();
        await store.DefineAsync("ssn_last4", "text", sensitive: true);

        Assert.Equal(CrmFailureCodes.NotFound, (await store.SetAsync("ssn_last4", "\"1234\"", includeSensitive: false)).ErrorCode);
        Assert.True((await store.SetAsync("ssn_last4", "\"1234\"", includeSensitive: true)).IsSuccess);

        var hidden = await store.Repository.GetPropertyValuesAsync(TenantA, "organization", store.OrganizationA, includeSensitive: false);
        var shown = await store.Repository.GetPropertyValuesAsync(TenantA, "organization", store.OrganizationA, includeSensitive: true);
        Assert.Empty(hidden.Value!);
        Assert.Equal("1234", Assert.Single(shown.Value!).Value.GetString());

        var defsHidden = await store.Repository.ListPropertyDefinitionsAsync(TenantA, null, false, includeSensitive: false);
        var defsShown = await store.Repository.ListPropertyDefinitionsAsync(TenantA, null, false, includeSensitive: true);
        Assert.Empty(defsHidden.Value!);
        Assert.Single(defsShown.Value!);
    }

    [Fact]
    public async Task A_retired_property_hides_its_values_refuses_new_ones_and_keeps_what_was_stored()
    {
        using var store = new Store();
        var definition = await store.DefineAsync("pms", "text");
        Assert.True((await store.SetAsync("pms", "\"Dentrix\"")).IsSuccess);

        var retired = await store.Repository.RetirePropertyDefinitionAsync(TenantA, definition.Id);
        Assert.NotNull(retired.Value!.RetiredAt);

        Assert.Empty((await store.Repository.GetPropertyValuesAsync(TenantA, "organization", store.OrganizationA, true)).Value!);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.SetAsync("pms", "\"x\"")).ErrorCode);
        Assert.Empty((await store.Repository.ListPropertyDefinitionsAsync(TenantA, null, includeRetired: false, includeSensitive: true)).Value!);
        Assert.Single((await store.Repository.ListPropertyDefinitionsAsync(TenantA, null, includeRetired: true, includeSensitive: true)).Value!);
        Assert.Equal("\"Dentrix\"", Assert.Single(store.Db.PropertyValues).Value);
    }

    [Fact]
    public async Task Retiring_is_repeatable_and_refused_for_another_tenants_property()
    {
        using var store = new Store();
        var definition = await store.DefineAsync("pms", "text");

        Assert.True((await store.Repository.RetirePropertyDefinitionAsync(TenantA, definition.Id)).IsSuccess);
        Assert.True((await store.Repository.RetirePropertyDefinitionAsync(TenantA, definition.Id)).IsSuccess);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.RetirePropertyDefinitionAsync(TenantB, definition.Id)).ErrorCode);
    }

    [Fact]
    public async Task Removing_an_option_that_stored_values_use_is_refused()
    {
        using var store = new Store();
        var definition = await store.DefineAsync("pms", "enum", ["dentrix", "eaglesoft"]);
        Assert.True((await store.SetAsync("pms", "\"eaglesoft\"")).IsSuccess);

        var refused = await store.Repository.UpdatePropertyDefinitionAsync(definition.Id, new(TenantA, "Practice software", ["dentrix"], false));
        var allowed = await store.Repository.UpdatePropertyDefinitionAsync(definition.Id, new(TenantA, "Practice software", ["dentrix", "eaglesoft", "open_dental"], false));

        Assert.Equal(CrmFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.True(allowed.IsSuccess, allowed.Error);
        Assert.Equal(["dentrix", "eaglesoft", "open_dental"], allowed.Value!.Options);
    }

    [Fact]
    public async Task A_definition_with_a_bad_key_or_options_is_refused_before_anything_is_stored()
    {
        using var store = new Store();

        var badKey = await store.Repository.CreatePropertyDefinitionAsync(new(TenantA, "organization", "Bad Key", "L", "text", null, false));
        var noOptions = await store.Repository.CreatePropertyDefinitionAsync(new(TenantA, "organization", "pms", "L", "enum", null, false));
        var unknownTenant = await store.Repository.CreatePropertyDefinitionAsync(new(Guid.NewGuid(), "organization", "pms", "L", "text", null, false));

        Assert.Equal(CrmFailureCodes.InvalidRequest, badKey.ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, noOptions.ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, unknownTenant.ErrorCode);
        Assert.Empty(store.Db.PropertyDefinitions);
    }

    [Fact]
    public async Task More_values_than_one_call_allows_and_none_at_all_are_refused()
    {
        using var store = new Store();
        var tooMany = Enumerable.Range(0, 101).ToDictionary(i => $"k{i}", _ => Json("1"));

        var many = await store.Repository.SetPropertyValuesAsync(new(TenantA, "organization", store.OrganizationA, tooMany, false));
        var none = await store.Repository.SetPropertyValuesAsync(new(TenantA, "organization", store.OrganizationA, new Dictionary<string, JsonElement>(), false));

        Assert.Equal(CrmFailureCodes.InvalidRequest, many.ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, none.ErrorCode);
    }
}
