using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PatchAdamsSharp;

internal static class FontPreferencesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        // Use the current machine's font catalog, not a font installed only on a developer's machine.
        var installed = Fonts.SystemFontFamilies.First(family => family.Source.Contains(' '));
        CheckName(installed.Source, expectedValid: true);
        CheckName($"  {installed.Source.ToLowerInvariant()}  ", expectedValid: true);
        var localizedName = installed.FamilyNames.Values.First();
        CheckName(localizedName, expectedValid: true);
        CheckName("PatchAdams Missing Font 0F7318C2", expectedValid: false);
        CheckName($"PatchAdams Missing Font 0F7318C2, {installed.Source}", expectedValid: false);
        CheckName("   ", expectedValid: false);
        CheckName("PatchAdams Missing Font 0F7318C2", expectedValid: false, correction: installed.Source);
    }

    private static void CheckName(string name, bool expectedValid, string? correction = null)
    {
        // Exercise the actual settings dialog without invoking Settings_Click, which writes user preferences.
        var type = typeof(MainWindow).Assembly.GetType("PatchAdamsSharp.PreferencesDialog", throwOnError: true)!;
        var dialog = (Window)Activator.CreateInstance(type, name, 15d, true)!;
        var fontBox = (TextBox)type.GetField("fontFamilyBox", Private)!.GetValue(dialog)!;
        var error = (TextBlock)type.GetField("fontFamilyError", Private)!.GetValue(dialog)!;
        Exception? failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var panel = (Panel)dialog.Content;
                var buttons = panel.Children.OfType<Panel>().Single();
                var apply = buttons.Children.OfType<Button>().Single(button => (string?)button.Content == "Apply");
                Check(error.Visibility == Visibility.Collapsed, "font validation is hidden before Apply");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (expectedValid)
                    Check(dialog.DialogResult == true, $"installed font is accepted: '{name}'");
                else
                {
                    Check(dialog.IsVisible && dialog.DialogResult != true,
                        $"unavailable font cannot be applied: '{name}'");
                    Check(error.Visibility == Visibility.Visible && error.Text.Contains("Windows Settings"),
                        "unavailable font shows guidance beside the font field");
                    Check(fontBox.IsKeyboardFocusWithin && fontBox.SelectedText == name,
                        "unavailable font is selected for correction");
                    if (correction is null)
                        dialog.Close();
                    else
                    {
                        fontBox.Text = correction;
                        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(dialog.DialogResult == true, "corrected installed font is accepted");
                        Check(error.Visibility == Visibility.Collapsed, "correcting the font clears validation");
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                dialog.Close();
            }
        };
        var accepted = dialog.ShowDialog();
        if (failure is not null) throw failure;
        Check(accepted == (expectedValid || correction is not null), $"font Apply result for '{name}'");
        if (accepted == true)
            Check((string)type.GetProperty("SelectedFontFamily")!.GetValue(dialog)! == (correction ?? name).Trim(),
                "accepted font preserves the family name and trims outer whitespace");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS {message}");
    }
}
