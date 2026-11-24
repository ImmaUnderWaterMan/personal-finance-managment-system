using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using PersonalFinance.Contracts.Auth;

namespace PersonalFinance.Wpf;

public sealed partial class RegistrationWindow : Window
{
    private readonly FinanceApi api;
    private bool isSubmitting;

    public UserDto? User { get; private set; }

    public RegistrationWindow(FinanceApi api)
    {
        this.api = api;
        InitializeComponent();
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) => DisplayNameBox.Focus();

    private void WindowClosing(object? sender, CancelEventArgs e) => e.Cancel = isSubmitting;

    private async void RegisterClick(object sender, RoutedEventArgs e)
    {
        if (isSubmitting) return;
        isSubmitting = true;
        RegisterButton.IsEnabled = BackButton.IsEnabled = false;
        ErrorLabel.Text = "";

        try
        {
            User = await api.SendAsync<UserDto>("auth/register",
                new RegisterRequest(DisplayNameBox.Text, EmailBox.Text, PasswordBox.Password));
            PasswordBox.Clear();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ErrorLabel.Text = exception is HttpRequestException
                ? "Не удалось подключиться к серверу. Запустите PersonalFinance.Api."
                : exception.Message;
        }
        finally
        {
            isSubmitting = false;
            RegisterButton.IsEnabled = BackButton.IsEnabled = true;
        }

        if (User is not null)
        {
            DialogResult = true;
        }
    }
}
