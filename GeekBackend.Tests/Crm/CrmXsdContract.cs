using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using GeekApplication.Models.Crm;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// GeekApplication/Schemas/GeekCrm.xsd read as a model: its tables (with shipped or planned status),
/// columns, enumerations, unique rules and foreign keys. Shared, by file link, with
/// GeekBackend.IntegrationTests so the unit tests (against the script) and the clean-database tests
/// (against Postgres's catalog) judge the same contract. It must not reference GeekRepository,
/// which the integration project aliases.
/// </summary>
internal static class CrmXsdContract
{
    public static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    public static readonly XNamespace CrmNs = "https://geekatyourspot.com/schemas/geek-crm";

    public static readonly string[] Phases = ["shipped", "planned"];

    public static string XsdText()
    {
        using var stream = typeof(CrmSeed).Assembly.GetManifestResourceStream("GeekApplication.Schemas.GeekCrm.xsd")
            ?? throw new InvalidOperationException("GeekCrm.xsd is not an embedded resource of GeekApplication.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static XmlSchemaSet Compile(List<string> problems)
    {
        var set = new XmlSchemaSet();
        set.ValidationEventHandler += (_, e) => problems.Add($"{e.Severity}: {e.Message}");
        using var reader = XmlReader.Create(new StringReader(XsdText()));
        set.Add(null, reader);
        set.Compile();
        return set;
    }

    public static readonly XElement Schema = XDocument.Parse(XsdText()).Root!;

    public static string? PhaseOf(XElement node) =>
        node.Element(Xs + "annotation")?.Element(Xs + "appinfo")?.Element(CrmNs + "phase")?.Attribute("status")?.Value;

    public static string Local(string qualified) => qualified[(qualified.IndexOf(':') + 1)..];

    public static string[] Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Ordinal).ToArray();

    /// <summary>PascalCase element name to snake_case column name: an underscore before a capital that follows a lower-case letter or a digit.</summary>
    public static string Snake(string name)
    {
        var text = Local(name);
        var sb = new System.Text.StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1]))) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static readonly HashSet<string> ComplexTypeNames =
        Schema.Elements(Xs + "complexType").Select(t => t.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);

    public sealed record XsdColumn(string Column, string Type, bool Required, bool Shipped);

    public sealed record XsdTable(string TypeName, string Table, string? ParentKey, bool Shipped, IReadOnlyList<XsdColumn> Columns)
    {
        /// <summary>The columns the database must have now: shipped elements plus the parent key a nested type carries.</summary>
        public string[] ExpectedColumns() => Sorted(Columns.Where(c => c.Shipped).Select(c => c.Column)
            .Concat(ParentKey is null ? [] : [ParentKey]));
    }

    public static IReadOnlyList<XsdTable> Tables { get; } = Schema.Elements(Xs + "complexType").Select(type =>
    {
        var info = type.Element(Xs + "annotation")!.Element(Xs + "appinfo")!;
        var columns = type.Descendants(Xs + "element")
            .Where(e => e.Attribute("name") is not null && e.Attribute("type") is not null)
            .Where(e => !ComplexTypeNames.Contains(Local(e.Attribute("type")!.Value)))
            .Select(e => new XsdColumn(
                Snake(e.Attribute("name")!.Value),
                e.Attribute("type")!.Value,
                (string?)e.Attribute("minOccurs") != "0",
                PhaseOf(e) != "planned"))
            .ToList();
        return new XsdTable(
            type.Attribute("name")!.Value,
            info.Element(CrmNs + "table")!.Value,
            info.Element(CrmNs + "parentKey")?.Value,
            PhaseOf(type) == "shipped",
            columns);
    }).ToList();

    public static IReadOnlyList<XElement> EnumTypes { get; } = Schema.Elements(Xs + "simpleType")
        .Where(t => t.Descendants(Xs + "enumeration").Any()).ToList();

    public static string[] Enumeration(string typeName) => Sorted(EnumTypes.Single(t => t.Attribute("name")!.Value == typeName)
        .Descendants(Xs + "enumeration").Select(e => e.Attribute("value")!.Value));

    /// <summary>Each CHECK ... IN (...) constraint of the script and the XSD enumeration it must equal.</summary>
    public static readonly Dictionary<string, string> ConstraintToType = new(StringComparer.Ordinal)
    {
        ["ck_participants_kind"] = "ParticipantKind",
        ["ck_participants_lifecycle_stage"] = "LifecycleStage",
        ["ck_participants_source"] = "ParticipantSource",
        ["ck_persons_marital_status"] = "MaritalStatus",
        ["ck_persons_education_level"] = "EducationLevel",
        ["ck_organizations_type"] = "OrganizationType",
        ["ck_organizations_revenue_band"] = "RevenueBand",
        ["ck_contact_points_kind"] = "ContactPointKind",
        ["ck_contact_points_label"] = "ContactPointLabel",
        ["ck_participant_links_role"] = "ParticipantLinkRole",
        ["ck_stages_outcome"] = "StageOutcome",
        ["ck_deal_participants_role"] = "DealParticipantRole",
        ["ck_activities_channel"] = "ActivityChannel",
        ["ck_activities_direction"] = "ActivityDirection",
        ["ck_activities_outcome"] = "ActivityOutcome",
        ["ck_property_definitions_object"] = "PropertyObjectType",
        ["ck_property_definitions_type"] = "PropertyDataType",
        ["ck_products_kind"] = "ProductKind",
        ["ck_products_category"] = "ProductCategory",
        ["ck_products_billing"] = "Billing",
        ["ck_product_prices_size"] = "EstimateSize",
        ["ck_product_prices_currency"] = "Currency",
        ["ck_pricing_rules_currency"] = "Currency",
        ["ck_deal_line_items_currency"] = "Currency",
        ["ck_customer_holdings_status"] = "HoldingStatus",
        ["ck_customer_holdings_currency"] = "Currency",
        ["ck_customer_holdings_arrangement"] = "PartnerArrangement",
    };

    /// <summary>The SQL type an XSD element type maps to, as the script writes it with spaces removed.</summary>
    public static string SqlTypeOf(string xsdType)
    {
        var name = Local(xsdType);
        if (xsdType.StartsWith("xs:", StringComparison.Ordinal))
        {
            return name switch
            {
                "string" => "text",
                "boolean" => "boolean",
                "int" => "integer",
                "date" => "date",
                "dateTime" => "timestamptz",
                _ => throw new NotSupportedException($"No SQL type is agreed for {xsdType}."),
            };
        }
        if (EnumTypes.Any(t => t.Attribute("name")!.Value == name)) return "text";
        return name switch
        {
            "Uuid" => "uuid",
            "Money" => "numeric(12,2)",
            "Percent" => "numeric(5,2)",
            "Hours" => "numeric(7,2)",
            "Quantity" => "numeric(10,2)",
            "Json" => "jsonb",
            "NaicsCodeValue" or "SicCodeValue" => "text",
            _ => throw new NotSupportedException($"No SQL type is agreed for {xsdType}."),
        };
    }

    private static readonly Dictionary<string, string> TableOfElement = Schema.Descendants(Xs + "element")
        .Where(e => e.Attribute("name") is not null && e.Attribute("type") is not null && ComplexTypeNames.Contains(Local(e.Attribute("type")!.Value)))
        .GroupBy(e => e.Attribute("name")!.Value)
        .ToDictionary(g => g.Key, g => Tables.Single(t => t.TypeName == Local(g.First().Attribute("type")!.Value)).Table, StringComparer.Ordinal);

    private static string TableOfPath(string path) => TableOfElement[Local(path.Split('/').Last())];

    /// <summary>Each shipped unique rule as <c>table(col,col)</c>, columns sorted. A nested scope adds the parent key.</summary>
    public static string[] Uniques()
    {
        var found = new List<string>();
        foreach (var unique in Schema.Descendants(Xs + "unique"))
        {
            var table = Tables.Single(t => t.Table == TableOfPath(unique.Element(Xs + "selector")!.Attribute("xpath")!.Value));
            if (!table.Shipped) continue;
            var columns = unique.Elements(Xs + "field").Select(f => Snake(f.Attribute("xpath")!.Value)).ToList();
            var scope = (string?)unique.Parent!.Attribute("name");
            if (scope is not ("GeekCrmTenant" or "GeekCrmPlatform") && table.ParentKey is not null) columns.Add(table.ParentKey);
            found.Add($"{table.Table}({string.Join(",", Sorted(columns))})");
        }
        // A tenant-owned table's key is (TenantId, Id), which the database holds as UNIQUE (tenant_id, id).
        foreach (var key in Schema.Descendants(Xs + "key").Where(k => k.Elements(Xs + "field").Count() == 2))
        {
            var table = Tables.Single(t => t.Table == TableOfPath(key.Element(Xs + "selector")!.Attribute("xpath")!.Value));
            if (!table.Shipped) continue;
            found.Add($"{table.Table}({string.Join(",", Sorted(key.Elements(Xs + "field").Select(f => Snake(f.Attribute("xpath")!.Value))))})");
        }
        return Sorted(found);
    }

    /// <summary>
    /// Each shipped foreign key as <c>child(cols)->parent(cols)</c>, columns in declared order: a keyref, or a
    /// nesting. A nested table that has a tenant_id references its parent by (tenant_id, key) -> (tenant_id, id);
    /// one without (persons, organizations) by its parent key alone.
    /// </summary>
    public static string[] ForeignKeys()
    {
        var shipped = Tables.Where(t => t.Shipped).Select(t => t.Table).ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyref in Schema.Descendants(Xs + "keyref"))
        {
            var columns = string.Join(",", keyref.Elements(Xs + "field").Select(f => Snake(f.Attribute("xpath")!.Value)));
            var key = Schema.Descendants(Xs + "key").Single(k => k.Attribute("name")!.Value == Local(keyref.Attribute("refer")!.Value));
            var parent = TableOfPath(key.Element(Xs + "selector")!.Attribute("xpath")!.Value);
            var parentColumns = string.Join(",", key.Elements(Xs + "field").Select(f => Snake(f.Attribute("xpath")!.Value)));
            foreach (var path in keyref.Element(Xs + "selector")!.Attribute("xpath")!.Value.Split('|'))
            {
                var child = TableOfPath(path);
                if (shipped.Contains(child) && shipped.Contains(parent)) found.Add($"{child}({columns})->{parent}({parentColumns})");
            }
        }
        foreach (var table in Tables.Where(t => t.Shipped && t.ParentKey is not null))
        {
            var container = Schema.Elements(Xs + "complexType").Single(c => c.Descendants(Xs + "element").Any(e => e.Attribute("type")?.Value == "crm:" + table.TypeName));
            var parent = Tables.Single(t => t.TypeName == container.Attribute("name")!.Value).Table;
            found.Add(table.Columns.Any(c => c.Column == "tenant_id")
                ? $"{table.Table}(tenant_id,{table.ParentKey})->{parent}(tenant_id,id)"
                : $"{table.Table}({table.ParentKey})->{parent}(id)");
        }
        return Sorted(found);
    }
}
