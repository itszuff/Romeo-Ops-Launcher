using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RomeoOps;

// The file log retains the whole run. This control displays a bounded live tail.
internal sealed class RunJournal : TextBox
{
    private const int TailSize = 500;
    private readonly Queue<string> tail = new();
    private readonly DispatcherTimer refresh;
    private long received;
    private long displayed;
    private int displayedCount;

    public RunJournal()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        TextWrapping = TextWrapping.Wrap;
        FontFamily = new FontFamily("Consolas");
        FontSize = 11;
        Padding = new Thickness(8);
        BorderBrush = Brushes.Gray;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        refresh.Tick += (_, _) => Flush();
        Loaded += (_, _) => refresh.Start();
        Unloaded += (_, _) => refresh.Stop();
    }

    public void Add(string line)
    {
        tail.Enqueue(line);
        if (tail.Count > TailSize) tail.Dequeue();
        received++;
    }

    internal void Flush()
    {
        if (displayed == received || SelectionLength > 0) return;
        // Leave the visible text intact while the reader is away from the bottom.
        if (ExtentHeight - ViewportHeight - VerticalOffset > 1) return;

        var pending = received - displayed;
        if (pending > tail.Count || displayedCount + pending > TailSize * 2)
        {
            // Compact only at the live edge, in batches rather than on every line.
            Text = string.Join(Environment.NewLine, tail);
            displayedCount = tail.Count;
        }
        else
        {
            var batch = new StringBuilder();
            foreach (var line in tail.Skip(tail.Count - (int)pending))
            {
                if (displayedCount > 0) batch.AppendLine();
                batch.Append(line);
                displayedCount++;
            }
            AppendText(batch.ToString());
        }
        displayed = received;
        UpdateLayout();
        ScrollToEnd();
    }
}
