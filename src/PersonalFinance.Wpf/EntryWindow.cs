using System.Windows;
using System.Windows.Controls;

namespace PersonalFinance.Wpf;

public record Choice(string Name, object Value);

public sealed class EntryWindow : Window
{
    private readonly StackPanel panel = new() { Margin = new Thickness(28) };
    private readonly Dictionary<string, Control> fields = [];
    private readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button save = new() { Content = "Сохранить", Margin = new Thickness(0, 12, 0, 0) };
    public Func<Task>? Submit { get; set; }

    public EntryWindow(string title)
    {
        Title = title; Width = 440; SizeToContent = SizeToContent.Height; MaxHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(error); panel.Children.Add(save);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try { if (Submit is not null) await Submit(); DialogResult = true; }
            catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException)
            { error.Text = exception is System.Net.Http.HttpRequestException ? "Сервер недоступен." : exception.Message; }
            finally { save.IsEnabled = true; }
        };
    }

    public void AddText(string key, string label, string value = "")
    {
        var field = new TextBox { Text = value }; fields[key] = field;
        AddField(label, field);
    }

    public ComboBox AddChoices(string key, string label, Choice[] choices)
    {
        var field = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0 }; fields[key] = field;
        AddField(label, field);
        return field;
    }

    private void AddField(string label, Control field)
    {
        panel.Children.Insert(panel.Children.Count - 2, new Label { Content = label, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Insert(panel.Children.Count - 2, field);
    }

    public string Text(string key) => ((TextBox)fields[key]).Text;
    public void SetText(string key, string value) => ((TextBox)fields[key]).Text = value;
    public T Selected<T>(string key) => (T)((Choice)((ComboBox)fields[key]).SelectedItem).Value;
}
