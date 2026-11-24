using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PersonalFinance.Contracts.Accounts;
using PersonalFinance.Contracts.Budgets;
using PersonalFinance.Contracts.Transactions;
using PersonalFinance.Contracts.Reports;

namespace PersonalFinance.Wpf;

public partial class MainWindow : Window
{
    private readonly FinanceApi api;
    private readonly List<FinanceTransaction> transactions = [];
    private AccountDto[] accounts = [];
    private CategoryDto[] categories = [];
    private string page = "Обзор";
    private bool loading;

    public MainWindow(FinanceApi api)
    {
        this.api = api;
        InitializeComponent();
        Loaded += async (_, _) => await Reload();
    }

    internal static string Money(decimal amount) => amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    private async Task Reload()
    {
        if (loading) return;
        loading = true;
        try
        {
            var today = DateTime.Today;
            var from = new DateOnly(today.Year, today.Month, 1);
            var to = from.AddMonths(1);
            var loadedAccounts = await api.GetAsync<AccountDto[]>("accounts");
            var loadedCategories = await api.GetAsync<CategoryDto[]>("categories");
            var report = await api.GetAsync<FinanceReportDto>($"reports/summary?from={from:yyyy-MM-dd}&toExclusive={to:yyyy-MM-dd}");
            var history = await api.GetAsync<TransactionDto[]>($"transactions?from=0001-01-01&toExclusive={to:yyyy-MM-dd}");
            var budgets = await api.GetAsync<BudgetReportDto[]>($"budgets?year={today.Year}&month={today.Month}");
            accounts = loadedAccounts;
            categories = loadedCategories;
            transactions.Clear();
            transactions.AddRange(history.Select(t => new FinanceTransaction(t.OperationDate.ToDateTime(TimeOnly.MinValue),
                t.Comment ?? categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Операция",
                categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Категория",
                accounts.FirstOrDefault(a => a.Id == t.AccountId)?.Name ?? "Счёт", t.Amount, t.Type == TransactionType.Income)));
            PeriodLabel.Text = today.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
            BalanceLabel.Text = Money(report.Accounts.Sum(a => a.BalanceAtEnd));
            IncomeLabel.Text = Money(report.TotalIncome);
            ExpenseLabel.Text = Money(report.TotalExpense);
            AccountsList.ItemsSource = report.Accounts.Select(a => new { a.Name, BalanceText = Money(a.BalanceAtEnd), StateText = a.IsArchived ? "Архивный счёт · RUB" : "Активный счёт · RUB" }).ToArray();
            BudgetList.ItemsSource = budgets.Select(b => new
            {
                Category = b.CategoryName,
                Summary = $"{Money(b.Budget.Spent)} из {Money(b.Budget.Limit)}",
                Percent = Math.Min(100m, b.Budget.Spent / b.Budget.Limit * 100m),
                BarColor = b.Budget.Status == BudgetStatus.Exceeded ? "#B65539" : "#805EFF",
                Status = b.Budget.Remaining < 0 ? "Превышение: " + Money(-b.Budget.Remaining) : "Осталось: " + Money(b.Budget.Remaining)
            }).ToArray();
            ReportList.ItemsSource = report.ExpensesByCategory.Select(c => new { Category = c.Name, AmountText = Money(c.Amount) }).ToArray();
            ReportTotal.Text = "Разница доходов и расходов за месяц: " + Money(report.NetIncome);
            SavingsGauge.Value = report.TotalIncome > 0 ? (double)Math.Clamp(report.NetIncome / report.TotalIncome * 100m, 0m, 100m) : 0;
            BudgetGauge.Value = budgets.Length > 0 ? (double)(budgets.Sum(b => b.Budget.Spent) / budgets.Sum(b => b.Budget.Limit) * 100m) : 0;
            var maximum = report.ExpensesByCategory.Count > 0 ? report.ExpensesByCategory.Max(c => c.Amount) : 1m;
            ChartList.ItemsSource = report.ExpensesByCategory.Select((c, i) => new { Category = c.Name, AmountText = Money(c.Amount), Height = (double)(c.Amount / maximum * 115m), Color = i == 0 ? "#805EFF" : "#A0AFDA" }).ToArray();
            FilterTransactions();
            UpdatePage();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(this, exception is HttpRequestException ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api." : exception.Message, "Не удалось загрузить данные", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { loading = false; }
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        page = (string)((Button)sender).Tag;
        UpdatePage();
    }

    private void UpdatePage()
    {
        PageTitle.Text = page;
        InsightsPanel.Visibility = page is "Обзор" or "Отчёты" ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility = page is "Обзор" or "Отчёты" ? Visibility.Visible : Visibility.Collapsed;
        AccountsPanel.Visibility = page is "Обзор" or "Счета" ? Visibility.Visible : Visibility.Collapsed;
        OperationsPanel.Visibility = page is "Обзор" or "Операции" ? Visibility.Visible : Visibility.Collapsed;
        BudgetsPanel.Visibility = page is "Обзор" or "Бюджеты" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPanel.Visibility = page == "Отчёты" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { OverviewNav, AccountsNav, TransactionsNav, BudgetsNav, ReportsNav })
        {
            button.Background = button.Tag.ToString() == page ? new SolidColorBrush(Color.FromRgb(128, 94, 255)) : Brushes.Transparent;
            button.Foreground = button.Tag.ToString() == page ? Brushes.White : new SolidColorBrush(Color.FromRgb(64, 72, 103));
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (OperationsGrid is not null && EmptyLabel is not null) FilterTransactions();
    }

    private void FilterTransactions()
    {
        var filtered = transactions.Where(t => $"{t.Description} {t.Category} {t.Account}".Contains(SearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(t => t.Date).ToArray();
        OperationsGrid.ItemsSource = filtered;
        EmptyLabel.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddTransaction(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        if (!accounts.Any(a => !a.IsArchived) || categories.Length == 0)
        {
            MessageBox.Show(this, "Сначала создайте счёт и категорию.", "Добавление операции");
            return;
        }
        var dialog = new TransactionWindow(api, accounts.Where(a => !a.IsArchived).ToArray(), categories) { Owner = this };
        if (dialog.ShowDialog() == true) await Reload();
    }

    private async void RefreshData(object sender, RoutedEventArgs e) => await Reload();

    private async void AddAccount(object sender, RoutedEventArgs e)
    {
        var form = new EntryWindow("Новый счёт");
        form.AddText("name", "Название"); form.AddText("amount", "Начальный остаток, ₽", "0");
        form.Submit = async () =>
        {
            await api.SendAsync<AccountDto>("accounts", new CreateAccountRequest(form.Text("name"), ParseMoney(form.Text("amount"))));
        };
        form.Owner = this;
        if (form.ShowDialog() == true) await Reload();
    }

    private async void AddCategory(object sender, RoutedEventArgs e)
    {
        var form = new EntryWindow("Новая категория");
        form.AddText("name", "Название");
        form.AddChoices("type", "Тип", new[] { new Choice("Доход", TransactionType.Income), new Choice("Расход", TransactionType.Expense) });
        form.Submit = async () => { await api.SendAsync<CategoryDto>("categories", new CreateCategoryRequest(form.Text("name"), form.Selected<TransactionType>("type"))); };
        form.Owner = this;
        if (form.ShowDialog() == true) await Reload();
    }

    private async void SetBudget(object sender, RoutedEventArgs e)
    {
        var expenseCategories = categories.Where(c => c.Type == TransactionType.Expense).ToArray();
        if (expenseCategories.Length == 0) { MessageBox.Show(this, "Сначала создайте категорию расходов."); return; }
        var form = new EntryWindow("Бюджет на текущий месяц");
        form.AddChoices("category", "Категория", expenseCategories.Select(c => new Choice(c.Name, c.Id)).ToArray());
        form.AddText("amount", "Месячный лимит, ₽");
        form.Submit = async () =>
        {
            var today = DateTime.Today;
            var id = form.Selected<Guid>("category");
            await api.SendAsync<BudgetDto>($"budgets/{id}/{today.Year}/{today.Month}", new SetBudgetRequest(id, today.Year, today.Month, ParseMoney(form.Text("amount"))), HttpMethod.Put);
        };
        form.Owner = this;
        if (form.ShowDialog() == true) await Reload();
    }

    private static decimal ParseMoney(string value)
    {
        var raw = value.Trim().Replace(" ", "").Replace("\u00a0", "").Replace(',', '.');
        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
            throw new InvalidOperationException("Введите корректную сумму.");
        return amount;
    }
}

public record FinanceTransaction(DateTime Date, string Description, string Category, string Account, decimal Amount, bool IsIncome)
{
    public string DateText => Date.ToString("dd.MM.yyyy");
    public string AmountText => (IsIncome ? "+ " : "− ") + MainWindow.Money(Amount);
}
