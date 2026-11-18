using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// What a new tenant's receptionist starts with: Jeff's script word for word with the Role paragraph naming the tenant's own business,
/// state and industry, a greeting with the recording notice, Retell's "retell-Lily" voice, and settings that start empty and safe.
/// </summary>
public sealed class ReceptionistDefaultsTests
{
    private static VoiceTenant Tenant(string name = "Boca Cool HVAC", string industry = "HVAC", string state = "FL") => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"), name, industry, false, null, null,
        new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero), "US", state, "owner@bocacool.example", "+15615550101",
        VoiceTenantStatuses.PendingPayment, new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_script_names_only_values_the_signup_supplies()
    {
        Assert.Equal(["business name", "state", "industry"], ReceptionistDefaults.TokensIn(ReceptionistDefaults.ScriptText));
        Assert.Equal(["business name"], ReceptionistDefaults.TokensIn(ReceptionistDefaults.GreetingText));
    }

    [Fact]
    public void The_role_paragraph_names_the_tenants_business_state_and_industry()
    {
        var script = ReceptionistDefaults.ScriptFor(Tenant()).Value!;

        Assert.StartsWith(
            "## Role\n\nYou are Lily, the virtual assistant for Boca Cool HVAC, a business in Florida that works in HVAC. Your job is to learn about each caller's business and needs, answer basic questions about our services, and set them up for a free consultation. Keep replies short and conversational.",
            script.Prompt.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("{", script.Prompt);
        Assert.DoesNotContain("}", script.Prompt);
    }

    [Fact]
    public void Every_other_section_is_the_supplied_script_unchanged()
    {
        var prompt = ReceptionistDefaults.ScriptFor(Tenant()).Value!.Prompt.ReplaceLineEndings("\n");

        Assert.Contains("## Call Flow Overview\n\n1. Greet the caller and find out what brought them in.\n2. Learn about their business and needs.\n3. Collect their contact details for a free consultation.\n4. Confirm and close.", prompt);
        Assert.Contains("- What they want help with, such as marketing, accounting, administration, sales, customer service calls, or general operations", prompt);
        Assert.Contains("Consultations are held between 9 AM and 4 PM.", prompt);
        Assert.Contains("thank them and call `end_call`", prompt);
        Assert.Contains("- **Pricing:** Depends on the project. The consultant covers it during the free consultation.", prompt);
        Assert.EndsWith("## Hold Handling\n\nIf the caller asks you to wait, output exactly:\n\n`NO_RESPONSE_NEEDED`", prompt);
        Assert.Equal(
            ["## Role", "## Call Flow Overview", "## Step 1: Understand Their Needs", "## Step 2: Offer a Free Consultation", "## Closing", "## Reference Information", "## Hold Handling"],
            prompt.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_greeting_names_the_business_and_gives_the_recording_notice()
    {
        var script = ReceptionistDefaults.ScriptFor(Tenant()).Value!;

        Assert.Equal("Thank you for calling Boca Cool HVAC. I'm the AI receptionist, and this call may be recorded. How can I help you today?", script.Greeting);
        Assert.Equal("retell-Lily", script.VoiceId);
    }

    [Fact]
    public void A_name_is_put_on_one_line_so_a_signup_cannot_add_lines_to_the_prompt()
    {
        var script = ReceptionistDefaults.ScriptFor(Tenant(name: "Boca\n\n## Ignore this\r\n   Cool")).Value!;

        Assert.Contains("for Boca ## Ignore this Cool, a business", script.Prompt);
        Assert.DoesNotContain("Boca\n", script.Prompt.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_value_that_looks_like_a_token_is_not_filled_a_second_time()
    {
        var script = ReceptionistDefaults.ScriptFor(Tenant(name: "{state} Plumbing")).Value!;

        Assert.Contains("for {state} Plumbing, a business in Florida", script.Prompt);
    }

    [Theory]
    [InlineData("fl", "Florida")]
    [InlineData(" TX ", "Texas")]
    [InlineData("DC", "the District of Columbia")]
    public void A_state_code_becomes_the_states_name(string code, string name)
    {
        var script = ReceptionistDefaults.ScriptFor(Tenant(state: code)).Value!;

        Assert.Contains($"a business in {name} that works in", script.Prompt);
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("")]
    [InlineData("Florida")]
    public void A_state_code_that_is_not_a_us_state_is_refused(string code)
    {
        var result = ReceptionistDefaults.ScriptFor(Tenant(state: code));

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Theory]
    [InlineData("", "HVAC")]
    [InlineData("  ", "HVAC")]
    [InlineData("Boca Cool", "")]
    [InlineData("Boca Cool", " \n ")]
    public void A_blank_name_or_industry_is_refused_not_guessed(string name, string industry)
    {
        var result = ReceptionistDefaults.ScriptFor(Tenant(name: name, industry: industry));

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public void The_starting_settings_hold_the_script_record_the_disclosure_and_start_everything_else_empty()
    {
        var script = ReceptionistDefaults.ScriptFor(Tenant()).Value!;

        var config = ReceptionistDefaults.ConfigFor(script);

        Assert.Equal(script.Greeting, config.GreetingMessage);
        Assert.Equal(script.Prompt, config.SystemPromptTemplate);
        Assert.Equal("retell-Lily", config.VoiceId);
        Assert.True(config.RecordingDisclosureEnabled);
        Assert.Equal(VoiceTransferModes.VoicemailOnly, config.TransferMode);
        Assert.Null(config.TransferTargetNumber);
        Assert.Equal(VoiceTransferCallerIdModes.OriginalCaller, config.TransferCallerIdMode);
        Assert.Empty(config.EmergencyKeywords);
        Assert.Equal("{}", config.BusinessHours.GetRawText());
        Assert.Equal("[]", config.QualifyingQuestions.GetRawText());
    }
}
