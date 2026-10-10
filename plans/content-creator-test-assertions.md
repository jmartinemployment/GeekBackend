# Content Creator tests: the assertions

Written 2026-10-07. The assertions of the tests added for the prompt restructure, copied from the test code
by script, so the wording is the code's. `GeekBackend.Tests`, 1,886 passing when this was written.

For each new test file: the commit that added it, then each test with its assertions in order. A theory lists
its data rows. Whitespace inside a statement is collapsed to one space, so a long assertion reads on one
line. Strings are the test's own.

Existing tests whose assertions changed are in section 2. What is not tested is at the end.

## 1. New test files

### `Workflow/SectionSchemaNullabilityTests.cs`

Added in cc4dcfa.

**`An_optional_section_field_is_required_and_may_be_null(string field)`**
Data: `[InlineData("provenance")]`  `[InlineData("imagePrompt")]`  `[InlineData("href")]`

```csharp
Assert.Contains(field, required);
Assert.NotNull(property);
Assert.True(AllowsNull(property!), $"'{field}' is required by strict mode but cannot be null: {property}");
```

### `ContentCreator/OpenAiProviderOutcomeTests.cs`

Added in cc4dcfa.

**`A_finished_answer_returns_its_text_its_finish_reason_and_its_cached_tokens()`**

```csharp
Assert.Equal("""{"sections":[]}""", result.Content);
Assert.Equal("stop", result.FinishReason);
Assert.Equal(8, result.CachedTokens);
```

**`An_answer_cut_off_at_the_output_limit_fails_by_name_and_carries_what_was_written()`**

```csharp
Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));
Assert.Contains("output limit of 16384 tokens", ex.Message, StringComparison.Ordinal);
Assert.Equal("length", ex.FinishReason);
Assert.Equal("""{"sections":[{"tag":"h2" """.TrimEnd(), ex.PartialResponse);
```

**`A_refusal_fails_with_the_models_own_words()`**

```csharp
Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));
Assert.Contains("refused the request: I can't help with that.", ex.Message, StringComparison.Ordinal);
```

**`A_filtered_answer_fails_by_name()`**

```csharp
Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));
Assert.Contains("content_filter", ex.Message, StringComparison.Ordinal);
```

**`An_answer_with_no_content_fails_naming_why_rather_than_throwing_from_a_regex()`**

```csharp
Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));
Assert.Contains("returned no content (finish_reason stop)", ex.Message, StringComparison.Ordinal);
```

**`A_call_the_http_client_abandons_is_a_timeout_naming_the_seconds()`**

```csharp
Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request()));
Assert.Contains("did not answer within 90 seconds", ex.Message, StringComparison.Ordinal);
```

**`The_runs_own_cancellation_stays_a_cancellation()`**

```csharp
Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Request(), cts.Token));
```

**`The_run_log_records_the_finish_reason_the_cached_tokens_and_a_truncated_responses_text()`**

```csharp
Assert.ThrowsAsync<ContentGenerationException>(() => cut.CompleteAsync(Request()));
Assert.Equal("stop", first.RootElement.GetProperty("finishReason").GetString());
Assert.Equal(8, first.RootElement.GetProperty("cachedTokens").GetInt32());
Assert.Equal("length", second.RootElement.GetProperty("finishReason").GetString());
Assert.Equal("half a bo", second.RootElement.GetProperty("response").GetString());
Assert.Contains("output limit", second.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
```

### `ContentCreator/GccDraftIsGuardedOnceTests.cs`

Added in 303d04c (renamed from GccGuardedRetryTests).

**`A_draft_with_a_gap_ships_with_the_gap_reported_and_is_not_written_again()`**

```csharp
Assert.Contains(warnings, w => w.Contains("scheduler link", StringComparison.Ordinal));
Assert.DoesNotContain(warnings, w => w.Contains("retry", StringComparison.OrdinalIgnoreCase));
Assert.DoesNotContain(Scheduler, body.GetRawText(), StringComparison.Ordinal);
Assert.Equal(3, provider.BodyCalls);
```

