using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using PatchAdams.Core;
using PatchAdamsSharp;

internal static class DiffBackgroundTests
{
    public static void Run()
    {
        var type = typeof(MainWindow).Assembly.GetType("PatchAdamsSharp.CodeEditor")!;
        var mappedEditor = (RichTextBox)Activator.CreateInstance(type)!;
        var goldenEditor = (RichTextBox)Activator.CreateInstance(type)!;
        string[] lines = ["+ added", "- removed", "  context"];
        DisplayLine[] mapped =
        [
            new(DisplayLineKind.Added, null, 1, "added"),
            new(DisplayLineKind.Removed, 1, null, "removed"),
            new(DisplayLineKind.Context, 2, 2, "context")
        ];
        var setCode = type.GetMethod("SetCode")!;
        setCode.Invoke(mappedEditor, [lines, mapped, ".txt", true, null]);
        setCode.Invoke(goldenEditor, [lines, Array.Empty<DisplayLine>(), ".txt", false, null]);
        var previousTheme = ThemePalette.Current.Name;
        try
        {
            foreach (var theme in new[] { "One Dark", "Ayu Dark" })
            {
                ThemePalette.Current.Apply(theme);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                foreach (var editor in new[] { mappedEditor, goldenEditor })
                {
                    var rows = editor.Document.Blocks.OfType<Paragraph>().ToArray();
                    Require(rows[0].Background is SolidColorBrush added && added.Color == Color.FromRgb(7, 35, 18),
                        $"{theme} added-line background is exactly #072312");
                    Require(rows[1].Background is SolidColorBrush removed && removed.Color == ThemePalette.Current["#472D33"],
                        $"{theme} removed-line background stays unchanged");
                    Require(rows[2].Background is null, $"{theme} context has no diff background");
                }
            }
        }
        finally
        {
            ThemePalette.Current.Apply(previousTheme);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
