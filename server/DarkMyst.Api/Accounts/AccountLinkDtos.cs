namespace DarkMyst.Api.Accounts
{
    public sealed record LinkStartRequest(string Provider, string ExternalToken);

    public sealed record LinkConfirmRequest(string PendingLinkId, LinkChoice Choice);
}
