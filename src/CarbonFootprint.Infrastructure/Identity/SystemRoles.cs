namespace CarbonFootprint.Infrastructure.Identity;

public static class SystemRoles
{
    public const string Administrator = "Administrator";
    public const string Manager = "Manager";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";

    public static readonly string[] All =
    [
        Administrator,
        Manager,
        Editor,
        Viewer
    ];
}
