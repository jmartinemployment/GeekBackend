using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using GeekApplication.Models.Crm;
using GeekRepository.Data;
using static GeekBackend.Tests.Crm.CrmXsdContract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// GeekApplication/Schemas/GeekCrm.xsd is Geek-CRM's canonical contract. It describes shipped and
/// planned parts; only shipped parts are authoritative. These tests make that real without a
/// database: the XSD compiles clean; every shipped enumeration equals its C# vocabulary and its SQL
/// CHECK list; every shipped table has exactly the columns, types and nullability the XSD names (and
/// no planned column or table has leaked into the script before its marker flipped); the EF mapping
/// agrees; a sample tenant document validates and four kinds of broken one are rejected; and no
/// outside standard's name appears in the contract, the migrations, the models or the CRM docs.
/// The same checks against a clean Postgres live in GeekBackend.IntegrationTests.
/// </summary>
public class SchemaContractTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // ----- the SQL script (0050), comments removed -----

    /// <summary>The schema scripts, in order, with comments removed: 0050 creates the tables and later scripts alter them. Data loads are not schema.</summary>
    private static readonly string[] SchemaScripts = 
        ["0050_geek_crm_schema.sql", "0051_geek_crm_import_batch_and_property_retire.sql", "0053_geek_crm_tenant_keys.sql", "0054_geek_crm_products.sql", "0055_geek_crm_pricing_and_subscriptions.sql"];

    private static string Sql => string.Join("\n", SchemaScripts.Select(name =>
        Regex.Replace(File.ReadAllText(Path.Combine(Root, "GeekRepository", "Migrations", "Sql", name)), @"--[^\n]*", string.Empty)));

    private sealed record SqlColumn(string Name, string Type, bool NotNull);

    private static Dictionary<string, List<SqlColumn>> SqlTables()
    {
        var tables = new Dictionary<string, List<SqlColumn>>(StringComparer.Ordinal);
        foreach (Match table in Regex.Matches(Sql, @"CREATE TABLE IF NOT EXISTS geek_crm\.(\w+)\s*\((.*?)\n\);", RegexOptions.Singleline))
        {
            tables[table.Groups[1].Value] = Regex.Matches(table.Groups[2].Value,
                    @"^ {4}([a-z_0-9]+)\s+(uuid|text|boolean|integer|timestamptz|date|jsonb|numeric(?:\(\d+,\s*\d+\))?)(.*)$", RegexOptions.Multiline)
                .Select(m => new SqlColumn(
                    m.Groups[1].Value,
                    Regex.Replace(m.Groups[2].Value, @"\s+", string.Empty),
                    m.Groups[3].Value.Contains("NOT NULL", StringComparison.Ordinal) || m.Groups[3].Value.Contains("PRIMARY KEY", StringComparison.Ordinal)))
                .ToList();
        }

        foreach (Match alter in Regex.Matches(Sql,
                     @"ALTER TABLE geek_crm\.(\w+)\s+ADD COLUMN IF NOT EXISTS ([a-z_0-9]+)\s+(uuid|text|boolean|integer|timestamptz|date|jsonb|numeric(?:\(\d+,\s*\d+\))?)([^;]*);"))
        {
            tables[alter.Groups[1].Value].Add(new SqlColumn(
                alter.Groups[2].Value,
                Regex.Replace(alter.Groups[3].Value, @"\s+", string.Empty),
                alter.Groups[4].Value.Contains("NOT NULL", StringComparison.Ordinal)));
        }
        return tables;
    }

    private static readonly Dictionary<string, IReadOnlyList<string>> Vocabularies = new(StringComparer.Ordinal)
    {
        ["ParticipantKind"] = ParticipantKinds.All,
        ["LifecycleStage"] = LifecycleStages.All,
        ["ParticipantSource"] = ParticipantSources.All,
        ["ContactPointKind"] = ContactPointKinds.All,
        ["ContactPointLabel"] = ContactPointLabels.All,
        ["ParticipantLinkRole"] = ParticipantLinkRoles.All,
        ["OrganizationType"] = OrganizationTypes.All,
        ["RevenueBand"] = RevenueBands.All,
        ["MaritalStatus"] = MaritalStatuses.All,
        ["EducationLevel"] = EducationLevels.All,
        ["StageOutcome"] = StageOutcomes.All,
        ["DealParticipantRole"] = DealParticipantRoles.All,
        ["ActivityChannel"] = ActivityChannels.All,
        ["ActivityDirection"] = ActivityDirections.All,
        ["ActivityOutcome"] = ActivityOutcomes.All,
        ["PropertyObjectType"] = PropertyObjectTypes.All,
        ["PropertyDataType"] = PropertyDataTypes.All,
        ["ProductKind"] = ProductKinds.All,
        ["ProductCategory"] = ProductCategories.All,
        ["Billing"] = BillingPeriods.All,
        ["HoldingStatus"] = HoldingStatuses.All,
        ["PartnerArrangement"] = PartnerArrangements.All,
        ["EstimateSize"] = EstimateSizes.All,
        ["Currency"] = Currencies.All,
    };

    // ----- tests: the schema itself -----

    [Fact]
    public void The_xsd_compiles_with_no_errors_or_warnings()
    {
        var problems = new List<string>();
        Compile(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void The_xsd_names_the_agreed_namespace_and_carries_a_version()
    {
        Assert.Equal("https://geekatyourspot.com/schemas/geek-crm", Schema.Attribute("targetNamespace")!.Value);
        Assert.Equal("1.0", Schema.Attribute("version")!.Value);
    }

    [Fact]
    public void Every_complex_type_says_which_table_and_whether_it_is_shipped_or_planned()
    {
        foreach (var type in Schema.Elements(Xs + "complexType"))
        {
            var name = type.Attribute("name")!.Value;
            Assert.True(type.Element(Xs + "annotation")?.Element(Xs + "appinfo")?.Element(CrmNs + "table") is not null, $"{name} has no crm:table.");
            Assert.Contains(PhaseOf(type), Phases);
        }
    }

    // ----- tests: shipped parts agree everywhere -----

    [Fact]
    public void Every_shipped_enumeration_equals_its_csharp_vocabulary_and_none_is_missing()
    {
        var shipped = EnumTypes.Where(t => PhaseOf(t) == "shipped").Select(t => t.Attribute("name")!.Value).ToArray();
        Assert.Equal(Sorted(shipped), Sorted(Vocabularies.Keys));
        foreach (var name in shipped)
            Assert.Equal(Sorted(Vocabularies[name]), Enumeration(name));
    }

    [Fact]
    public void Every_check_list_in_the_script_equals_its_xsd_enumeration_and_none_is_unmapped()
    {
        var checks = Regex.Matches(Sql, @"CONSTRAINT\s+(ck_\w+)\s+CHECK\s*\((?:\s*\w+\s+IS\s+NULL\s+OR)?\s*\w+\s+IN\s*\(([^)]*)\)");
        var seen = new List<string>();
        foreach (Match check in checks)
        {
            var constraint = check.Groups[1].Value;
            seen.Add(constraint);
            Assert.True(ConstraintToType.TryGetValue(constraint, out var type), $"{constraint} has no XSD enumeration in this test's map.");
            var values = Regex.Matches(check.Groups[2].Value, "'([^']+)'").Select(m => m.Groups[1].Value);
            Assert.Equal(Enumeration(type!), Sorted(values));
        }
        Assert.Equal(Sorted(ConstraintToType.Keys), Sorted(seen));
    }

    [Fact]
    public void Every_table_in_the_script_is_a_shipped_type_and_every_shipped_type_is_in_the_script()
    {
        var sql = SqlTables();
        Assert.Equal(Sorted(Tables.Where(t => t.Shipped).Select(t => t.Table)), Sorted(sql.Keys));
        foreach (var planned in Tables.Where(t => !t.Shipped))
            Assert.DoesNotContain(planned.Table, sql.Keys);
    }

    [Fact]
    public void Every_shipped_table_has_exactly_the_columns_types_and_nullability_the_xsd_names()
    {
        var problems = new List<string>();
        var sql = SqlTables();
        foreach (var table in Tables.Where(t => t.Shipped))
        {
            var actual = sql[table.Table];
            if (!table.ExpectedColumns().SequenceEqual(Sorted(actual.Select(c => c.Name))))
            {
                problems.Add($"{table.Table}: XSD [{string.Join(", ", table.ExpectedColumns())}] vs script [{string.Join(", ", Sorted(actual.Select(c => c.Name)))}]");
                continue;
            }
            foreach (var column in table.Columns.Where(c => c.Shipped))
            {
                var found = actual.Single(c => c.Name == column.Column);
                if (found.Type != SqlTypeOf(column.Type))
                    problems.Add($"{table.Table}.{column.Column}: XSD {column.Type} needs {SqlTypeOf(column.Type)}, script has {found.Type}");
                if (found.NotNull != column.Required)
                    problems.Add($"{table.Table}.{column.Column}: XSD {(column.Required ? "requires" : "allows no value for")} it, script says NOT NULL = {found.NotNull}");
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void A_planned_column_has_not_reached_the_script_before_its_marker_flips()
    {
        var sql = SqlTables();
        foreach (var table in Tables.Where(t => t.Shipped))
            foreach (var column in table.Columns.Where(c => !c.Shipped))
                Assert.DoesNotContain(sql[table.Table], c => c.Name == column.Column);
    }

    [Fact]
    public void The_ef_mapping_has_exactly_the_shipped_columns_of_each_table_it_maps()
    {
        using var db = new CrmDbContext(new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql("Host=localhost;Database=never-opened").Options);
        var mapped = new List<string>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            mapped.Add(table);
            var xsd = Tables.SingleOrDefault(t => t.Table == table);
            Assert.True(xsd is not null && xsd.Shipped, $"{table} is mapped by EF but is not a shipped type in the XSD.");
            var id = StoreObjectIdentifier.Table(table, CrmDbContext.Schema);
            Assert.Equal(xsd.ExpectedColumns(), Sorted(entity.GetProperties().Select(p => p.GetColumnName(id)!)));
        }

        // Not mapped yet (docs/GEEK-CRM-DATA-MODEL.md); when Phase 1 maps one, remove it from this list.
        string[] unmappedForNow = ["sic_codes"];
        Assert.Equal(Sorted(unmappedForNow), Sorted(Tables.Where(t => t.Shipped).Select(t => t.Table).Except(mapped)));
    }

    // ----- tests: uniqueness and foreign keys -----

    private static string[] SqlUniques()
    {
        var found = new List<string>();
        foreach (Match index in Regex.Matches(Sql, @"CREATE UNIQUE INDEX IF NOT EXISTS \w+\s+ON geek_crm\.(\w+)\s*\(([^)]*)\)"))
            found.Add($"{index.Groups[1].Value}({string.Join(",", Sorted(index.Groups[2].Value.Split(',').Select(c => c.Trim())))})");
        foreach (Match table in Regex.Matches(Sql, @"CREATE TABLE IF NOT EXISTS geek_crm\.(\w+)\s*\((.*?)\n\);", RegexOptions.Singleline))
        {
            foreach (Match unique in Regex.Matches(table.Groups[2].Value, @"CONSTRAINT\s+\w+\s+UNIQUE\s*\(([^)]*)\)"))
                found.Add($"{table.Groups[1].Value}({string.Join(",", Sorted(unique.Groups[1].Value.Split(',').Select(c => c.Trim())))})");
            foreach (Match column in Regex.Matches(table.Groups[2].Value, @"^ {4}([a-z_0-9]+)\s+\w+[^\n]*\bUNIQUE\b", RegexOptions.Multiline))
                found.Add($"{table.Groups[1].Value}({column.Groups[1].Value})");
        }
        return Sorted(found);
    }

    [Fact]
    public void Every_unique_rule_in_the_script_is_declared_in_the_xsd_and_the_reverse()
    {
        Assert.Equal(SqlUniques(), Uniques());
    }

    private static string[] SqlForeignKeys()
    {
        var found = new List<string>();
        foreach (Match table in Regex.Matches(Sql, @"CREATE TABLE IF NOT EXISTS geek_crm\.(\w+)\s*\((.*?)\n\);", RegexOptions.Singleline))
        {
            foreach (Match fk in Regex.Matches(table.Groups[2].Value, @"^ {4}([a-z_0-9]+)\s+uuid[^\n]*REFERENCES geek_crm\.(\w+)\s*\((\w+)\)", RegexOptions.Multiline))
            {
                // A later script drops a single-column key by Postgres's default name and adds a composite one.
                var defaultName = $"{table.Groups[1].Value}_{fk.Groups[1].Value}_fkey";
                if (Sql.Contains($"DROP CONSTRAINT IF EXISTS {defaultName};", StringComparison.Ordinal)) continue;
                found.Add($"{table.Groups[1].Value}({fk.Groups[1].Value})->{fk.Groups[2].Value}({fk.Groups[3].Value})");
            }
        }
        foreach (Match fk in Regex.Matches(Sql, @"ALTER TABLE geek_crm\.(\w+)\s+ADD CONSTRAINT \w+\s+FOREIGN KEY \(([^)]*)\)\s+REFERENCES geek_crm\.(\w+)\s*\(([^)]*)\)"))
            found.Add($"{fk.Groups[1].Value}({Regex.Replace(fk.Groups[2].Value, @"\s+", "")})->{fk.Groups[3].Value}({Regex.Replace(fk.Groups[4].Value, @"\s+", "")})");
        return Sorted(found);
    }

    [Fact]
    public void Every_foreign_key_in_the_script_is_a_reference_or_a_nesting_in_the_xsd_and_the_reverse()
    {
        Assert.Equal(SqlForeignKeys(), ForeignKeys());
    }

    // ----- tests: row-level security -----

    private static readonly string[] PublicLists = ["naics_codes", "sic_codes"];

    [Fact]
    public void Every_shipped_table_that_holds_tenant_data_has_forced_row_level_security_and_a_policy_and_the_public_lists_have_none()
    {
        var script = Sql;
        var tenantPolicy = "tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid";
        foreach (var table in Tables.Where(t => t.Shipped))
        {
            var rls = $"ALTER TABLE geek_crm.{table.Table} ENABLE ROW LEVEL SECURITY;";
            var forced = $"ALTER TABLE geek_crm.{table.Table} FORCE ROW LEVEL SECURITY;";
            var policy = $"CREATE POLICY tenant_isolation ON geek_crm.{table.Table}";
            if (table.Table == "tenant_subscriptions")
            {
                // Platform data: no tenant_id, so no tenant policy; only the platform flag opens it.
                Assert.Contains(rls, script, StringComparison.Ordinal);
                Assert.Contains(forced, script, StringComparison.Ordinal);
                var platformPolicy = script.IndexOf($"CREATE POLICY platform_access ON geek_crm.{table.Table}", StringComparison.Ordinal);
                Assert.True(platformPolicy >= 0, "tenant_subscriptions has no platform_access policy.");
                Assert.DoesNotContain(policy, script, StringComparison.Ordinal);
                var statement = script[platformPolicy..script.IndexOf(';', platformPolicy)];
                Assert.Contains("nullif(current_setting('app.platform', true), '') = 'on'", statement, StringComparison.Ordinal);
                continue;
            }
            if (PublicLists.Contains(table.Table))
            {
                Assert.DoesNotContain(rls, script, StringComparison.Ordinal);
                Assert.DoesNotContain(policy, script, StringComparison.Ordinal);
                continue;
            }
            Assert.Contains(rls, script, StringComparison.Ordinal);
            Assert.Contains(forced, script, StringComparison.Ordinal);
            Assert.Contains(policy, script, StringComparison.Ordinal);
            if (table.Columns.Any(c => c.Column == "tenant_id"))
            {
                var at = script.IndexOf(policy, StringComparison.Ordinal);
                var statement = script[at..script.IndexOf(';', at)];
                Assert.Contains($"USING ({tenantPolicy})", statement, StringComparison.Ordinal);
                Assert.Contains($"WITH CHECK ({tenantPolicy})", statement, StringComparison.Ordinal);
            }
        }
    }

    // ----- tests: documents -----

    private const string Tenant = "00000000-0000-4000-8000-000000000001";
    private const string OtherTenant = "00000000-0000-4000-8000-0000000000ff";

    private static string Id(int n) => $"00000000-0000-4000-8000-{n:D12}";

    /// <summary>A whole tenant: a person and the organization they own, a pipeline, a product, a deal with a line item, a note and a custom property.</summary>
    private static string SampleTenant(IReadOnlyDictionary<string, string>? override_ = null)
    {
        var v = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P1_ID"] = Id(10), ["P2_ID"] = Id(11), ["P2_TENANT"] = Tenant, ["P2_STAGE"] = "customer",
            ["DEAL_STAGE"] = Id(30), ["DEAL_PIPELINE"] = Id(20),
        };
        if (override_ is not null) foreach (var pair in override_) v[pair.Key] = pair.Value;

        return $$"""
        <GeekCrmTenant xmlns="https://geekatyourspot.com/schemas/geek-crm">
          <Tenant><Id>{{Tenant}}</Id><Name>Geek @ Your Spot</Name><Slug>geek-at-your-spot</Slug><CreatedAt>2026-11-11T12:00:00Z</CreatedAt></Tenant>
          <Participant>
            <Id>{{v["P1_ID"]}}</Id><TenantId>{{Tenant}}</TenantId><Kind>person</Kind><DisplayName>Maria Lopez</DisplayName>
            <LifecycleStage>lead</LifecycleStage><Source>phone_call</Source>
            <CreatedAt>2026-11-11T12:00:00Z</CreatedAt><UpdatedAt>2026-11-11T12:00:00Z</UpdatedAt>
            <Person><FirstName>Maria</FirstName><LastName>Lopez</LastName></Person>
          </Participant>
          <Participant>
            <Id>{{v["P2_ID"]}}</Id><TenantId>{{v["P2_TENANT"]}}</TenantId><Kind>organization</Kind><DisplayName>Boca Dental</DisplayName>
            <LifecycleStage>{{v["P2_STAGE"]}}</LifecycleStage><Source>import</Source>
            <CreatedAt>2026-11-11T12:00:00Z</CreatedAt><UpdatedAt>2026-11-11T12:00:00Z</UpdatedAt>
            <Organization><LegalName>Boca Dental LLC</LegalName><OrganizationType>llc</OrganizationType><NaicsCode>621210</NaicsCode></Organization>
          </Participant>
          <ContactPoint>
            <Id>{{Id(12)}}</Id><TenantId>{{Tenant}}</TenantId><ParticipantId>{{Id(10)}}</ParticipantId><Kind>phone</Kind><Label>mobile</Label>
            <Value>+15615550123</Value><IsPrimary>true</IsPrimary><DoNotContact>false</DoNotContact><SmsOptOut>false</SmsOptOut>
            <CreatedAt>2026-11-11T12:00:00Z</CreatedAt>
          </ContactPoint>
          <ParticipantLink>
            <Id>{{Id(13)}}</Id><TenantId>{{Tenant}}</TenantId><FromParticipantId>{{Id(10)}}</FromParticipantId><ToParticipantId>{{Id(11)}}</ToParticipantId>
            <Role>owner</Role><IsPrimary>true</IsPrimary><CreatedAt>2026-11-11T12:00:00Z</CreatedAt>
          </ParticipantLink>
          <Pipeline>
            <Id>{{Id(20)}}</Id><TenantId>{{Tenant}}</TenantId><Name>Sales</Name><IsDefault>true</IsDefault><CreatedAt>2026-11-11T12:00:00Z</CreatedAt>
            <Stage><Id>{{Id(30)}}</Id><TenantId>{{Tenant}}</TenantId><Name>New</Name><Position>1</Position><Outcome>open</Outcome><ProbabilityPct>10</ProbabilityPct></Stage>
            <Stage><Id>{{Id(31)}}</Id><TenantId>{{Tenant}}</TenantId><Name>Won</Name><Position>2</Position><Outcome>won</Outcome></Stage>
          </Pipeline>
          <Product>
            <Id>{{Id(40)}}</Id><TenantId>{{Tenant}}</TenantId><Name>Geek-Receptionist</Name><Code>geek-receptionist</Code><Kind>geek_saas</Kind>
            <Category>business_operations</Category><Billing>monthly</Billing><IsActive>true</IsActive>
            <CreatedAt>2026-11-11T12:00:00Z</CreatedAt><UpdatedAt>2026-11-11T12:00:00Z</UpdatedAt>
          </Product>
          <Deal>
            <Id>{{Id(50)}}</Id><TenantId>{{Tenant}}</TenantId><Name>Boca Dental, AI Receptionist</Name>
            <PipelineId>{{v["DEAL_PIPELINE"]}}</PipelineId><StageId>{{v["DEAL_STAGE"]}}</StageId><OrganizationParticipantId>{{Id(11)}}</OrganizationParticipantId>
            <ProductCategory>ai_receptionist</ProductCategory><EstimatedValue>1500.00</EstimatedValue>
            <CreatedAt>2026-11-11T12:00:00Z</CreatedAt><UpdatedAt>2026-11-11T12:00:00Z</UpdatedAt>
            <LineItem>
              <Id>{{Id(51)}}</Id><TenantId>{{Tenant}}</TenantId><ProductId>{{Id(40)}}</ProductId><Quantity>1</Quantity><UnitPrice>49.00</UnitPrice>
              <IsRecurring>true</IsRecurring><DiscountPct>0</DiscountPct><Currency>USD</Currency><CreatedAt>2026-11-11T12:00:00Z</CreatedAt>
            </LineItem>
            <StageChange><Id>{{Id(52)}}</Id><TenantId>{{Tenant}}</TenantId><ToStageId>{{Id(30)}}</ToStageId><ChangedAt>2026-11-11T12:00:00Z</ChangedAt><ChangedBy>receptionist</ChangedBy></StageChange>
            <DealParticipant><Id>{{Id(53)}}</Id><TenantId>{{Tenant}}</TenantId><ParticipantId>{{Id(10)}}</ParticipantId><Role>primary_contact</Role></DealParticipant>
          </Deal>
          <Activity>
            <Id>{{Id(60)}}</Id><TenantId>{{Tenant}}</TenantId><ParticipantId>{{Id(10)}}</ParticipantId><DealId>{{Id(50)}}</DealId>
            <Channel>call</Channel><Direction>inbound</Direction><OccurredAt>2026-11-11T12:00:00Z</OccurredAt><Outcome>missed</Outcome>
            <ExternalRef>CA0001</ExternalRef><CreatedBy>receptionist</CreatedBy><CreatedAt>2026-11-11T12:00:00Z</CreatedAt>
          </Activity>
          <PropertyDefinition>
            <Id>{{Id(70)}}</Id><TenantId>{{Tenant}}</TenantId><ObjectType>organization</ObjectType><Key>pms</Key><Label>Practice software</Label>
            <DataType>text</DataType><IsSensitive>false</IsSensitive><CreatedAt>2026-11-11T12:00:00Z</CreatedAt>
          </PropertyDefinition>
          <PropertyValue>
            <Id>{{Id(71)}}</Id><TenantId>{{Tenant}}</TenantId><DefinitionId>{{Id(70)}}</DefinitionId><RecordId>{{Id(11)}}</RecordId>
            <Value>"Dentrix"</Value><UpdatedAt>2026-11-11T12:00:00Z</UpdatedAt>
          </PropertyValue>
        </GeekCrmTenant>
        """;
    }

    private static List<string> Validate(string document)
    {
        var problems = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = Compile([]) };
        settings.ValidationEventHandler += (_, e) => problems.Add(e.Message);
        using var reader = XmlReader.Create(new StringReader(document), settings);
        while (reader.Read()) { }
        return problems;
    }

    [Fact]
    public void A_hand_written_sample_tenant_validates()
    {
        Assert.Empty(Validate(SampleTenant()));
    }

    [Fact]
    public void A_reference_to_a_row_that_is_not_in_the_document_is_rejected()
    {
        Assert.NotEmpty(Validate(SampleTenant(new Dictionary<string, string> { ["DEAL_STAGE"] = Id(999) })));
        Assert.NotEmpty(Validate(SampleTenant(new Dictionary<string, string> { ["DEAL_PIPELINE"] = Id(998) })));
    }

    [Fact]
    public void A_duplicate_id_is_rejected()
    {
        Assert.NotEmpty(Validate(SampleTenant(new Dictionary<string, string> { ["P2_ID"] = Id(10) })));
    }

    [Fact]
    public void A_row_carrying_another_tenants_id_is_rejected()
    {
        Assert.NotEmpty(Validate(SampleTenant(new Dictionary<string, string> { ["P2_TENANT"] = OtherTenant })));
    }

    [Fact]
    public void A_value_outside_an_enumeration_is_rejected()
    {
        Assert.NotEmpty(Validate(SampleTenant(new Dictionary<string, string> { ["P2_STAGE"] = "vip" })));
    }

    [Fact]
    public void A_subscription_must_belong_to_a_tenant_in_the_platform_document()
    {
        string Platform(string subscriber) => $$"""
            <GeekCrmPlatform xmlns="https://geekatyourspot.com/schemas/geek-crm">
              <Tenant><Id>{{Tenant}}</Id><Name>Geek @ Your Spot</Name><Slug>geek-at-your-spot</Slug><CreatedAt>2026-11-11T12:00:00Z</CreatedAt></Tenant>
              <TenantSubscription><Id>{{Id(80)}}</Id><SubscriberTenantId>{{subscriber}}</SubscriberTenantId><ProductCode>geek-receptionist</ProductCode><Status>active</Status><StartOn>2026-11-11</StartOn><CreatedAt>2026-11-11T12:00:00Z</CreatedAt></TenantSubscription>
              <NaicsCode><Code>621210</Code><Title>Offices of Dentists</Title><Level>6</Level></NaicsCode>
            </GeekCrmPlatform>
            """;

        Assert.Empty(Validate(Platform(Tenant)));
        Assert.NotEmpty(Validate(Platform(OtherTenant)));
    }

    // ----- tests: the outside standard's name is gone -----

    [Fact]
    public void No_outside_standard_is_named_in_the_contract_the_migrations_the_models_or_the_crm_docs()
    {
        var files = new List<string>
        {
            Path.Combine(Root, "GeekApplication", "Schemas", "GeekCrm.xsd"),
            Path.Combine(Root, "docs", "GEEK-CRM-DATA-MODEL.md"),
        };
        files.AddRange(Directory.GetFiles(Path.Combine(Root, "GeekRepository", "Migrations", "Sql"), "*crm*.sql"));
        files.AddRange(Directory.GetFiles(Path.Combine(Root, "GeekApplication", "Models", "Crm"), "*.cs"));

        var hits = files.Where(f => File.ReadAllText(f).Contains("acord", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(Root, f)).ToList();
        Assert.Empty(hits);
        Assert.DoesNotContain("acord", XsdText(), StringComparison.OrdinalIgnoreCase);
    }
}
