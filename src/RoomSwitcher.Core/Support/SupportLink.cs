namespace RoomSwitcher.Core.Support;

/// <summary>
/// Where "Support This App" goes. griffith.software forwards it to Griffith Software's support page,
/// so where support goes can change without updating Room Switcher. Opened in the browser only.
/// </summary>
public static class SupportLink
{
    /// <summary>This program's web slug, as used in griffith.software links.</summary>
    public const string Slug = "room-switcher";

    public const string Url = "https://griffith.software/support?app=" + Slug;
}
