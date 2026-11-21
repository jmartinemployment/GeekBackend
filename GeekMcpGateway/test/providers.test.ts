import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { allowedTools, GoogleCalendar, providerCodes, providerFor, Providers } from "../src/providers.ts";

// What must be true of the registry itself, whatever is in it. These are the checks that used to be unnecessary because there was
// one provider and one hardcoded comparison: with a list, a second entry can be wrong in ways a single constant could not be.
describe("providers", () => {
  it("resolves a provider by its code and refuses anything else", () => {
    assert.equal(providerFor("google_calendar"), GoogleCalendar);
    assert.equal(providerFor("outlook"), null);
    assert.equal(providerFor("GOOGLE_CALENDAR"), null);
    assert.equal(providerFor(""), null);
  });

  it("offers exactly the tools its argument allow-list has keys for", () => {
    for (const provider of Providers) {
      assert.deepEqual(allowedTools(provider), Object.keys(provider.toolArguments));
    }
  });

  // A forced argument for a tool that is not offered can never be applied, and a forced argument the caller is also allowed to send
  // would be silently overridden - the caller's value accepted by the validator and then replaced. Both are entry mistakes that no
  // request would reveal.
  it("forces arguments only on tools it offers, and never one the caller may also send", () => {
    for (const provider of Providers) {
      for (const [tool, forced] of Object.entries(provider.forcedArguments)) {
        assert.ok(allowedTools(provider).includes(tool), `${provider.code} forces arguments on ${tool}, which it does not offer.`);
        for (const key of Object.keys(forced)) {
          assert.ok(
            !(provider.toolArguments[tool] ?? []).includes(key),
            `${provider.code} both allows and forces ${key} on ${tool}, so a caller's value would be accepted and then thrown away.`,
          );
        }
      }
    }
  });

  it("gives every provider a distinct code, a name for a failure, and an https endpoint", () => {
    assert.equal(new Set(providerCodes()).size, Providers.length);
    for (const provider of Providers) {
      assert.notEqual(provider.code.trim(), "");
      assert.notEqual(provider.displayName.trim(), "");
      assert.ok(provider.mcpUrl.startsWith("https://"), `${provider.code} has a non-https MCP endpoint.`);
      assert.ok(allowedTools(provider).length > 0, `${provider.code} offers no tools, so nothing could ever be called on it.`);
    }
  });

  // Google Calendar's entry is what the gateway was before it had a registry, and it must not have changed in the move: the booking
  // tool still sends no invitations, and nothing that mails or shares with whoever the caller names has been let in.
  it("keeps Google Calendar's booking tool from inviting, attaching or recurring", () => {
    assert.deepEqual(GoogleCalendar.forcedArguments["create_event"], { notificationLevel: "NONE" });
    for (const forbidden of ["attendees", "attendeeEmails", "attachments", "recurrence", "conferenceData"]) {
      assert.ok(
        !(GoogleCalendar.toolArguments["create_event"] ?? []).includes(forbidden),
        `create_event may not take ${forbidden}.`,
      );
    }
  });
});
