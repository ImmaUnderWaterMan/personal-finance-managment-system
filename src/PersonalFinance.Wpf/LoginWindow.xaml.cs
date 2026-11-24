using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using PersonalFinance.Contracts.Auth;

namespace PersonalFinance.Wpf;

public sealed partial class LoginWindow : Window
{
    private readonly FinanceApi api;
    private bool isSubmitting;

    public UserDto? User { get; private set; }

    public LoginWindow(FinanceApi api)
    {
        this.api = api;
        InitializeComponent();
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) => EmailBox.Focus();

    private void WindowClosing(object? sender, CancelEventArgs e) => e.Cancel = isSubmitting;

    private async void LoginClick(object sender, RoutedEventArgs e)
    {
        if (isSubmitting) return;
        isSubmitting = true;
        LoginButton.IsEnabled = RegisterButton.IsEnabled = false;
        ErrorLabel.Text = "";

        try
        {
            User = await api.SendAsync<UserDto>("auth/login", new AuthRequest(EmailBox.Text, PasswordBox.Password));
            PasswordBox.Clear();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ErrorLabel.Text = exception is HttpRequestException
                ? "Не удалось подключиться к серверу."
                : exception.Message;
        }
        finally
        {
            isSubmitting = false;
            LoginButton.IsEnabled = RegisterButton.IsEnabled = true;
        }

        if (User is not null)
        {
            DialogResult = true;
        }
    }

    private void OpenRegistration(object sender, RoutedEventArgs e)
    {
        if (isSubmitting) return;
        var registration = new RegistrationWindow(api) { Owner = this };

        if (registration.ShowDialog() == true)
        {
            User = registration.User;
            PasswordBox.Clear();
            DialogResult = true;
        }
    }
}
