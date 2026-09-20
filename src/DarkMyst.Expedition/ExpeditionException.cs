using System;

namespace DarkMyst.Expedition
{
    /// <summary>
    /// Raised when a run cannot proceed for a reason the caller needs to see — an invalid
    /// choice index, a stage or content mismatch, resuming a run under content it was not
    /// started with. Mirrors <c>DarkMyst.Content.ContentException</c> in spirit: a plain,
    /// undecorated exception type so a server layer can catch it without pulling in anything else.
    /// </summary>
    public sealed class ExpeditionException : Exception
    {
        public ExpeditionException(string message) : base(message)
        {
        }
    }
}
