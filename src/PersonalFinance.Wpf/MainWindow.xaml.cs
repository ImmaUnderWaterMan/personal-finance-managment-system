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
    private FinanceReportDto? monthlyReport;
    private FinanceReportDto? periodReport;
    private DateOnly reportFrom;
    private DateOnly reportToExclusive;
    private string page = "Обзор";
    private bool loading;
    private bool savingAccount;

    public MainWindow(FinanceApi api)
    {
        this.api = api;
        InitializeComponent();
        var today = DateTime.Today;
        reportFrom = new DateOnly(today.Year, today.Month, 1);
        reportToExclusive = reportFrom.AddMonths(1);
        ReportFromDate.SelectedDate = reportFrom.ToDateTime(TimeOnly.MinValue);
        ReportToDate.SelectedDate = reportToExclusive.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        Loaded += async (_, _) => await Reload();
    }

    internal static string Money(decimal amount) => amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    private async Task Reload()
    {
        if (loading) return;
        loading = true;
        ReportPeriodControls.IsEnabled = false;
        try
        {
            var today = DateTime.Today;
            var from = new DateOnly(today.Year, today.Month, 1);
            var to = from.AddMonths(1);
            var loadedAccounts = await api.GetAsync<AccountDto[]>("accounts");
            var loadedCategories = await api.GetAsync<CategoryDto[]>("categories");
            var report = await GetReport(from, to);
            var selectedReport = reportFrom == from && reportToExclusive == to
                ? report
                : await GetReport(reportFrom, reportToExclusive);
            var history = await api.GetAsync<TransactionDto[]>($"transactions?from=0001-01-01&toExclusive={to:yyyy-MM-dd}");
            var budgets = await api.GetAsync<BudgetReportDto[]>($"budgets?year={today.Year}&month={today.Month}");
            accounts = loadedAccounts;
            categories = loadedCategories;
            monthlyReport = report;
            periodReport = selectedReport;
            transactions.Clear();
            transactions.AddRange(history.Select(t => new FinanceTransaction(t.OperationDate.ToDateTime(TimeOnly.MinValue),
                t.Comment ?? categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Операция",
                categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Категория",
                accounts.FirstOrDefault(a => a.Id == t.AccountId)?.Name ?? "Счёт", t.Amount, t.Type == TransactionType.Income)));
            AccountsList.ItemsSource = report.Accounts.Select(a => new
            {
                a.AccountId,
                a.Name,
                BalanceText = Money(a.BalanceAtEnd),
                StateText = a.IsArchived ? "Архивный счёт · RUB" : "Активный счёт · RUB",
                CanArchive = !a.IsArchived
            }).ToArray();
            BudgetList.ItemsSource = budgets.Select(b => new
            {
                Category = b.CategoryName,
                Summary = $"{Money(b.Budget.Spent)} из {Money(b.Budget.Limit)}",
                Percent = Math.Min(100m, b.Budget.Spent / b.Budget.Limit * 100m),
                BarColor = b.Budget.Status == BudgetStatus.Exceeded ? "#B65539" : "#805EFF",
                Status = b.Budget.Remaining < 0 ? "Превышение: " + Money(-b.Budget.Remaining) : "Осталось: " + Money(b.Budget.Remaining)
            }).ToArray();
            BudgetGauge.Value = budgets.Length > 0 ? (double)(budgets.Sum(b => b.Budget.Spent) / budgets.Sum(b => b.Budget.Limit) * 100m) : 0;
            FilterTransactions();
            UpdatePage();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(this, exception is HttpRequestException ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api." : exception.Message, "Не удалось загрузить данные", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            loading = false;
            ReportPeriodControls.IsEnabled = true;
        }
    }

    private Task<FinanceReportDto> GetReport(DateOnly from, DateOnly toExclusive) =>
        api.GetAsync<FinanceReportDto>(FormattableString.Invariant(
            $"reports/summary?from={from:yyyy-MM-dd}&toExclusive={toExclusive:yyyy-MM-dd}"));

    private async void ApplyReportPeriod(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount) return;
        ReportPeriodError.Visibility = Visibility.Collapsed;

        if (ReportFromDate.SelectedDate is not DateTime fromDate || ReportToDate.SelectedDate is not DateTime toDate)
        {
            ShowReportError("Выберите дату начала и дату окончания периода.");
            return;
        }

        var from = DateOnly.FromDateTime(fromDate);
        var through = DateOnly.FromDateTime(toDate);
        if (from > through)
        {
            ShowReportError("Дата начала не должна быть позже даты окончания.");
            return;
        }
        if (through == DateOnly.MaxValue)
        {
            ShowReportError("Выберите более раннюю дату окончания.");
            return;
        }

        var toExclusive = through.AddDays(1);
        loading = true;
        ReportPeriodControls.IsEnabled = false;
        try
        {
            var report = await GetReport(from, toExclusive);
            reportFrom = from;
            reportToExclusive = toExclusive;
            periodReport = report;
            if (page == "Отчёты") ShowSummary(report, true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ShowReportError(exception is HttpRequestException
                ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api."
                : exception is TaskCanceledException
                    ? "Сервер не ответил вовремя. Попробуйте ещё раз."
                    : exception.Message);
        }
        finally
        {
            loading = false;
            ReportPeriodControls.IsEnabled = true;
        }
    }

    private void ShowReportError(string message)
    {
        ReportPeriodError.Text = message;
        ReportPeriodError.Visibility = Visibility.Visible;
    }

    private void ShowSummary(FinanceReportDto report, bool selectedPeriod)
    {
        PeriodLabel.Text = selectedPeriod
            ? $"{report.From:dd.MM.yyyy} — {report.ToExclusive.AddDays(-1):dd.MM.yyyy}"
            : report.From.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
        BalanceTitle.Text = selectedPeriod ? "Баланс на конец периода" : "Общий баланс";
        IncomeTitle.Text = selectedPeriod ? "Доходы за период" : "Доходы за месяц";
        ExpenseTitle.Text = selectedPeriod ? "Расходы за период" : "Расходы за месяц";
        BalanceLabel.Text = Money(report.Accounts.Sum(a => a.BalanceAtEnd));
        IncomeLabel.Text = Money(report.TotalIncome);
        ExpenseLabel.Text = Money(report.TotalExpense);
        ReportList.ItemsSource = report.ExpensesByCategory.Select(c => new { Category = c.Name, AmountText = Money(c.Amount) }).ToArray();
        ReportTotal.Text = "Разница доходов и расходов за период: " + Money(report.NetIncome);
        ReportEmptyLabel.Visibility = report.ExpensesByCategory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavingsGauge.Value = report.TotalIncome > 0 ? (double)Math.Clamp(report.NetIncome / report.TotalIncome * 100m, 0m, 100m) : 0;
        var maximum = report.ExpensesByCategory.Count > 0 ? report.ExpensesByCategory.Max(c => c.Amount) : 1m;
        ChartList.ItemsSource = report.ExpensesByCategory.Select((c, i) => new
        {
            Category = c.Name,
            AmountText = Money(c.Amount),
            Height = (double)(c.Amount / maximum * 115m),
            Color = i == 0 ? "#805EFF" : "#A0AFDA"
        }).ToArray();
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
        ReportPeriodPanel.Visibility = page == "Отчёты" ? Visibility.Visible : Visibility.Collapsed;
        BudgetIndicator.Visibility = page == "Отчёты" ? Visibility.Collapsed : Visibility.Visible;
        RhythmIndicators.Columns = page == "Отчёты" ? 1 : 2;
        var report = page == "Отчёты" ? periodReport : monthlyReport;
        if (report is not null) ShowSummary(report, page == "Отчёты");
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
        if (loading || savingAccount) return;
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

    private async void RenameAccount(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount || sender is not Button { Tag: Guid accountId }) return;
        var account = accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;

        var form = new EntryWindow("Переименование счёта") { Owner = this };
        form.AddText("name", "Название", account.Name);
        var submitting = false;
        form.Closing += (_, args) => args.Cancel = submitting;
        form.Submit = async () =>
        {
            submitting = true;
            try
            {
                await api.SendAsync<AccountDto>($"accounts/{accountId}",
                    new RenameAccountRequest(form.Text("name")), HttpMethod.Put);
            }
            finally
            {
                submitting = false;
            }
        };

        if (form.ShowDialog() == true) await Reload();
    }

    private async void ArchiveAccount(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount || sender is not Button { Tag: Guid accountId }) return;
        var account = accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null || account.IsArchived) return;

        var confirmation = MessageBox.Show(this,
            $"Архивировать счёт «{account.Name}»?\n\nИстория операций и баланс сохранятся. Добавлять новые операции по этому счёту будет нельзя.",
            "Архивирование счёта", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        savingAccount = true;
        AccountsList.IsEnabled = false;
        try
        {
            await api.SendAsync<AccountDto>($"accounts/{accountId}/archive");
            await Reload();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(this,
                exception is HttpRequestException ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api." : exception.Message,
                "Не удалось архивировать счёт", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            savingAccount = false;
            AccountsList.IsEnabled = true;
        }
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