**`A_batch_short_of_its_floor_and_its_keyword_is_reported_and_not_written_again()`**

```csharp
Assert.Contains("Planned section A", body.GetRawText(), StringComparison.Ordinal);
Assert.Contains(warnings, w => w.Contains("word floor", StringComparison.Ordinal));
Assert.Contains(warnings, w => w.Contains("has no heading containing", StringComparison.Ordinal));
Assert.DoesNotContain(warnings, w => w.Contains("retry", StringComparison.OrdinalIgnoreCase));
Assert.Equal(3, provider.BodyCalls);
```

**`A_draft_that_fails_a_refusing_check_is_refused_on_that_attempt()`**

```csharp
Assert.ThrowsAsync<InvalidOperationException>(() => service.GeneratePillarBodyAsync( Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None));
Assert.StartsWith("Refused: the pillar", ex.Message, StringComparison.Ordinal);
Assert.DoesNotContain("retry", ex.Message, StringComparison.OrdinalIgnoreCase);
Assert.Equal(3, provider.BodyCalls);
```

**`The_people_also_ask_section_is_on_the_draft()`**

```csharp
Assert.Contains("People Also Ask", body.GetRawText(), StringComparison.Ordinal);
```

**`An_image_prompt_failure_saves_the_guarded_draft_and_reports_it()`**

```csharp
Assert.Contains("Planned section A", body.GetRawText(), StringComparison.Ordinal);
Assert.Contains(warnings, w => w.Contains("Image prompts were not written", StringComparison.Ordinal));
```

### `Workflow/PromptBuilders/SystemPromptIsStaticTests.cs`

Added in e55f41c; one assertion added after, uncommitted.

**`A_body_calls_system_message_is_the_same_for_two_batches_two_projects_and_a_revision(string type)`**
Data: `[MemberData(nameof(Bodies))]`

```csharp
Assert.Equal(System(first), System(laterBatch));
Assert.Equal(System(first), System(otherProject));
Assert.Equal(System(first), System(revision));
```

**`None_of_a_runs_own_data_is_in_the_system_message(string type)`**
Data: `[MemberData(nameof(Bodies))]`

```csharp
Assert.DoesNotContain(runData, system, StringComparison.Ordinal);
Assert.Contains("accounts payable automation", user, StringComparison.Ordinal);
Assert.Contains("EVIDENCE: a competitor says X.", user, StringComparison.Ordinal);
Assert.Contains("Make it concrete.", user, StringComparison.Ordinal);
Assert.Contains("THE OPENING THIS PAGE ALREADY HAS", user, StringComparison.Ordinal);
```

**`The_system_message_holds_the_rules_that_never_change(string type)`**
Data: `[MemberData(nameof(Bodies))]`

```csharp
Assert.Contains("a senior consultant who knows this work", system, StringComparison.Ordinal);
Assert.Contains("Ban filler", system, StringComparison.Ordinal);
Assert.Contains("delve", system, StringComparison.Ordinal);
Assert.Contains("MONEY IS IN US DOLLARS ONLY", system, StringComparison.Ordinal);
Assert.Contains("A LINK SITS ON A FEW WORDS", system, StringComparison.Ordinal);
Assert.Contains("HEADINGS: write them for this page and no other", system, StringComparison.Ordinal);
Assert.Contains("GROUNDING:", system, StringComparison.Ordinal);
Assert.Contains("NAMING THE PARTNER TOOLS:", system, StringComparison.Ordinal);
Assert.Contains("CONTENT ONLY:", system, StringComparison.Ordinal);
Assert.Contains("\"sections\"", system, StringComparison.Ordinal);
Assert.DoesNotContain("over coffee", system, StringComparison.Ordinal);
Assert.DoesNotContain("content marketer", system, StringComparison.Ordinal);
```

**`Provenance_is_in_the_contract_for_pillar_and_blog_and_not_for_tool()`**

