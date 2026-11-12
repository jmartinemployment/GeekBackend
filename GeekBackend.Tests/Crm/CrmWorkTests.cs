using System.Net;
using System.Reflection;
using System.Text;
using GeekAPI.Auth;
using GeekAPI.Controllers.Crm;
using GeekAPI.HttpClients;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekRepository.Data;
using GeekRepository.Data.Entities.Crm;
using GeekRepository.Repositories.Crm;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// The work a person does in Geek-CRM's screens (ICrmWorkStore): the store's rules in memory, the routes GeekAPI calls on
/// GeekRepository, and who GeekAPI says did it. The SQL itself (ILIKE search, the grouped totals, the unique keys) is proved only by
/// the integration tests against Postgres; the in-memory provider cannot run ILIKE, so text search is not tested here.
/// </summary>
public class CrmWorkTests
{
    private static readonly Guid TenantA = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly Guid TenantB = Guid.Parse("6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2aff");
    private static readonly Guid User = Guid.Parse("92b274f5-2fcb-4935-ba2d-cd8c03e1b21b");

    private sealed class NoTenantScope : ICrmTenantScope
    {
        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
        public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) => work();
    }

    private sealed class Store : IDisposable
    {
        public CrmDbContext Db { get; } = new(new DbContextOptionsBuilder<CrmDbContext>().UseInMemoryDatabase("crm-work-" + Guid.NewGuid()).Options);
        public CrmRepository Repository { get; }
        public Guid Pipeline { get; } = Guid.NewGuid();
        public Guid NewStage { get; } = Guid.NewGuid();
        public Guid QualifiedStage { get; } = Guid.NewGuid();
        public Guid WonStage { get; } = Guid.NewGuid();
        public Guid OtherTenantPerson { get; } = Guid.NewGuid();

        public Store()
        {
            Repository = new CrmRepository(Db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new NoTenantScope());
            Db.Tenants.AddRange(new CrmTenantRow { Id = TenantA, Name = "A", Slug = "a" }, new CrmTenantRow { Id = TenantB, Name = "B", Slug = "b" });
            Db.Participants.Add(new CrmParticipantRow { Id = OtherTenantPerson, TenantId = TenantB, Kind = "person", DisplayName = "Not Ours", Source = "import" });
            Db.Pipelines.Add(new CrmPipelineRow { Id = Pipeline, TenantId = TenantA, Name = "Sales", IsDefault = true });
            Db.Stages.AddRange(
                new CrmStageRow { Id = NewStage, TenantId = TenantA, PipelineId = Pipeline, Name = "New", Position = 1, Outcome = "open", ProbabilityPct = 10 },
                new CrmStageRow { Id = QualifiedStage, TenantId = TenantA, PipelineId = Pipeline, Name = "Qualified", Position = 2, Outcome = "open", ProbabilityPct = 25 },
                new CrmStageRow { Id = WonStage, TenantId = TenantA, PipelineId = Pipeline, Name = "Won", Position = 3, Outcome = "won", ProbabilityPct = 100 });
            Db.SaveChanges();
        }

        public async Task<CrmParticipant> CompanyAsync(string name = "Boca Dental", string? website = "https://bocadental.example")
        {
            var made = await Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Organization, null, null, null, name, website, null, null,
                LifecycleStages.Prospect, ParticipantSources.Referral, null, null, null));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public async Task<CrmParticipant> PersonAsync(string first = "Maria", string last = "Lopez", string? email = "maria@bocadental.example", Guid? company = null)
        {
            var made = await Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, first, last, "Owner", null, null, email, "(561) 555-0100",
                LifecycleStages.Lead, ParticipantSources.PhoneCall, "Called about missed calls.", company, null));
            Assert.True(made.IsSuccess, made.Error);
            return made.Value!;
        }

        public void Dispose() => Db.Dispose();
    }

    // ----- people and companies -----

    [Fact]
    public async Task A_person_is_created_with_a_normalized_phone_an_email_and_a_link_to_their_company()
    {
        using var store = new Store();
        var company = await store.CompanyAsync();
        var person = await store.PersonAsync(company: company.Id);

        Assert.Equal("Maria Lopez", person.DisplayName);
        Assert.Equal((LifecycleStages.Lead, ParticipantSources.PhoneCall, "Called about missed calls."), (person.LifecycleStage, person.Source, person.Notes));
        Assert.Equal("Owner", person.Person!.JobTitle);
        Assert.Contains(person.ContactPoints, c => c.Kind == ContactPointKinds.Phone && c.Value == "+15615550100" && c.IsPrimary);
        Assert.Contains(person.ContactPoints, c => c.Kind == ContactPointKinds.Email && c.Value == "maria@bocadental.example" && c.IsPrimary);
        var link = Assert.Single(person.Links);
        Assert.Equal((company.Id, ParticipantLinkRoles.Owner, "Boca Dental"), (link.ToParticipantId, link.Role, link.OtherDisplayName));
    }

    [Fact]
    public async Task A_company_is_created_with_its_website_and_appears_in_the_list()
    {
        using var store = new Store();
        var company = await store.CompanyAsync("Boca Dental");

        Assert.Equal("Boca Dental", company.Organization!.LegalName);
        Assert.Equal("https://bocadental.example", company.Organization.Website);
        var listed = (await store.Repository.SearchParticipantsAsync(TenantA, null, null, ParticipantKinds.Organization, 50)).Value!;
        Assert.Equal(company.Id, Assert.Single(listed).Id);
    }

    [Theory]
    [InlineData("person", null, null, "A person needs a first or last name.")]
    [InlineData("organization", null, null, "A company needs a name.")]
    [InlineData("gadget", "A", "B", "Unknown kind 'gadget'.")]
    public async Task A_record_that_is_not_enough_to_name_is_refused_in_plain_words(string kind, string? first, string? last, string message)
    {
        using var store = new Store();
        var made = await store.Repository.CreateParticipantAsync(new(TenantA, kind, first, last, null, null, null, null, null,
            LifecycleStages.Lead, ParticipantSources.Other, null, null, null));

        Assert.False(made.IsSuccess);
        Assert.Equal((CrmFailureCodes.InvalidRequest, message), (made.ErrorCode, made.Error));
    }

    [Theory]
    [InlineData("not-an-email", null, "That email address is not valid.")]
    [InlineData(null, "12345", "That phone number is not valid. Use ten digits, or start with + and the country code.")]
    public async Task An_email_or_phone_that_cannot_be_one_is_refused_not_dropped(string? email, string? phone, string message)
    {
        using var store = new Store();
        var made = await store.Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, "A", "B", null, null, null, email, phone,
            LifecycleStages.Lead, ParticipantSources.Other, null, null, null));

        Assert.Equal((CrmFailureCodes.InvalidRequest, message), (made.ErrorCode, made.Error));
        Assert.Empty(store.Db.Participants.Where(p => p.TenantId == TenantA));
    }

    [Fact]
    public async Task A_second_person_with_the_same_email_is_a_conflict_and_names_who_has_it()
    {
        using var store = new Store();
        await store.PersonAsync();

        var sameEmail = await store.Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, "Mia", "Lopez", null, null, null, "MARIA@bocadental.example", null,
            LifecycleStages.Lead, ParticipantSources.Other, null, null, null));
        Assert.Equal((CrmFailureCodes.Conflict, "Maria Lopez already has the email address maria@bocadental.example."), (sameEmail.ErrorCode, sameEmail.Error));
    }

    [Fact]
    public async Task A_person_cannot_be_linked_to_a_company_that_is_not_theirs_to_link()
    {
        using var store = new Store();
        var person = await store.PersonAsync();

        var missing = await store.Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, "A", "B", null, null, null, null, null,
            LifecycleStages.Lead, ParticipantSources.Other, null, Guid.NewGuid(), null));
        var anotherPerson = await store.Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, "A", "B", null, null, null, null, null,
            LifecycleStages.Lead, ParticipantSources.Other, null, person.Id, null));
        var otherTenant = await store.Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, "A", "B", null, null, null, null, null,
            LifecycleStages.Lead, ParticipantSources.Other, null, store.OtherTenantPerson, null));
        var roleWithoutCompany = await store.Repository.CreateParticipantAsync(new(TenantA, ParticipantKinds.Person, "A", "B", null, null, null, null, null,
            LifecycleStages.Lead, ParticipantSources.Other, null, null, ParticipantLinkRoles.Owner));

        Assert.All(new[] { missing, anotherPerson, otherTenant }, r => Assert.Equal(CrmFailureCodes.NotFound, r.ErrorCode));
        Assert.Equal(CrmFailureCodes.InvalidRequest, roleWithoutCompany.ErrorCode);
    }

    [Fact]
    public async Task An_edit_replaces_the_fields_and_a_blank_email_or_phone_leaves_the_contact_points_alone()
    {
        using var store = new Store();
        var person = await store.PersonAsync();

        var edited = await store.Repository.UpdateParticipantAsync(person.Id, new(TenantA, "Maria", "Lopez-Diaz", "Practice owner", null, null,
            LifecycleStages.Customer, "Signed up.", null, null));
        Assert.True(edited.IsSuccess, edited.Error);
        Assert.Equal(("Maria Lopez-Diaz", LifecycleStages.Customer, "Signed up."), (edited.Value!.DisplayName, edited.Value.LifecycleStage, edited.Value.Notes));
        Assert.Equal("Practice owner", edited.Value.Person!.JobTitle);
        Assert.Equal(2, edited.Value.ContactPoints.Count);

        var changed = await store.Repository.UpdateParticipantAsync(person.Id, new(TenantA, "Maria", "Lopez-Diaz", null, null, null,
            LifecycleStages.Customer, null, "maria@newaddress.example", "+1 561 555 0199"));
        Assert.Contains(changed.Value!.ContactPoints, c => c.Kind == ContactPointKinds.Email && c.Value == "maria@newaddress.example");
        Assert.Contains(changed.Value.ContactPoints, c => c.Kind == ContactPointKinds.Phone && c.Value == "+15615550199");
        Assert.Equal(2, changed.Value.ContactPoints.Count);
        Assert.Null(changed.Value.Notes);
    }

    [Fact]
    public async Task An_edit_cannot_take_another_persons_email_or_leave_a_record_nameless_or_touch_another_tenant()
    {
        using var store = new Store();
        var maria = await store.PersonAsync();
        var other = await store.PersonAsync("Ben", "Ortiz", "ben@bocadental.example");

        var taken = await store.Repository.UpdateParticipantAsync(other.Id, new(TenantA, "Ben", "Ortiz", null, null, null, LifecycleStages.Lead, null, "maria@bocadental.example", null));
        Assert.Equal(CrmFailureCodes.Conflict, taken.ErrorCode);

        var nameless = await store.Repository.UpdateParticipantAsync(maria.Id, new(TenantA, " ", null, null, null, null, LifecycleStages.Lead, null, null, null));
        Assert.Equal((CrmFailureCodes.InvalidRequest, "A person needs a first or last name."), (nameless.ErrorCode, nameless.Error));

        var foreign = await store.Repository.UpdateParticipantAsync(store.OtherTenantPerson, new(TenantA, "X", "Y", null, null, null, LifecycleStages.Lead, null, null, null));
        Assert.Equal(CrmFailureCodes.NotFound, foreign.ErrorCode);
    }

    [Fact]
    public async Task Archiving_takes_a_record_out_of_the_list_and_keeps_it_and_a_repeat_changes_nothing()
    {
        using var store = new Store();
        var person = await store.PersonAsync();

        Assert.True((await store.Repository.ArchiveParticipantAsync(TenantA, person.Id)).Value);
        var archivedAt = store.Db.Participants.Single(p => p.Id == person.Id).ArchivedAt;
        Assert.NotNull(archivedAt);
        Assert.True((await store.Repository.ArchiveParticipantAsync(TenantA, person.Id)).Value);
        Assert.Equal(archivedAt, store.Db.Participants.Single(p => p.Id == person.Id).ArchivedAt);

        Assert.Empty((await store.Repository.SearchParticipantsAsync(TenantA, null, null, null, 50)).Value!);
        Assert.True((await store.Repository.GetParticipantAsync(TenantA, person.Id)).IsSuccess);
        var edit = await store.Repository.UpdateParticipantAsync(person.Id, new(TenantA, "A", "B", null, null, null, LifecycleStages.Lead, null, null, null));
        Assert.Equal(CrmFailureCodes.InvalidRequest, edit.ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.ArchiveParticipantAsync(TenantA, store.OtherTenantPerson)).ErrorCode);
    }

    [Fact]
    public async Task A_person_or_company_is_deleted_only_when_nothing_depends_on_it_and_the_refusal_says_what_does()
    {
        using var store = new Store();
        var company = await store.CompanyAsync();
        var person = await store.PersonAsync(company: company.Id);
        var other = await store.PersonAsync("Sam", "Free", "sam@example.test");
        var deal = (await store.Repository.CreateDealAsync(new(TenantA, "Receptionist", "x", company.Id, person.Id, null, null, null, null))).Value!;

        var company_on_deal = await store.Repository.DeleteParticipantAsync(TenantA, company.Id);
        Assert.Equal(CrmFailureCodes.Conflict, company_on_deal.ErrorCode);
        Assert.Contains("1 deal", company_on_deal.Error);
        Assert.Contains("archive", company_on_deal.Error);
        var contact_on_deal = await store.Repository.DeleteParticipantAsync(TenantA, person.Id);
        Assert.Equal(CrmFailureCodes.Conflict, contact_on_deal.ErrorCode);
        Assert.True(store.Db.Participants.Any(p => p.Id == company.Id) && store.Db.Participants.Any(p => p.Id == person.Id));

        store.Db.CustomerHoldings.Add(new CrmCustomerHoldingRow { Id = Guid.NewGuid(), TenantId = TenantA, ParticipantId = other.Id, ProductId = Guid.NewGuid(), Status = "active" });
        store.Db.Projects.Add(new CrmProjectRow { Id = Guid.NewGuid(), TenantId = TenantA, ParticipantId = other.Id, Status = "planned" });
        store.Db.SaveChanges();
        var held = await store.Repository.DeleteParticipantAsync(TenantA, other.Id);
        Assert.Equal(CrmFailureCodes.Conflict, held.ErrorCode);
        Assert.Contains("1 customer holding", held.Error);
        Assert.Contains("1 project", held.Error);

        var deleted = await store.Repository.DeleteDealAsync(TenantA, deal.Id);
        Assert.True(deleted.IsSuccess, deleted.Error);
        Assert.True(deleted.Value);
        Assert.True((await store.Repository.DeleteParticipantAsync(TenantA, person.Id)).Value);
        Assert.True((await store.Repository.DeleteParticipantAsync(TenantA, company.Id)).Value);
        Assert.False(store.Db.Participants.Any(p => p.Id == person.Id || p.Id == company.Id));
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.DeleteParticipantAsync(TenantA, person.Id)).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.DeleteParticipantAsync(TenantA, store.OtherTenantPerson)).ErrorCode);
        Assert.True(store.Db.Participants.Any(p => p.Id == store.OtherTenantPerson));
    }

    [Fact]
    public async Task A_deal_is_deleted_and_the_people_on_it_and_another_tenants_deals_are_left_alone()
    {
        using var store = new Store();
        var person = await store.PersonAsync();
        var deal = (await store.Repository.CreateDealAsync(new(TenantA, "Boca Dental, receptionist", "x", null, person.Id, null, null, null, null))).Value!;

        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.DeleteDealAsync(TenantB, deal.Id)).ErrorCode);
        Assert.True(store.Db.Deals.Any(d => d.Id == deal.Id));

        var deleted = await store.Repository.DeleteDealAsync(TenantA, deal.Id);
        Assert.True(deleted.IsSuccess, deleted.Error);
        Assert.True(deleted.Value);
        Assert.False(store.Db.Deals.Any(d => d.Id == deal.Id));
        Assert.True(store.Db.Participants.Any(p => p.Id == person.Id));
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.DeleteDealAsync(TenantA, deal.Id)).ErrorCode);
    }

    [Fact]
    public async Task An_activity_is_deleted_and_the_person_it_was_about_is_left_alone()
    {
        using var store = new Store();
        var person = await store.PersonAsync();
        var note = (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note, "Remove me"))).Value!;
        var kept = (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note, "Keep me"))).Value!;

        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.DeleteActivityAsync(TenantB, note.Id)).ErrorCode);
        Assert.True((await store.Repository.DeleteActivityAsync(TenantA, note.Id)).Value);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.DeleteActivityAsync(TenantA, note.Id)).ErrorCode);
        Assert.Equal(kept.Id, Assert.Single((await store.Repository.ListActivityViewsAsync(TenantA, person.Id, null, null, false, 50)).Value!).Activity.Id);
        Assert.True(store.Db.Participants.Any(p => p.Id == person.Id));
    }

    // ----- deals -----

    [Fact]
    public async Task A_deal_is_listed_with_its_company_and_primary_contact_named_and_the_contact_can_be_changed()
    {
        using var store = new Store();
        var company = await store.CompanyAsync();
        var maria = await store.PersonAsync(company: company.Id);
        var ben = await store.PersonAsync("Ben", "Ortiz", "ben@bocadental.example", company.Id);
        var made = await store.Repository.CreateDealAsync(new(TenantA, "Boca Dental, AI Receptionist", "ai_receptionist", company.Id, maria.Id, null, 1500m, null, null));
        Assert.True(made.IsSuccess, made.Error);

        var view = (await store.Repository.GetDealViewAsync(TenantA, made.Value!.Id)).Value!;
        Assert.Equal(("Boca Dental", maria.Id, "Maria Lopez", "New"), (view.OrganizationName, view.PrimaryContactId, view.PrimaryContactName, view.Deal.StageName));

        var changed = await store.Repository.UpdateDealAsync(made.Value.Id, new(TenantA, "Boca Dental, receptionist", "ai_receptionist", 1800m,
            new DateOnly(2026, 12, 1), company.Id, ben.Id));
        Assert.True(changed.IsSuccess, changed.Error);
        Assert.Equal(("Boca Dental, receptionist", 1800m, new DateOnly(2026, 12, 1), "Ben Ortiz"),
            (changed.Value!.Deal.Name, changed.Value.Deal.EstimatedValue, changed.Value.Deal.ExpectedCloseOn, changed.Value.PrimaryContactName));
        Assert.Single(store.Db.DealParticipants.Where(dp => dp.DealId == made.Value.Id && dp.Role == DealParticipantRoles.PrimaryContact));

        var listed = (await store.Repository.ListDealViewsAsync(TenantA, true, null, 50)).Value!;
        Assert.Equal("Ben Ortiz", Assert.Single(listed).PrimaryContactName);
    }

    [Fact]
    public async Task The_value_of_a_deal_with_line_items_is_their_sum_and_cannot_be_typed_over()
    {
        using var store = new Store();
        var made = await store.Repository.CreateDealAsync(new(TenantA, "Deal", "ai_receptionist", null, null, null, 100m, null, null));
        store.Db.DealLineItems.Add(new CrmDealLineItemRow { Id = Guid.NewGuid(), TenantId = TenantA, DealId = made.Value!.Id, ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100m });
        await store.Db.SaveChangesAsync();

        var typedOver = await store.Repository.UpdateDealAsync(made.Value.Id, new(TenantA, "Deal", "ai_receptionist", 999m, null, null, null));
        Assert.Equal((CrmFailureCodes.InvalidRequest, "This deal's value is the sum of its line items. Change the line items instead."), (typedOver.ErrorCode, typedOver.Error));

        var unchanged = await store.Repository.UpdateDealAsync(made.Value.Id, new(TenantA, "Renamed", "ai_receptionist", 100m, null, null, null));
        Assert.True(unchanged.IsSuccess, unchanged.Error);
        Assert.Equal("Renamed", unchanged.Value!.Deal.Name);
    }

    [Fact]
    public async Task A_deal_edit_refuses_a_missing_name_a_negative_value_and_a_company_or_person_that_is_not_one()
    {
        using var store = new Store();
        var person = await store.PersonAsync();
        var made = await store.Repository.CreateDealAsync(new(TenantA, "Deal", "ai_receptionist", null, null, null, null, null, null));
        var id = made.Value!.Id;

        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.UpdateDealAsync(id, new(TenantA, " ", "x", null, null, null, null))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.UpdateDealAsync(id, new(TenantA, "D", " ", null, null, null, null))).ErrorCode);
        Assert.Equal(CrmFailureCodes.InvalidRequest, (await store.Repository.UpdateDealAsync(id, new(TenantA, "D", "x", -1m, null, null, null))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.UpdateDealAsync(id, new(TenantA, "D", "x", null, null, person.Id, null))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.UpdateDealAsync(id, new(TenantA, "D", "x", null, null, null, store.OtherTenantPerson))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.UpdateDealAsync(Guid.NewGuid(), new(TenantA, "D", "x", null, null, null, null))).ErrorCode);
    }

    [Fact]
    public async Task The_stages_come_back_in_order_and_the_dashboard_counts_deals_by_stage()
    {
        using var store = new Store();
        var contact = await store.PersonAsync();
        await store.Repository.CreateDealAsync(new(TenantA, "One", "x", null, null, null, 100m, 10m, "New"));
        await store.Repository.CreateDealAsync(new(TenantA, "Two", "x", null, null, null, 200m, null, "New"));
        await store.Repository.CreateDealAsync(new(TenantA, "Three", "x", null, contact.Id, null, 300m, null, "Qualified"));
        await store.CompanyAsync();

        var stages = (await store.Repository.ListStagesAsync(TenantA)).Value!;
        Assert.Equal(["New", "Qualified", "Won"], stages.Select(s => s.Name));
        Assert.Empty((await store.Repository.ListStagesAsync(TenantB)).Value!);

        var dashboard = (await store.Repository.GetDashboardAsync(TenantA)).Value!;
        Assert.Equal((1, 1), (dashboard.People, dashboard.Organizations));
        var totals = dashboard.Stages.ToDictionary(s => s.Name);
        Assert.Equal((2, 300m, 10m), (totals["New"].DealCount, totals["New"].EstimatedValue, totals["New"].RecurringValueMonthly));
        Assert.Equal((1, 300m), (totals["Qualified"].DealCount, totals["Qualified"].EstimatedValue));
        Assert.Equal((0, 0m), (totals["Won"].DealCount, totals["Won"].EstimatedValue));
    }

    // ----- notes and tasks -----

    private static CreateActivityCommand Activity(Guid? participant, string channel, string? subject = "Spoke with her", DateTime? due = null, Guid? deal = null, string? outcome = null) =>
        new(TenantA, participant, deal, channel, null, DateTime.UtcNow, subject, null, outcome, due, "user");

    [Fact]
    public async Task A_note_is_saved_on_the_person_and_shows_in_their_history_and_moves_them_up_the_list()
    {
        using var store = new Store();
        var person = await store.PersonAsync();
        var other = await store.PersonAsync("Ben", "Ortiz", "ben@bocadental.example");
        store.Db.Participants.Single(p => p.Id == other.Id).UpdatedAt = DateTime.UtcNow.AddHours(-1);
        await store.Db.SaveChangesAsync();

        var made = await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note, "Wants a demo"));
        Assert.True(made.IsSuccess, made.Error);
        Assert.Equal(("note", "Wants a demo", "user"), (made.Value!.Channel, made.Value.Subject, made.Value.CreatedBy));

        var history = (await store.Repository.ListActivityViewsAsync(TenantA, person.Id, null, null, false, 50)).Value!;
        var row = Assert.Single(history);
        Assert.Equal(("Maria Lopez", null), (row.ParticipantName, row.DealName));
        Assert.Equal(person.Id, (await store.Repository.SearchParticipantsAsync(TenantA, null, null, null, 50)).Value![0].Id);
    }

    [Fact]
    public async Task A_task_is_listed_soonest_due_first_with_undated_ones_last_and_completes_once()
    {
        using var store = new Store();
        var person = await store.PersonAsync();
        var later = (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Task, "Later", DateTime.UtcNow.AddDays(3)))).Value!;
        var undated = (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Task, "Whenever"))).Value!;
        var soon = (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Task, "Soon", DateTime.UtcNow.AddHours(-2)))).Value!;

        var open = (await store.Repository.ListActivityViewsAsync(TenantA, null, null, null, true, 50)).Value!;
        Assert.Equal(["Soon", "Later", "Whenever"], open.Select(v => v.Activity.Subject));
        var dashboard = (await store.Repository.GetDashboardAsync(TenantA)).Value!;
        Assert.Equal((3, 1), (dashboard.OpenTasks, dashboard.OverdueTasks));

        var done = await store.Repository.CompleteTaskAsync(TenantA, soon.Id);
        Assert.Equal((ActivityOutcomes.Completed, true), (done.Value!.Outcome, done.Value.CompletedAt is not null));
        var firstCompletion = done.Value.CompletedAt;
        Assert.Equal(firstCompletion, (await store.Repository.CompleteTaskAsync(TenantA, soon.Id)).Value!.CompletedAt);
        Assert.Equal(["Later", "Whenever"], (await store.Repository.ListActivityViewsAsync(TenantA, null, null, null, true, 50)).Value!.Select(v => v.Activity.Subject));
        Assert.Equal(later.Id, (await store.Repository.ListActivityViewsAsync(TenantA, null, null, null, true, 50)).Value![0].Activity.Id);
        Assert.NotEqual(Guid.Empty, undated.Id);
    }

    [Fact]
    public async Task An_activity_that_does_not_make_sense_is_refused_and_says_why()
    {
        using var store = new Store();
        var person = await store.PersonAsync();

        Assert.Equal("An activity belongs to a person, a deal, or both.", (await store.Repository.CreateActivityAsync(Activity(null, ActivityChannels.Note))).Error);
        Assert.Equal("Say what happened.", (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note, " "))).Error);
        Assert.Equal("A task needs a title.", (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Task, null))).Error);
        Assert.Equal("Only a task has a due date.", (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Call, due: DateTime.UtcNow))).Error);
        Assert.Equal("A task has no outcome until it is completed.", (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Task, outcome: ActivityOutcomes.Completed))).Error);
        Assert.Equal("Unknown channel 'fax'.", (await store.Repository.CreateActivityAsync(Activity(person.Id, "fax"))).Error);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.CreateActivityAsync(Activity(store.OtherTenantPerson, ActivityChannels.Note))).ErrorCode);
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note, deal: Guid.NewGuid()))).ErrorCode);

        var note = (await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note))).Value!;
        Assert.Equal((CrmFailureCodes.InvalidRequest, "Only a task can be completed."), ((await store.Repository.CompleteTaskAsync(TenantA, note.Id)).ErrorCode, (await store.Repository.CompleteTaskAsync(TenantA, note.Id)).Error));
        Assert.Equal(CrmFailureCodes.NotFound, (await store.Repository.CompleteTaskAsync(TenantB, note.Id)).ErrorCode);
    }

    [Fact]
    public async Task A_deals_history_names_the_deal_and_the_person()
    {
        using var store = new Store();
        var person = await store.PersonAsync();
        var deal = (await store.Repository.CreateDealAsync(new(TenantA, "Boca Dental, receptionist", "x", null, person.Id, null, null, null, null))).Value!;
        await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Call, "Intro call", deal: deal.Id));
        await store.Repository.CreateActivityAsync(Activity(person.Id, ActivityChannels.Note, "Unrelated"));

        var forDeal = (await store.Repository.ListActivityViewsAsync(TenantA, null, deal.Id, null, false, 50)).Value!;
        var row = Assert.Single(forDeal);
        Assert.Equal(("Maria Lopez", "Boca Dental, receptionist", "Intro call"), (row.ParticipantName, row.DealName, row.Activity.Subject));
        Assert.Equal(2, (await store.Repository.ListActivityViewsAsync(TenantA, person.Id, null, null, false, 50)).Value!.Count);
        Assert.Single((await store.Repository.ListActivityViewsAsync(TenantA, null, null, ActivityChannels.Call, false, 50)).Value!);
    }

    // ----- the routes GeekAPI calls on GeekRepository -----

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", Encoding.UTF8, "application/json") };
        }
    }

    private static string Strip(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+):[^}]+\}", "{$1}");

    private static bool Matches(string template, string path)
    {
        var t = template.Split('/');
        var p = path.Split('/');
        return t.Length == p.Length && t.Zip(p).All(pair => pair.First.StartsWith('{') || pair.First == pair.Second);
    }

    [Fact]
    public async Task Every_work_call_goes_to_a_route_and_verb_geekrepository_declares()
    {
        var recorder = new Recorder();
        var client = new HttpCrmRepository(new HttpClient(recorder) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpCrmRepository>.Instance);
        var id = Guid.NewGuid();

        await client.ListStagesAsync(TenantA);
        await client.GetDashboardAsync(TenantA);
        await client.SearchParticipantsAsync(TenantA, "maria & co", "lead", "person", 20);
        await client.CreateParticipantAsync(new(TenantA, "person", "A", "B", null, null, null, null, null, "lead", "other", null, null, null));
        await client.UpdateParticipantAsync(id, new(TenantA, "A", "B", null, null, null, "lead", null, null, null));
        await client.ArchiveParticipantAsync(TenantA, id);
        await client.DeleteParticipantAsync(TenantA, id);
        await client.DeleteDealAsync(TenantA, id);
        await client.DeleteActivityAsync(TenantA, id);
        await client.ListDealViewsAsync(TenantA, true, "dental", 20);
        await client.GetDealViewAsync(TenantA, id);
        await client.UpdateDealAsync(id, new(TenantA, "D", "x", null, null, null, null));
        await client.ListActivityViewsAsync(TenantA, id, id, "task", true, 20);
        await client.CreateActivityAsync(Activity(id, "note"));
        await client.CompleteTaskAsync(TenantA, id);

        var controller = typeof(GeekRepository.Controllers.Crm.CrmWorkController);
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        var routes = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => (Verb: a.HttpMethods.Single(), Template: Strip($"{root}/{a.Template}")))
            .ToList();

        Assert.Equal(15, recorder.Calls.Count);
        foreach (var call in recorder.Calls)
        {
            var path = call.PathAndQuery.Split('?')[0].TrimStart('/');
            Assert.True(routes.Any(r => r.Verb == call.Method.Method && Matches(r.Template, path)),
                $"GeekRepository declares no {call.Method} route for {path}.");
        }
        Assert.Equal(routes.Count, recorder.Calls.Count);
        Assert.Equal($"/repo/crm/tenants/{TenantA}/participants/search?limit=20&q=maria%20%26%20co&stage=lead&kind=person", recorder.Calls[2].PathAndQuery);
    }

    // ----- who did it, and which tenant -----

    private sealed class StoreReached(string method, object?[] args) : Exception(method)
    {
        public string Method { get; } = method;
        public object?[] Args { get; } = args;
    }

    private class Trap : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new StoreReached(targetMethod!.Name, args ?? []);
    }

    private static CrmWorkController ControllerFor(CrmCaller caller)
    {
        var controller = new CrmWorkController(
            DispatchProxy.Create<ICrmWorkStore, Trap>(),
            DispatchProxy.Create<ICrmStore, Trap>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Items[CrmAuthConstants.CallerItem] = caller;
        return controller;
    }

    private static async Task<StoreReached> ReachedAsync(Func<Task<IActionResult>> call) =>
        await Assert.ThrowsAsync<StoreReached>(call);

    [Fact]
    public async Task A_signed_in_user_works_in_their_own_tenant_whatever_the_address_asks_for()
    {
        var member = new CrmCaller(false, TenantB, TenantUserRoles.Member, User);

        var stages = await ReachedAsync(() => ControllerFor(member).ListStages(TenantA, default));
        Assert.Equal(("ListStagesAsync", TenantB), (stages.Method, stages.Args[0]));
        var search = await ReachedAsync(() => ControllerFor(member).SearchParticipants("maria", null, null, 10, TenantA, default));
        Assert.Equal(TenantB, search.Args[0]);
        var dashboard = await ReachedAsync(() => ControllerFor(CrmCaller.Server).GetDashboard(null, default));
        Assert.Equal(TenantA, dashboard.Args[0]);
    }

    [Fact]
    public async Task A_note_and_a_stage_change_are_signed_with_the_users_id_and_a_servers_with_api()
    {
        var member = new CrmCaller(false, TenantB, TenantUserRoles.Member, User);

        var note = await ReachedAsync(() => ControllerFor(member).CreateActivity(new CreateActivityRequest(Guid.NewGuid(), null, "note", null, null, "Hi", null, null, null), null, default));
        var command = Assert.IsType<CreateActivityCommand>(note.Args[0]);
        Assert.Equal((TenantB, User.ToString(), "note"), (command.TenantId, command.CreatedBy, command.Channel));
        Assert.True(command.OccurredAt > DateTime.UtcNow.AddMinutes(-1));

        var moved = await ReachedAsync(() => ControllerFor(member).MoveDealStage(Guid.NewGuid(), new MoveDealStageRequest("Qualified", null), null, default));
        Assert.Equal(("MoveDealStageAsync", User.ToString()), (moved.Method, ((MoveDealStageCommand)moved.Args[1]!).ChangedBy));

        var byServer = await ReachedAsync(() => ControllerFor(CrmCaller.Server).MoveDealStage(Guid.NewGuid(), new MoveDealStageRequest("Qualified", null), null, default));
        Assert.Equal("api", ((MoveDealStageCommand)byServer.Args[1]!).ChangedBy);
    }

    [Fact]
    public async Task A_request_missing_its_body_or_its_stage_is_refused_before_the_store_is_reached()
    {
        var owner = new CrmCaller(false, TenantA, TenantUserRoles.Owner, User);

        Assert.IsType<BadRequestObjectResult>(await ControllerFor(owner).CreateParticipant(null, null, default));
        Assert.IsType<BadRequestObjectResult>(await ControllerFor(owner).CreateDeal(null, null, default));
        Assert.IsType<BadRequestObjectResult>(await ControllerFor(owner).MoveDealStage(Guid.NewGuid(), new MoveDealStageRequest(" ", null), null, default));
        Assert.IsType<BadRequestObjectResult>(await ControllerFor(owner).CreateActivity(null, null, default));
    }

    [Fact]
    public void Every_work_route_sits_behind_the_access_filter_and_only_the_three_deletes_are_owner_only()
    {
        Assert.NotNull(typeof(CrmWorkController).GetCustomAttribute<CrmAccessAttribute>());
        var actions = typeof(CrmWorkController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        var ownerOnly = actions.Where(m => m.GetCustomAttribute<CrmOwnerOnlyAttribute>() is not null).Select(m => m.Name).Order().ToList();
        Assert.Equal(["DeleteActivity", "DeleteDeal", "DeleteParticipant"], ownerOnly);
        Assert.All(actions.Where(m => m.Name.StartsWith("Delete", StringComparison.Ordinal)),
            m => Assert.Equal("HttpDeleteAttribute", Assert.Single(m.GetCustomAttributes<HttpMethodAttribute>()).GetType().Name));
    }

    [Fact]
    public void No_route_is_declared_twice_across_the_two_geekapi_crm_controllers()
    {
        static IEnumerable<string> Routes(Type controller) => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => $"{a.HttpMethods.Single()} {Strip(a.Template ?? string.Empty)}");

        var all = Routes(typeof(CrmController)).Concat(Routes(typeof(CrmWorkController))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
