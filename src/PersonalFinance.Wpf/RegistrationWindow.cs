using System.Windows;
using System.Windows.Controls;
using PersonalFinance.Contracts.Auth;

namespace PersonalFinance.Wpf;

public sealed class RegistrationWindow : Window
{
    private readonly TextBox name = new();
    private readonly TextBox email = new();
    private readonly PasswordBox password = new() { Padding = new Thickness(10) };
    private readonly TextBlock error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = System.Windows.Media.Brushes.Firebrick,
        Margin = new Thickness(0, 12, 0, 12)
    };
    private readonly Button register = new()
    {
        Content = "Зарегистрироваться",
        IsDefault = true,
        Margin = new Thickness(0, 8, 0, 8)
    };
    private readonly Button back = new() { Content = "Назад ко входу", IsCancel = true };
    private readonly FinanceApi api;
    private bool isSubmitting;

    public UserDto? User { get; private set; }

    public RegistrationWindow(FinanceApi api)
    {
        this.api = api;
        Title = "Регистрация";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock
        {
            Text = "Регистрация",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 20)
        });
        panel.Children.Add(new Label { Content = "Имя", Target = name });
        panel.Children.Add(name);
        panel.Children.Add(new Label { Content = "Email", Target = email });
        panel.Children.Add(email);
        panel.Children.Add(new Label { Content = "Пароль", Target = password });
        panel.Children.Add(password);
        panel.Children.Add(error);
        panel.Children.Add(register);
        panel.Children.Add(back);
        Content = panel;

        register.Click += async (_, _) => await Register();
        Loaded += (_, _) => name.Focus();
        Closing += (_, args) => args.Cancel = isSubmitting;
    }

    private async Task Register()
    {
        isSubmitting = true;
        register.IsEnabled = back.IsEnabled = false;
        error.Text = "";

        try
        {
            User = await api.SendAsync<UserDto>("auth/register", new RegisterRequest(name.Text, email.Text, password.Password));
            password.Clear();
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            error.Text = exception is System.Net.Http.HttpRequestException
                ? "Не удалось подключиться к серверу. Запустите PersonalFinance.Api."
                : exception.Message;
        }
        finally
        {
            isSubmitting = false;
            register.IsEnabled = back.IsEnabled = true;
        }

        if (User is not null)
        {
            DialogResult = true;
        }
    }
}
