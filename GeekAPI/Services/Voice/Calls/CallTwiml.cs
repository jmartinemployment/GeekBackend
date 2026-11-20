using Microsoft.AspNetCore.Http;
using GeekAPI.Services.Receptionist.Relay;
using Twilio.TwiML;

namespace GeekAPI.Services.Voice.Calls;

/// <summary>
/// The TwiML a tenant's call needs when it cannot be answered, and the address a ConversationRelay handshake is signed over.
///
/// <para>
/// These three lived in the company line's endpoint file (<c>Services/Receptionist/ReceptionistEndpoints.cs</c>) and were called from
/// <c>TenantCallController</c>, so deleting that file - the whole environment-configured, single-business path - would have taken them
/// with it. They are the only part of it the tenant path used, so they moved here, beside the controller that calls them, rather than
/// keeping a file alive for three methods.
/// </para>
/// </summary>
public static class CallTwiml
{
    /// <summary>
    /// No <c>&lt;Record&gt;</c>: this product keeps no call content (Zero Data Retention), so a caller who cannot be served is told to
    /// call again rather than offered voicemail.
    /// </summary>
    public static VoiceResponse Unanswered(string business, ReceptionistLanguages.Language language)
    {
        var response = new VoiceResponse();
        response.Say(UnansweredText(business, language), language: language.Code);
        response.Hangup();
        return response;
    }

    public static string UnansweredText(string business, ReceptionistLanguages.Language language) => language.Unanswered(business);

    /// <summary>
    /// The address a ConversationRelay WebSocket handshake is signed over. Twilio's docs say the handshake carries
    /// X-Twilio-Signature and do not say what it signs, so it is checked the way Twilio's webhook signatures are: over the full
    /// address Twilio requested - the wss:// URL given in the TwiML, with its query - and no parameters, since a handshake has no form.
    /// </summary>
    public static string HandshakeUrl(HttpRequest request, string publicBaseUrl) =>
        $"wss://{new Uri(publicBaseUrl).Authority}{request.Path}{request.QueryString}";
}
