using System.Text.Json;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The hours a business gives at signup, as the receptionist is told them. They were stored, validated and read back for weeks while
/// no call could see them: a caller asking when the business opened got nothing the owner had entered.
/// </summary>
public sealed class BusinessHoursPromptTests
{
    private static VoiceReceptionistConfig Config(string hoursJson) =>
        ReceptionistDefaults.ConfigFor(ReceptionistDefaults.ScriptFor("Dag Plumbing", "FL", "plumbing").Value!)
            with { BusinessHours = JsonDocument.Parse(hoursJson).RootElement };

    private const string Week = """
        {
          "timezone": "America/New_York",
          "days": [
            { "day": "Monday", "open": "08:00", "close": "17:00" },
            { "day": "Saturday", "open": "09:00", "close": "13:00" },
            { "day": "Sunday", "closed": true }
          ],
          "notes": "Closed on public holidays."
        }
        """;

    [Fact]
    public void A_caller_asking_when_the_business_opens_can_now_be_answered()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config(Week));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("Monday: 08:00 to 17:00.", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Saturday: 09:00 to 13:00.", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Sunday: closed.", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("All times are America/New_York.", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Closed on public holidays.", prompt.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hours_are_given_as_the_only_ones_it_may_offer()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config(Week));

        Assert.Contains("the only hours you may give a caller", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Never guess", prompt.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_business_that_gave_no_hours_has_its_receptionist_say_so_rather_than_invent_them()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config("{}"));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("has not given its opening hours", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("offer to take a message", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Never guess", prompt.Value!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "days": "every day" }""")]
    [InlineData("""{ "days": [ { "open": "08:00", "close": "17:00" } ] }""")]
    [InlineData("""{ "days": [ { "day": "Monday", "open": "08:00" } ] }""")]
    [InlineData("""{ "days": [ { "day": "Monday", "close": "17:00" } ] }""")]
    [InlineData("""{ "days": [ "Monday 8 to 5" ] }""")]
    [InlineData("""{ "timezone": "America/New_York" }""")]
    public void Hours_in_a_shape_this_code_does_not_read_are_refused_not_quietly_dropped(string json)
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config(json));

        Assert.False(prompt.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, prompt.ErrorCode);
    }

    [Fact]
    public void A_day_that_is_closed_needs_no_times_and_a_day_that_is_open_does()
    {
        var closed = ReceptionistConsent.BusinessHoursText(
            JsonDocument.Parse("""{ "days": [ { "day": "Sunday", "closed": true } ] }""").RootElement);
        Assert.True(closed.IsSuccess, closed.Error);
        Assert.Contains("Sunday: closed.", closed.Value!, StringComparison.Ordinal);

        var openWithout = ReceptionistConsent.BusinessHoursText(
            JsonDocument.Parse("""{ "days": [ { "day": "Sunday", "closed": false } ] }""").RootElement);
        Assert.False(openWithout.IsSuccess);
    }

    [Fact]
    public void An_empty_list_of_days_is_a_business_with_no_hours_not_a_failure()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config("""{ "days": [] }"""));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("has not given its opening hours", prompt.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hours_reach_the_prompt_alongside_the_greeting_and_the_questions_not_instead_of_them()
    {
        var config = Config(Week);
        var prompt = ReceptionistConsent.SystemPrompt(config);

        Assert.Contains(config.GreetingMessage.Trim(), prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Monday: 08:00 to 17:00.", prompt.Value!, StringComparison.Ordinal);
    }
}

/// <summary>
/// The one rule that stands between a caller and an invented price. It is not conditional on the business having an indexed site,
/// because the receptionist may or may not be given a lookup tool on any given call — the toolbox decides that, after the prompt is
/// built — and a receptionist that behaved differently depending on which would be guessing exactly when it had least to go on.
/// </summary>
public sealed class NeverInventPromptTests
{
    private static VoiceReceptionistConfig Config(Guid? knowledgeRunId) =>
        ReceptionistDefaults.ConfigFor(ReceptionistDefaults.ScriptFor("Dag Plumbing", "FL", "plumbing").Value!)
            with { KnowledgeRunId = knowledgeRunId };

    [Fact]
    public void A_receptionist_is_told_to_take_a_message_rather_than_state_what_it_was_not_given()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config(null));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("Never state a fact about this business", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("take a message", prompt.Value!, StringComparison.Ordinal);
        Assert.Contains("Guessing is worse than taking a message.", prompt.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rule_is_the_same_whether_or_not_the_business_has_an_indexed_site()
    {
        var without = ReceptionistConsent.SystemPrompt(Config(null));
        var with = ReceptionistConsent.SystemPrompt(Config(Guid.NewGuid()));

        Assert.True(without.IsSuccess, without.Error);
        Assert.True(with.IsSuccess, with.Error);
        Assert.Equal(without.Value, with.Value);
    }

    [Fact]
    public void The_rule_names_a_price_so_the_commonest_invention_is_covered_explicitly()
    {
        var prompt = ReceptionistConsent.SystemPrompt(Config(Guid.NewGuid()));

        Assert.True(prompt.IsSuccess, prompt.Error);
        Assert.Contains("a price", prompt.Value!, StringComparison.Ordinal);
    }
}