```csharp
Assert.Contains("\"provenance\"", System(PillarBatch(One, slot, 0, null, null, null)), StringComparison.Ordinal);
Assert.Contains("\"provenance\"", System(BlogBody(One, slot, 0, null, null, null)), StringComparison.Ordinal);
Assert.DoesNotContain("\"provenance\"", System(ToolBody(One, slot, 0, null, null, null)), StringComparison.Ordinal);
```

**`The_user_message_ends_by_pointing_back_at_the_contract_not_on_the_evidence(string type)`**
Data: `[MemberData(nameof(Bodies))]`

```csharp
Assert.EndsWith( "Answer in the JSON the output contract in the system message describes. You write each heading yourself.", user, StringComparison.Ordinal);
```

**`The_lede_calls_share_one_system_message_per_contract_whatever_the_page()`**

```csharp
Assert.Equal(System(pillarOne), System(pillarTwo));
Assert.Equal(System(blogOne), System(blogTwo));
Assert.Equal(System(blogOne), System(toolOne));
Assert.Equal(System(toolOne), System(toolTwo));
Assert.DoesNotContain("Geek", System(pillarOne), StringComparison.Ordinal);
Assert.Contains("\"introduction\"", System(pillarOne), StringComparison.Ordinal);
Assert.Contains("\"ledeType\"", System(blogOne), StringComparison.Ordinal);
```

### `ContentCreator/GccHeadingProvenanceTellsTheWriterWhatResolvesTests.cs`

Added in uncommitted.

**`The_valid_values_are_listed_by_kind_and_an_empty_kind_says_so()`**

```csharp
Assert.Contains("=== THE ONLY VALUES THAT LICENSE A HEADING ===", text, StringComparison.Ordinal);
Assert.Contains("brief: \"primaryIntent\" | \"angle\"", text, StringComparison.Ordinal);
Assert.Contains("paa: \"How long does setup take?\"", text, StringComparison.Ordinal);
Assert.Contains("site: \"Invoice capture\"", text, StringComparison.Ordinal);
Assert.Contains("evidence: \"Stampli\"", text, StringComparison.Ordinal);
Assert.Contains("competitor: none available", text, StringComparison.Ordinal);
```

**`The_instructions_own_brief_example_is_a_tag_that_can_resolve()`**

```csharp
Assert.DoesNotContain("brief:topic", instruction, StringComparison.Ordinal);
Assert.Contains("brief:primaryIntent", instruction, StringComparison.Ordinal);
```

**`A_heading_tagged_with_a_listed_value_resolves_and_the_topic_does_not()`**

```csharp
Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings([Heading("brief:primaryIntent")], evidence));
Assert.NotEmpty(GccHeadingProvenanceGuard.FindUnlicensedHeadings([Heading("brief:topic")], evidence));
```

## 2. Existing tests whose assertions changed

Added or removed assertion lines only (`+` and `-` lines from the diff of each file since before `cc4dcfa`), with the commit that changed them.

### `ContentCreator/GccRunLogTests.cs` (cc4dcfa)
Added:

```csharp
Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("response").ValueKind);
```

Removed:

```csharp
Assert.False(second.RootElement.TryGetProperty("response", out _));
```

### `ContentCreator/GccDraftGuardTests.cs` (303d04c)
Added:

```csharp
Assert.Contains(PartnerPage, finding.Detail, StringComparison.Ordinal);
Assert.Contains("names Stampli, Approvalmax and not Lightyear, Ramp, Bill", finding.Detail, StringComparison.Ordinal);
```

Removed:

```csharp
Assert.Contains(PartnerPage, finding.RetryInstruction, StringComparison.Ordinal);
Assert.Contains("a short run that names the source", finding.RetryInstruction, StringComparison.Ordinal);
Assert.Contains("names Stampli, Approvalmax and not Lightyear, Ramp, Bill", finding.RetryInstruction, StringComparison.Ordinal);
Assert.False(GccGuardVerdict.RetryReplaces(draft, retry));
Assert.False(GccGuardVerdict.RetryReplaces(draft, retry));
Assert.False(GccGuardVerdict.RetryReplaces(draft, retry));
Assert.True(GccGuardVerdict.RetryReplaces(draft, retry));
Assert.True(GccGuardVerdict.RetryReplaces(draft, Verdict(("closing-link", false))));
Assert.False(GccGuardVerdict.RetryReplaces(draft, Verdict(("closing-link", false), ("partner-mentions", false))));
```

