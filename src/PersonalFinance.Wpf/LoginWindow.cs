using System.Windows;
using System.Windows.Controls;
using PersonalFinance.Contracts.Auth;

namespace PersonalFinance.Wpf;

public sealed class LoginWindow : Window
{
    private readonly TextBox email = new();
    private readonly TextBox name = new();
    private readonly PasswordBox password = new() { Padding = new Thickness(10) };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button login = new() { Content = "Войти", Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button register = new() { Content = "Создать аккаунт" };
    private readonly FinanceApi api;
    public UserDto? User { get; private set; }

    public LoginWindow(FinanceApi api)
    {
        this.api = api;
        Title = "Вход"; Width = 440; Height = 610; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = "Мои финансы", FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new Label { Content = "Имя (для регистрации)" }); panel.Children.Add(name);
        panel.Children.Add(new Label { Content = "Email" }); panel.Children.Add(email);
        panel.Children.Add(new Label { Content = "Пароль" }); panel.Children.Add(password);
        panel.Children.Add(error); panel.Children.Add(login); panel.Children.Add(register);
        Content = panel;
        login.Click += async (_, _) => await Authenticate(false);
        register.Click += async (_, _) => await Authenticate(true);
    }

    private async Task Authenticate(bool create)
    {
        login.IsEnabled = register.IsEnabled = false;
        error.Text = "";
        try
        {
            User = create
                ? await api.SendAsync<UserDto>("auth/register", new RegisterRequest(name.Text, email.Text, password.Password))
                : await api.SendAsync<UserDto>("auth/login", new AuthRequest(email.Text, password.Password));
            password.Clear();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            error.Text = exception is System.Net.Http.HttpRequestException ? "Не удалось подключиться к серверу. Запустите PersonalFinance.Api." : exception.Message;
        }
        finally { login.IsEnabled = register.IsEnabled = true; }
    }
}
