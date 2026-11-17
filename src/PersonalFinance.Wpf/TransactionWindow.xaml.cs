using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PersonalFinance.Wpf;

public partial class TransactionWindow : Window
{
    public FinanceTransaction? Result { get; private set; }

    public TransactionWindow(string[] accounts)
    {
        InitializeComponent();
        AccountBox.ItemsSource = accounts;
        AccountBox.SelectedIndex = 0;
        DateBox.SelectedDate = DateTime.Today;
        UpdateCategories();
        Loaded += (_, _) => AmountBox.Focus();
    }

    private void TypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox is not null) UpdateCategories();
    }

    private void UpdateCategories()
    {
        CategoryBox.ItemsSource = TypeBox.SelectedIndex == 1
            ? new[] { "Зарплата", "Подработка", "Другой доход" }
            : new[] { "Продукты", "Транспорт", "Развлечения", "Жильё", "Здоровье", "Другой расход" };
        CategoryBox.SelectedIndex = 0;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        var raw = AmountBox.Text.Trim().Replace(" ", "").Replace('\u00A0', ' ').Trim().Replace(',', '.');
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0 || amount > 999999999m || decimal.Round(amount, 2) != amount)
        {
            ErrorLabel.Text = "Введите положительную сумму до 999 999 999 ₽, не более двух знаков после запятой.";
            AmountBox.Focus();
            return;
        }
        if (DateBox.SelectedDate is not DateTime date || date.Date > DateTime.Today)
        {
            ErrorLabel.Text = "Выберите корректную дату не позднее сегодняшнего дня.";
            return;
        }
        if (AccountBox.SelectedItem is not string account || CategoryBox.SelectedItem is not string category)
        {
            ErrorLabel.Text = "Выберите счёт и категорию.";
            return;
        }
        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? category : DescriptionBox.Text.Trim();
        Result = new(date.Date, description, category, account, amount, TypeBox.SelectedIndex == 1);
        DialogResult = true;
    }
}