### `ContentCreator/GccBatchShortfallTests.cs` (303d04c)

Removed:

```csharp
Assert.StartsWith("LENGTH:", shortfall.Instruction, StringComparison.Ordinal);
Assert.Contains("600-850 words", shortfall.Instruction, StringComparison.Ordinal);
Assert.StartsWith("KEYWORD:", shortfall.Instruction, StringComparison.Ordinal);
Assert.Contains("word for word, at least 6 times", shortfall.Instruction, StringComparison.Ordinal);
Assert.Contains("is not counted", shortfall.Instruction, StringComparison.Ordinal);
Assert.StartsWith("HEADING:", shortfall.Instruction, StringComparison.Ordinal);
Assert.StartsWith("LENGTH:", shortfall.Instruction, StringComparison.Ordinal);
Assert.Equal(3, owed.Count);
Assert.StartsWith("=== SHORTFALL -- WRITE THESE SECTIONS AGAIN ===", instruction, StringComparison.Ordinal);
Assert.Contains("- LENGTH:", instruction, StringComparison.Ordinal);
Assert.Contains("- KEYWORD:", instruction, StringComparison.Ordinal);
Assert.Contains("- HEADING:", instruction, StringComparison.Ordinal);
```

### `ContentCreator/GccCurrencyGuardTests.cs` (303d04c)
Added:

```csharp
Assert.Contains("US dollars", finding.Detail);
```

Removed:

```csharp
Assert.Contains("never convert it", finding.RetryInstruction);
```

### `ContentCreator/GccToolsSectionGuardTests.cs` (303d04c)

Removed:

```csharp
Assert.Contains("\"Top Tools for X\"", instruction, StringComparison.Ordinal);
Assert.Contains("Do not simply delete the material", instruction, StringComparison.Ordinal);
Assert.Contains("link the first substantive mention", instruction, StringComparison.Ordinal);
```

### `ContentCreator/GccGenerateServiceProvenanceTests.cs` (303d04c, e55f41c)
Added:

```csharp
Assert.DoesNotContain("retry", ex.Message, StringComparison.OrdinalIgnoreCase);
Assert.Equal(3, provider.Requests.Count(r => r.JsonSchemaName == "sections"));
Assert.DoesNotContain(
```

Removed:

```csharp
Assert.Contains("after a retry", ex.Message, StringComparison.OrdinalIgnoreCase);
await Assert.ThrowsAsync<InvalidOperationException>(() =>
Assert.NotNull(retryPrompt);
Assert.Contains("Made Up Subtopic", retryPrompt!, StringComparison.Ordinal);
Assert.Contains("only values that license a heading", retryPrompt!, StringComparison.Ordinal);
Assert.Contains("none available", retryPrompt!, StringComparison.Ordinal);
```

### `Workflow/PromptBuilders/ContentPromptBuilderFillerBanTests.cs` (e55f41c)
Added:

```csharp
Assert.Contains("=== BRIEF CONTROLS", user);
Assert.Contains("Primary intent: commercial_investigation", user);
Assert.Contains("Writing notes: SMBs looking to implement AI", user);
Assert.DoesNotContain("=== BRIEF CONTROLS", SystemPrompt(request));
Assert.DoesNotContain("=== BRIEF CONTROLS", UserPrompt(request));
Assert.Contains("Lede types (pick ONE ledeType", user);
Assert.DoesNotContain("Prefer a creative (hook/narrative) opening", user);
```

Removed:

```csharp
Assert.Contains("=== BRIEF CONTROLS", system);
Assert.Contains("Primary intent: commercial_investigation", system);
Assert.Contains("Writing notes: SMBs looking to implement AI", system);
Assert.DoesNotContain("=== BRIEF CONTROLS", SystemPrompt(request));
Assert.Contains("Lede types (pick ONE ledeType", system);
Assert.DoesNotContain("Prefer a creative (hook/narrative) opening", system);
```

