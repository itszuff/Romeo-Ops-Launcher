using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RomeoOps.Installer;

namespace RomeoOps;

public partial class MainWindow
{
    private void ShowModPurge(ExistingStack stack)
    {
        const string title = "Purge unused mod files";
        var gamma = stack.Layout.Gamma;
        try
        {
            var profiles = ModPurge.Profiles(gamma);
            if (profiles.Length == 0) throw new IOException("No MO2 profiles were found.");
            var body = new StackPanel { Margin = new Thickness(16) };
            var dialog = ToolWindow(title, body);
            body.Children.Add(new TextBlock { Text = "MO2 profile" });
            var picker = new ComboBox { ItemsSource = profiles, SelectedItem = Path.GetFileName(Path.GetDirectoryName(stack.Profile)) };
            if (picker.SelectedIndex < 0) picker.SelectedIndex = 0;
            body.Children.Add(picker);
            var shared = new CheckBox { Content = "Include mods enabled in other profiles", Margin = new Thickness(0, 8, 0, 8) };
            body.Children.Add(shared);
            var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = Math.Min(300, SystemParameters.WorkArea.Height * 0.42) };
            body.Children.Add(details);
            var buttons = new WrapPanel();
            body.Children.Add(buttons);
            List<PurgeCandidate>? reviewed = null;
            bool busy = false;
            var action = DialogButton("PREVIEW", () => {});
            var close = DialogButton("CLOSE", dialog.Close);
            buttons.Children.Add(close);
            buttons.Children.Add(action);
            void Reset()
            {
                reviewed = null;
                action.Content = "PREVIEW";
                action.IsEnabled = true;
                details.Text = $"Review disabled mod folders in {Path.Combine(gamma, "mods")}. Close MO2 before cleanup.";
            }
            Reset();
            picker.SelectionChanged += (_, _) => Reset();
            shared.Click += (_, _) => Reset();
            dialog.Closing += (_, e) => e.Cancel = busy;
            action.Click += async (_, _) =>
            {
                if (busy || picker.SelectedItem is not string profile) return;
                busy = true;
                close.IsEnabled = action.IsEnabled = picker.IsEnabled = shared.IsEnabled = false;
                bool includeShared = shared.IsChecked == true;
                try
                {
                    using var gate = SavedRun.Lock(stack.Layout);
                    if (SavedRun.Read(stack.Layout.Root) != null) throw new IOException("Finish the saved installation or repair before cleanup.");
                    var organizers = Process.GetProcessesByName("ModOrganizer");
                    foreach (var process in organizers) process.Dispose();
                    if (organizers.Length > 0) throw new IOException("Close Mod Organizer so its profile changes are saved, then preview again.");
                    if (reviewed == null)
                    {
                        details.Text = "Checking profile usage and folder sizes…";
                        var mods = await Task.Run(() => ModPurge.Preview(gamma, profile, includeShared));
                        if (mods.Count == 0)
                            details.Text = "No disabled mod folders qualify for cleanup in this profile.";
                        else
                        {
                            reviewed = mods;
                            details.Text = $"{mods.Count} folders · {mods.Sum(m => m.Bytes) / 1048576d:N1} MiB of files\n{Path.Combine(gamma, "mods")}\n\nDeletion is permanent. Reinstall a removed mod to use it again.\n\n" +
                                string.Join("\n", mods.Select(m => m.Name + (m.UsedBy.Length == 0 ? "" : " — enabled in " + string.Join(", ", m.UsedBy))));
                            action.Content = "PERMANENTLY DELETE";
                        }
                    }
                    else
                    {
                        details.Text = "Removing reviewed mod folders…";
                        var report = await Task.Run(() => ModPurge.Delete(gamma, profile, includeShared, reviewed));
                        Reset();
                        details.Text = report;
                    }
                }
                catch (Exception ex) { Reset(); details.Text = ex.Message; }
                finally { busy = false; close.IsEnabled = action.IsEnabled = picker.IsEnabled = shared.IsEnabled = true; }
            };
            dialog.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, title); }
    }
}
