using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Transactions;

namespace PersonalFinance.Wpf;

public partial class TransactionWindow : Window
{
    private readonly FinanceApi api;
    private readonly CategoryDto[] categories;
    private readonly Guid requestId = Guid.NewGuid();
    private bool saving;

    public TransactionWindow(FinanceApi api, AccountDto[] accounts, CategoryDto[] categories)
    {
        this.api = api;
        this.categories = categories;
        InitializeComponent();
        AccountBox.DisplayMemberPath = "Name";
        AccountBox.ItemsSource = accounts;
        AccountBox.SelectedIndex = 0;
        CategoryBox.DisplayMemberPath = "Name";
        DateBox.SelectedDate = DateTime.Today;
        UpdateCategories();
        Loaded += (_, _) => AmountBox.Focus();
    }

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox is not null && categories is not null) UpdateCategories();
    }

    private void UpdateCategories()
    {
        CategoryBox.ItemsSource = categories.Where(c => c.Type == (TypeBox.SelectedIndex == 1 ? TransactionType.Income : TransactionType.Expense)).ToArray();
        CategoryBox.SelectedIndex = 0;
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        var raw = AmountBox.Text.Trim().Replace(" ", "").Replace("\u00a0", "").Replace(',', '.');
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0 || amount > 999999999m || decimal.Round(amount, 2) != amount)
        { ErrorLabel.Text = "Введите положительную сумму до 999 999 999 ₽ с точностью до копеек."; return; }
        if (DateBox.SelectedDate is not DateTime date || date.Date > DateTime.Today)
        { ErrorLabel.Text = "Выберите дату не позднее сегодняшнего дня."; return; }
        if (AccountBox.SelectedItem is not AccountDto account || CategoryBox.SelectedItem is not CategoryDto category)
        { ErrorLabel.Text = "Выберите счёт и категорию."; return; }
        var button = (Button)sender;
        saving = true; button.IsEnabled = false;
        try
        {
            var request = new CreateTransactionRequest(requestId, account.Id, category.Id, category.Type, amount, DateOnly.FromDateTime(date), string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim());
            await api.SendAsync<TransactionDto>("transactions", request);
            DialogResult = true;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException)
        { ErrorLabel.Text = exception is System.Net.Http.HttpRequestException ? "Сервер недоступен. Попробуйте ещё раз." : exception.Message; }
        finally { saving = false; button.IsEnabled = true; }
    }
}
