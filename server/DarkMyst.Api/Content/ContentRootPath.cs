namespace DarkMyst.Api.Content
{
    /// <summary>
    /// The one directory on disk the running process treats as <c>content/</c> — the same path
    /// <see cref="ContentLocator.Locate"/> resolved for <see cref="ContentPackRegistry"/> at
    /// startup, handed to <c>Admin/AdminContentService</c> too so publish/rollback write to
    /// exactly the directory the registry loaded from, never a second copy it invents itself.
    /// A plain wrapper (not a bare <c>string</c>) so it resolves unambiguously through DI.
    /// </summary>
    public sealed class ContentRootPath
    {
        public ContentRootPath(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }
    }
}
