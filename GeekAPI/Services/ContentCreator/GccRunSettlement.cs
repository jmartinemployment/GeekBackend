namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What a Generate saves and what it reports once every requested type has been attempted: the decision,
/// with nothing to read and nothing to write, so it can be tested without a repository.
/// </summary>
/// <remarks>
/// <para>
/// <b>A type that cannot be written is that type's refusal, not the run's.</b> Every requested type is
/// attempted. The pieces of the types that wrote are saved together in one write, and each type that did
/// not is named in <c>refusals</c> with its reason and stays empty. Only when no type wrote anything does
/// the run fail, and it says why for each type. That is Jeff's decision of 2026-10-06 ("When some types
/// write, those save and the refused ones stay empty"), and his word on 2026-10-07: "One failure should not
/// kill the batch". The coordinator did the opposite until then: it threw as soon as any type failed,
/// before the save, so on 2026-10-07 five good pages (social, email, three tool pages) were discarded
/// beside a pillar, a blog and two tool pages that were refused, and the job reported nothing saved.
/// </para>
/// <para>
/// It fails closed on states a run cannot be in. An attempt that both wrote and refused, or did neither,
/// or "wrote" with no piece, is a bug in the caller, and settling it would save or report something the run
/// did not do.
/// </para>
/// </remarks>
internal static class GccRunSettlement
{
    /// <summary>One requested type's attempt: what it wrote, or why it did not.</summary>
    internal sealed record TypeAttempt(string Type, GccGenerationCoordinator.TypeOutcome? Outcome, string? Error)
    {
        internal static TypeAttempt Wrote(string type, GccGenerationCoordinator.TypeOutcome outcome) => new(type, outcome, null);

        internal static TypeAttempt Refused(string type, string error) => new(type, null, error);
    }

    /// <summary>
    /// One thing the run did not write. <c>Text</c> is the reason, handed to the live note beside the type;
    /// <c>Line</c> is the entry in the result's <c>refusals</c>, which has to say what it is about.
    /// </summary>
    internal sealed record TypeRefusal(string Type, string Text, string Line)
    {
        /// <summary>A whole type that was not written: its line leads with the type.</summary>
        internal static TypeRefusal OfType(string type, string reason) => new(type, reason, RefusalLine(type, reason));

        /// <summary>One partner a tool run did not write. The reason leads with the partner
        /// ("Approvalmax: Refused: ..."), and its line leads with the type like any other.</summary>
        /// <remarks>
        /// Until 2026-10-13 its line was the reason alone, with no type. The page lists what a run did
        /// not write from two things, the live note of each type ("tool" and this reason, which it
        /// prints as "tool: Adp: ...") and the run's record (this line), and shows a line once when the
        /// two are the same text. They were not, so on the 13:16 UTC run of that day ADP's page was
        /// listed twice under "Not written", as "tool: Adp: ..." and as "Adp: ...".
        /// </remarks>
        internal static TypeRefusal OfPartner(string type, string partnerRefusal) =>
            new(type, partnerRefusal, RefusalLine(type, partnerRefusal));
    }

    /// <summary>What to save, and what to report as not written.</summary>
    internal sealed record Settled(
        IReadOnlyList<GccGenerationCoordinator.GeneratedPiece> Pieces,
        IReadOnlyList<TypeRefusal> Refusals);

    /// <summary>One definition of a refusal line: "{type}: {reason}". The page builds the same text
    /// from a type's live note (content-creator-v2 <c>ProjectContentWorkspace.tsx</c>), which is how
    /// a line the two both carry is listed once.</summary>
    internal static string RefusalLine(string type, string text) => $"{type}: {text}";

    /// <summary>
    /// Settles the run. Throws, with the same joined message as before, when no type wrote anything; that
    /// is the only way a run whose types were all attempted fails.
    /// </summary>
    internal static Settled Settle(IReadOnlyList<TypeAttempt> attempts)
    {
        if (attempts.Count == 0)
        {
            throw new InvalidOperationException("No content type was attempted, so there is nothing to save.");
        }

        Check(attempts);
        if (attempts.All(a => a.Outcome is null))
        {
            throw new InvalidOperationException(NothingWrote(Collect(attempts).Refusals));
        }

        return Collect(attempts);
    }

    /// <summary>
    /// Settles one part of a run that has two: its pages, or the short pieces written from one of them
    /// after the pages are saved (a Generate asked for an email, a social post or an ad).
    /// </summary>
    /// <remarks>
    /// A part in which nothing wrote is that part's refusals and not the run's failure: an email is
    /// still written from the Pillar the project already has when the Blog asked for beside it is
    /// refused, and a Pillar is still saved when its email is. The caller fails the run when neither
    /// part wrote anything (<see cref="NothingWrote"/>). A part with nothing asked of it settles to
    /// nothing. The states a run cannot be in are refused here as they are in <see cref="Settle"/>.
    /// </remarks>
    internal static Settled SettlePart(IReadOnlyList<TypeAttempt> attempts)
    {
        Check(attempts);
        return Collect(attempts);
    }

    /// <summary>What a run that wrote nothing fails with: every refusal line, joined as it always was.</summary>
    internal static string NothingWrote(IEnumerable<TypeRefusal> refusals) =>
        string.Join(" | ", refusals.Select(refusal => refusal.Line));

    private static void Check(IReadOnlyList<TypeAttempt> attempts)
    {
        foreach (var attempt in attempts)
        {
            if ((attempt.Outcome is null) == (attempt.Error is null))
            {
                throw new InvalidOperationException(
                    $"The run's record of '{attempt.Type}' says both or neither of written and refused; it cannot be settled.");
            }

            if (attempt.Outcome is { Pieces.Count: 0 })
            {
                throw new InvalidOperationException(
                    $"'{attempt.Type}' reported success but wrote no piece; it cannot be settled.");
            }
        }
    }

    private static Settled Collect(IReadOnlyList<TypeAttempt> attempts)
    {
        // In request order, a type's own refusals together: a refused type is one entry, and a tool type
        // that wrote contributes each partner it refused, as it did before.
        var refusals = new List<TypeRefusal>();
        foreach (var attempt in attempts)
        {
            if (attempt.Error is not null)
            {
                refusals.Add(TypeRefusal.OfType(attempt.Type, attempt.Error));
                continue;
            }

            foreach (var soft in attempt.Outcome!.SoftFailures)
            {
                refusals.Add(TypeRefusal.OfPartner(attempt.Type, soft));
            }
        }

        return new Settled([.. attempts.Where(a => a.Outcome is not null).SelectMany(a => a.Outcome!.Pieces)], refusals);
    }
}
