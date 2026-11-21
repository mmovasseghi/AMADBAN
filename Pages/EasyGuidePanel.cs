namespace Amadban.Pages;

public sealed record EasyGuideStep(string Title, string? Detail = null, string? Page = null, string? LinkText = null);
public sealed record EasyGuidePanel(string Eyebrow, string Title, string? Lead, IReadOnlyList<EasyGuideStep> Steps, bool PinnedOpen = false);