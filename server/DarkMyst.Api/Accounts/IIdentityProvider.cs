namespace DarkMyst.Api.Accounts
{
    /// <summary>One durable external identity a guest account can link to.</summary>
    public sealed record ExternalIdentity(string Provider, string ExternalId);

    /// <summary>
    /// Stands in for Apple/Google Sign-In (docs/04-economy-spec.md: "บัญชีเริ่มเป็น guest ได้ทันที
    /// แล้วเชื่อมกับบัญชีถาวร"). This round does not implement real OAuth — validating a real
    /// provider token is out of scope — but the linking logic behind it
    /// (<see cref="LinkingService"/>) is real and does not change when this is swapped for one.
    /// <para>
    /// The stub trusts whatever external id the client presents. That is fine for this round (no
    /// real credential is at stake yet) and is exactly the seam a real implementation replaces:
    /// everything downstream of <see cref="Resolve"/> already treats the identity as opaque and
    /// verified.
    /// </para>
    /// </summary>
    public interface IIdentityProvider
    {
        ExternalIdentity Resolve(string provider, string externalToken);
    }

    /// <summary>Stub: the "token" a client presents for linking is the external id itself, no
    /// signature or provider round-trip. Never use this for anything that matters.</summary>
    public sealed class StubIdentityProvider : IIdentityProvider
    {
        public ExternalIdentity Resolve(string provider, string externalToken)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new System.ArgumentException("provider is required.", nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(externalToken))
            {
                throw new System.ArgumentException("externalToken is required.", nameof(externalToken));
            }

            return new ExternalIdentity(provider, externalToken);
        }
    }
}
