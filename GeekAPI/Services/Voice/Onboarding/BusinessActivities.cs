using System.Text.Json;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>One IRS Principal Business Activity code: the six digits, its name, and the chart's sector and group it sits under.</summary>
public sealed record BusinessActivity(string Code, string Name, string Sector, string? Group);

/// <summary>
/// What a business does, as one of the IRS Principal Business Activity codes in the 2025 Schedule C chart (the embedded
/// schedule-c-2025.json, which names its source). A signup chooses a code, not words, and the gate decides on the code: the
/// earlier word match refused "AI for accounting firms" as accounting. The file is read once; a missing or empty file stops
/// the first signup with an exception rather than letting every code through.
/// </summary>
public static class BusinessActivities
{
    private const string ResourceName = "GeekAPI.Assets.BusinessActivities.schedule-c-2025.json";

    // HIPAA-covered providers: ambulatory care (621), hospitals (622), nursing and residential care (623). Veterinary services
    // (541940) stay excluded as they were under the word match ("Veterinary Clinic"). Social assistance (624) is not healthcare.
    private static readonly string[] HealthcarePrefixes = ["621", "622", "623", "541940"];

    // The industries waiting on an addendum: legal (5411), accounting, tax and payroll (5412), finance and insurance (52),
    // education (61).
    private static readonly string[] AddendumPrefixes = ["5411", "5412", "52", "61"];

    private sealed record Chart(string Source, List<BusinessActivity> Activities);

    private static readonly Chart Loaded = Load();
    private static readonly Dictionary<string, BusinessActivity> ByCode = Loaded.Activities.ToDictionary(a => a.Code, StringComparer.Ordinal);

    /// <summary>Where the codes come from, as the file states it.</summary>
    public static string Source => Loaded.Source;

    /// <summary>Every code, in the chart's order.</summary>
    public static IReadOnlyList<BusinessActivity> All => Loaded.Activities;

    /// <summary>The activity with this code, or null when the chart has no such code.</summary>
    public static BusinessActivity? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var activity) ? activity : null;

    public static bool IsHealthcare(BusinessActivity activity) => HealthcarePrefixes.Any(p => activity.Code.StartsWith(p, StringComparison.Ordinal));

    public static bool RequiresAddendum(BusinessActivity activity) => AddendumPrefixes.Any(p => activity.Code.StartsWith(p, StringComparison.Ordinal));

    private static Chart Load()
    {
        using var stream = typeof(BusinessActivities).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The business activity codes ({ResourceName}) are not in the assembly.");
        var chart = JsonSerializer.Deserialize<Chart>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The business activity codes could not be read.");
        if (string.IsNullOrWhiteSpace(chart.Source) || chart.Activities is not { Count: > 0 })
            throw new InvalidOperationException("The business activity codes file has no source or no codes.");
        foreach (var activity in chart.Activities)
        {
            if (activity.Code is not { Length: 6 } || !activity.Code.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(activity.Name))
                throw new InvalidOperationException($"The business activity code \"{activity.Code}\" is not six digits with a name.");
        }

        return chart;
    }
}