### `Workflow/PromptBuilders/HumanRegisterTests.cs` (e55f41c)
Added:

```csharp
Assert.Contains("a senior consultant who knows this work", blog, StringComparison.Ordinal);
Assert.DoesNotContain("over coffee", blog, StringComparison.Ordinal);
Assert.Contains("a senior consultant who knows this work", pillar, StringComparison.Ordinal);
```

Removed:

```csharp
Assert.Contains("explaining this to a colleague over coffee", blog, StringComparison.Ordinal);
Assert.Contains("explaining this to a colleague over coffee", pillar, StringComparison.Ordinal);
```

### `ContentCreator/SectionHeadingCraftTests.cs` (e55f41c)
Added:

```csharp
Assert.Contains("1. \"Planned Two\"", user, StringComparison.Ordinal);
Assert.DoesNotContain("you write its heading", user, StringComparison.OrdinalIgnoreCase);
Assert.Contains("THE OPENING THIS PAGE ALREADY HAS", user, StringComparison.Ordinal);
Assert.Contains("The invoice that sat in a drawer for nine days", user, StringComparison.Ordinal);
Assert.Contains("It was still there on Friday.", user, StringComparison.Ordinal);
Assert.Contains("do not drop into neutral textbook voice", user, StringComparison.Ordinal);
Assert.DoesNotContain("THE OPENING THIS PAGE ALREADY HAS", all, StringComparison.Ordinal);
Assert.Contains($"Write {outline.Count} top-level (h2) sections", user, StringComparison.Ordinal);
Assert.All(outline, slot => Assert.Contains(slot.Label, user, StringComparison.Ordinal));
Assert.DoesNotContain("Required top-level (h2) sections, in order: Overview", user, StringComparison.Ordinal);
```

Removed:

```csharp
Assert.DoesNotContain("HEADINGS: write them for this page and no other", system, StringComparison.Ordinal);
Assert.Contains("THE OPENING THIS PAGE ALREADY HAS", system, StringComparison.Ordinal);
Assert.Contains("The invoice that sat in a drawer for nine days", system, StringComparison.Ordinal);
Assert.Contains("It was still there on Friday.", system, StringComparison.Ordinal);
Assert.Contains("do not drop into neutral textbook voice", system, StringComparison.Ordinal);
Assert.DoesNotContain("THE OPENING THIS PAGE ALREADY HAS", system, StringComparison.Ordinal);
Assert.Contains($"Write {outline.Count} top-level (h2) sections", system, StringComparison.Ordinal);
Assert.All(outline, slot => Assert.Contains(slot.Label, system, StringComparison.Ordinal));
Assert.DoesNotContain("Required top-level (h2) sections, in order: Overview", system, StringComparison.Ordinal);
```

### `ContentCreator/GccHeadingProvenanceFormsTests.cs` (uncommitted)
Added:

```csharp
Assert.Contains("brief:primaryIntent", prompt, StringComparison.Ordinal);
Assert.DoesNotContain("brief:topic", prompt, StringComparison.Ordinal);
```

Removed:

```csharp
Assert.Contains("brief:topic", prompt, StringComparison.Ordinal);
```

`GccGuardedRetryTests` was replaced by `GccDraftIsGuardedOnceTests` in `303d04c`. Its assertions are not
listed above: every one of them asserted that a retry ran, or was taken, or was rejected.

## 3. Not tested

- No real model call. Nothing here shows how the model responds to the new layout.
- No test that the prompt *prevents* the partner-subset refusal of 2026-10-07; the system message now states
  the rule and only a run shows whether the model keeps it.
- The valid-values list reaching the pillar and blog calls is built where the evidence block is composed in
  `GccGenerateService`; it is covered at the builder level above, not by a run through the service.
- The tool and pillar FAQ, the metadata and the image-prompt builders have no new assertions: they were not
  changed.
