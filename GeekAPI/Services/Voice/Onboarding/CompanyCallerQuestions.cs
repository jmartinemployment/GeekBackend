using System.Text.Json;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>One area a caller can ask for help with, and the three questions the receptionist asks about it.</summary>
public sealed record CallerQuestionArea(string Name, IReadOnlyList<string> Questions);

/// <summary>
/// The caller questions Geek @ Your Spot LLC's own receptionist asks, as Jeff approved them on 2026-11-18, in his words (only the
/// capitalisation of "invoices" and "which" mid-sentence and a missing closing "?" were fixed). The receptionist asks the opening
/// question, then the three questions of the area the caller chose, then books the consultation. The answers go only into the
/// owner's calendar event; nothing here stores them. A client's qualifying questions start empty
/// (<see cref="ReceptionistDefaults.ConfigFor"/>) until it answers the signup form's question about them, and this set is what Geek @
/// Your Spot will answer there when it signs up like any other client. The only code that still reads it is the company line's
/// in-code config (ReceptionistConsent.CompanyConfig). The opening question also offers a phone receptionist; Jeff gave no questions
/// for it, so it has no area here.
/// </summary>
public static class CompanyCallerQuestions
{
    public const string OpeningQuestion =
        "Which area would you like help with: paying bills, getting paid, cash flow, taxes or payroll, advertising, marketing content, email marketing, finding leads, or a website chatbot or phone receptionist?";

    public static readonly IReadOnlyList<CallerQuestionArea> Areas =
    [
        new("Invoice approval",
        [
            "About how many vendor invoices do you get a month?",
            "Where do invoices enter your workflow: email, mail, vendor portal, a combination?",
            "Which accounting system do you use?",
        ]),
        new("Paying vendors",
        [
            "How do you pay vendors today: check, ACH, card or a mix?",
            "How many payments a month, roughly?",
            "Who approves a payment before it goes out?",
        ]),
        new("Getting paid (accounts receivable)",
        [
            "About how many invoices do you send a month?",
            "How do customers usually pay you?",
            "What is the hardest part of collecting?",
        ]),
        new("Cash flow planning",
        [
            "How do you track cash flow today?",
            "Do you use an accounting system, if so which accounting system do you use?",
            "How far ahead would you like to see?",
        ]),
        new("Cash flow spreadsheets",
        [
            "Who keeps the spreadsheet up to date, and how often?",
            "Which systems feed it: bank, accounting, billing or manual?",
            "Who reads the reports?",
        ]),
        new("Multi-state sales tax",
        [
            "Do you sell to customers outside Florida?",
            "Where do you sell: your own website, online marketplaces; which, in person?",
            "Are you registered to collect sales tax in any other state?",
        ]),
        new("Payroll tax",
        [
            "How many employees and how many contractors do you have?",
            "Does anyone work outside Florida, or Globally?",
            "Which payroll service do you use, if any?",
        ]),
        new("Tax law",
        [
            "Where do you sell, Globally, USA, Florida which counties or cities do you sell in?",
            "Do you sell anything that might be taxed differently, such as software or services?",
            "Who handles your sales tax filing today?",
        ]),
        new("Advertising and leads",
        [
            "What should an ad get someone to do: call, book or buy?",
            "Where do you advertise now: Google, Facebook or another?",
            "Can you tell which ads bring in customers?",
        ]),
        new("Content writing",
        [
            "What do you need written: blog posts, ads, social posts or marketing emails?",
            "Who writes it now?",
            "About how many pieces a month currently and goal?",
        ]),
        new("Content planning",
        [
            "Where should people find you: Google search, local, social media or all?",
            "Who decides what to write about?",
            "Are your competitors mostly local or national?",
        ]),
        new("Finding leads",
        [
            "Do most new clients find you, or do you prospect out to them?",
            "How do you find decision-makers' contact details now?",
            "Do you use a CRM? If true, which one do you use?",
        ]),
        new("Email campaigns",
        [
            "About how many contacts are on your list?",
            "What should the emails get people to do: call, book or buy?",
            "Which email tool do you use now?",
        ]),
        new("Website chatbot",
        [
            "How many inquiries, customer service requests, routine questions a week do you get?",
            "Would you want to be able to answer routine questions, how do you route callers, inquiries, customer service requests today?",
            "Do you use any software tools to track routine questions, inquires or customer service requests?",
        ]),
    ];

    /// <summary>
    /// The set as stored in qualifying_questions_json:
    /// <c>{"openingQuestion": "...", "areas": [{"name": "...", "questions": ["...", "...", "..."]}]}</c>.
    /// </summary>
    public static JsonElement ToJson() => JsonSerializer.SerializeToElement(new
    {
        openingQuestion = OpeningQuestion,
        areas = Areas.Select(a => new { name = a.Name, questions = a.Questions }).ToList(),
    });
}
