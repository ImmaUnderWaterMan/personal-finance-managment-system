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
    private readonly List<FinanceTransaction> periodTransactions = [];
    private AccountDto[] accounts = [];
    private CategoryDto[] categories = [];
    private FinanceReportDto? monthlyReport;
    private FinanceReportDto? periodReport;
    private BudgetReportDto[] currentMonthBudgets = [];
    private BudgetReportDto[] selectedMonthBudgets = [];
    private int budgetYear;
    private int budgetMonth;
    private DateOnly reportFrom;
    private DateOnly reportToExclusive;
    private DateOnly? historyFrom;
    private DateOnly? historyToExclusive;
    private string page = "Обзор";
    private bool loading;
    private bool savingAccount;

    public MainWindow(FinanceApi api)
    {
        this.api = api;
        InitializeComponent();
        var today = DateTime.Today;
        budgetYear = today.Year;
        budgetMonth = today.Month;
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        BudgetMonthBox.ItemsSource = Enumerable.Range(1, 12)
            .Select(month => new Choice(culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(month)), month))
            .ToArray();
        BudgetMonthBox.SelectedValue = budgetMonth;
        BudgetYearBox.Text = budgetYear.ToString(CultureInfo.InvariantCulture);
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
        SetLoading(true);
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
            var history = await GetTransactionHistory(DateOnly.MinValue, to);
            var selectedHistory = historyFrom is DateOnly selectedFrom && historyToExclusive is DateOnly selectedTo
                ? await GetTransactionHistory(selectedFrom, selectedTo)
                : Array.Empty<TransactionDto>();
            var budgets = await GetBudgets(today.Year, today.Month);
            var selectedBudgets = budgetYear == today.Year && budgetMonth == today.Month
                ? budgets
                : await GetBudgets(budgetYear, budgetMonth);
            accounts = loadedAccounts;
            categories = loadedCategories;
            monthlyReport = report;
            periodReport = selectedReport;
            currentMonthBudgets = budgets;
            selectedMonthBudgets = selectedBudgets;
            transactions.Clear();
            transactions.AddRange(MapTransactions(history));
            periodTransactions.Clear();
            periodTransactions.AddRange(MapTransactions(selectedHistory));
            AccountsList.ItemsSource = report.Accounts.Select(a => new
            {
                a.AccountId,
                a.Name,
                BalanceText = Money(a.BalanceAtEnd),
                StateText = a.IsArchived ? "Архивный счёт · RUB" : "Активный счёт · RUB",
                CanArchive = !a.IsArchived
            }).ToArray();
            BudgetGauge.Value = budgets.Length > 0 ? (double)(budgets.Sum(b => b.Budget.Spent) / budgets.Sum(b => b.Budget.Limit) * 100m) : 0;
            UpdatePage();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(this, exception is HttpRequestException ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api." : exception.Message, "Не удалось загрузить данные", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool value)
    {
        loading = value;
        ReportPeriodControls.IsEnabled = !value;
        BudgetPeriodControls.IsEnabled = !value;
        HistoryPeriodControls.IsEnabled = !value;
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
        SetLoading(true);
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
            SetLoading(false);
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
        BudgetPeriodPanel.Visibility = page == "Бюджеты" ? Visibility.Visible : Visibility.Collapsed;
        var currentMonth = monthlyReport?.From ?? DateOnly.FromDateTime(DateTime.Today);
        if (page == "Бюджеты")
        {
            ShowBudgets(selectedMonthBudgets, budgetYear, budgetMonth);
            PeriodLabel.Text = BudgetPeriodText(budgetYear, budgetMonth);
        }
        else
        {
            ShowBudgets(currentMonthBudgets, currentMonth.Year, currentMonth.Month);
        }
        HistoryPeriodPanel.Visibility = page == "Операции" ? Visibility.Visible : Visibility.Collapsed;
        OperationsTitle.Text = page == "Операции" ? "История операций" : "Последние операции";
        if (page == "Операции")
        {
            PeriodLabel.Text = historyFrom is DateOnly from && historyToExclusive is DateOnly to
                ? $"{from:dd.MM.yyyy} — {to.AddDays(-1):dd.MM.yyyy}"
                : "Вся история";
        }
        FilterTransactions();
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
        var source = page == "Операции" && historyFrom.HasValue ? periodTransactions : transactions;
        var filtered = source.Where(t => $"{t.Description} {t.Category} {t.Account}".Contains(SearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(t => t.Date).ToArray();
        OperationsGrid.ItemsSource = filtered;
        EmptyLabel.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Task<TransactionDto[]> GetTransactionHistory(DateOnly from, DateOnly toExclusive) =>
        api.GetAsync<TransactionDto[]>(FormattableString.Invariant(
            $"transactions?from={from:yyyy-MM-dd}&toExclusive={toExclusive:yyyy-MM-dd}"));

    private IEnumerable<FinanceTransaction> MapTransactions(IEnumerable<TransactionDto> source) =>
        source.Select(t => new FinanceTransaction(t.OperationDate.ToDateTime(TimeOnly.MinValue),
            t.Comment ?? categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Операция",
            categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Категория",
            accounts.FirstOrDefault(a => a.Id == t.AccountId)?.Name ?? "Счёт", t.Amount, t.Type == TransactionType.Income));

    private async void ApplyHistoryPeriod(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount) return;
        HistoryPeriodError.Visibility = Visibility.Collapsed;
        if (HistoryFromDate.SelectedDate is not DateTime fromDate || HistoryToDate.SelectedDate is not DateTime toDate)
        {
            ShowHistoryError("Выберите дату начала и дату окончания периода.");
            return;
        }

        var from = DateOnly.FromDateTime(fromDate);
        var through = DateOnly.FromDateTime(toDate);
        if (from > through)
        {
            ShowHistoryError("Дата начала не должна быть позже даты окончания.");
            return;
        }
        if (through == DateOnly.MaxValue)
        {
            ShowHistoryError("Выберите более раннюю дату окончания.");
            return;
        }

        var toExclusive = through.AddDays(1);
        SetLoading(true);
        try
        {
            var history = await GetTransactionHistory(from, toExclusive);
            periodTransactions.Clear();
            periodTransactions.AddRange(MapTransactions(history));
            historyFrom = from;
            historyToExclusive = toExclusive;
            if (page == "Операции") UpdatePage();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ShowHistoryError(exception is HttpRequestException
                ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api."
                : exception is TaskCanceledException
                    ? "Сервер не ответил вовремя. Попробуйте ещё раз."
                    : exception.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ResetHistoryPeriod(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount) return;
        historyFrom = null;
        historyToExclusive = null;
        periodTransactions.Clear();
        HistoryFromDate.SelectedDate = null;
        HistoryToDate.SelectedDate = null;
        HistoryPeriodError.Visibility = Visibility.Collapsed;
        if (page == "Операции") UpdatePage();
    }

    private void ShowHistoryError(string message)
    {
        HistoryPeriodError.Text = message;
        HistoryPeriodError.Visibility = Visibility.Visible;
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
        if (loading || savingAccount) return;
        var today = DateTime.Today;
        var year = today.Year;
        var month = today.Month;
        if (page == "Бюджеты" && !TryReadBudgetPeriod(out year, out month)) return;
        await OpenBudgetForm(year, month);
    }

    private async void EditBudget(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount || sender is not Button { Tag: BudgetSummaryDto budget }) return;
        await OpenBudgetForm(budget.Year, budget.Month, budget.CategoryId);
    }

    private Task<BudgetReportDto[]> GetBudgets(int year, int month) =>
        api.GetAsync<BudgetReportDto[]>(FormattableString.Invariant($"budgets?year={year}&month={month}"));

    private static string BudgetPeriodText(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));

    private bool TryReadBudgetPeriod(out int year, out int month)
    {
        year = 0;
        month = BudgetMonthBox.SelectedValue is int selectedMonth ? selectedMonth : 0;
        if (month < 1 || month > 12 ||
            !int.TryParse(BudgetYearBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
            year < 1 || year > 9999)
        {
            ShowBudgetError("Выберите месяц и укажите год от 1 до 9999.");
            return false;
        }
        if (year == 9999 && month == 12)
        {
            ShowBudgetError("Выберите более ранний месяц.");
            return false;
        }
        return true;
    }

    private async void ApplyBudgetPeriod(object sender, RoutedEventArgs e)
    {
        if (loading || savingAccount) return;
        BudgetPeriodError.Visibility = Visibility.Collapsed;
        if (!TryReadBudgetPeriod(out var year, out var month)) return;

        SetLoading(true);
        try
        {
            var budgets = await GetBudgets(year, month);
            SetSelectedBudgetMonth(budgets, year, month);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ShowBudgetError(exception is HttpRequestException
                ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api."
                : exception is TaskCanceledException
                    ? "Сервер не ответил вовремя. Попробуйте ещё раз."
                    : exception.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ShowBudgetError(string message)
    {
        BudgetPeriodError.Text = message;
        BudgetPeriodError.Visibility = Visibility.Visible;
    }

    private void SetSelectedBudgetMonth(BudgetReportDto[] budgets, int year, int month)
    {
        selectedMonthBudgets = budgets;
        budgetYear = year;
        budgetMonth = month;
        BudgetMonthBox.SelectedValue = month;
        BudgetYearBox.Text = year.ToString(CultureInfo.InvariantCulture);
        BudgetPeriodError.Visibility = Visibility.Collapsed;
        if (page == "Бюджеты")
        {
            ShowBudgets(budgets, year, month);
            PeriodLabel.Text = BudgetPeriodText(year, month);
        }
    }

    private void ShowBudgets(BudgetReportDto[] budgets, int year, int month)
    {
        BudgetTitle.Text = "Бюджеты — " + BudgetPeriodText(year, month);
        BudgetList.ItemsSource = budgets.Select(b => new
        {
            b.Budget,
            Category = b.CategoryName,
            Summary = $"{Money(b.Budget.Spent)} из {Money(b.Budget.Limit)}",
            Percent = Math.Min(100m, b.Budget.Spent / b.Budget.Limit * 100m),
            BarColor = b.Budget.Status == BudgetStatus.Exceeded ? "#B65539" : "#805EFF",
            Status = b.Budget.Remaining < 0 ? "Превышение: " + Money(-b.Budget.Remaining) : "Осталось: " + Money(b.Budget.Remaining)
        }).ToArray();
        BudgetEmptyLabel.Visibility = budgets.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task OpenBudgetForm(int year, int month, Guid? categoryId = null)
    {
        var expenseCategories = categories.Where(c => c.Type == TransactionType.Expense).ToArray();
        if (expenseCategories.Length == 0) { MessageBox.Show(this, "Сначала создайте категорию расходов."); return; }
        if (categoryId.HasValue && expenseCategories.All(c => c.Id != categoryId.Value)) return;

        BudgetReportDto[] budgets;
        SetLoading(true);
        try
        {
            budgets = await GetBudgets(year, month);
            if (page == "Бюджеты") SetSelectedBudgetMonth(budgets, year, month);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var message = exception is HttpRequestException ? "Сервер недоступен. Проверьте запуск PersonalFinance.Api." : exception.Message;
            if (page == "Бюджеты") ShowBudgetError(message);
            else MessageBox.Show(this, message, "Не удалось загрузить бюджет", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            SetLoading(false);
        }

        var form = new EntryWindow("Бюджет — " + BudgetPeriodText(year, month)) { Owner = this };
        var choices = expenseCategories.Select(c => new Choice(c.Name, c.Id)).ToArray();
        var categoryBox = form.AddChoices("category", "Категория", choices);
        form.AddText("amount", "Месячный лимит, ₽");
        void FillLimit()
        {
            var id = form.Selected<Guid>("category");
            var existing = budgets.FirstOrDefault(b => b.Budget.CategoryId == id);
            form.SetText("amount", existing?.Budget.Limit.ToString("0.00", CultureInfo.GetCultureInfo("ru-RU")) ?? "");
        }
        categoryBox.SelectionChanged += (_, _) => FillLimit();
        if (categoryId.HasValue)
        {
            categoryBox.SelectedItem = choices.Single(choice => (Guid)choice.Value == categoryId.Value);
            categoryBox.IsEnabled = false;
        }
        FillLimit();
        var submitting = false;
        form.Closing += (_, args) => args.Cancel = submitting;
        form.Submit = async () =>
        {
            submitting = true;
            try
            {
                var id = form.Selected<Guid>("category");
                var path = FormattableString.Invariant($"budgets/{id}/{year}/{month}");
                await api.SendAsync<BudgetDto>(path, new SetBudgetRequest(id, year, month, ParseMoney(form.Text("amount"))), HttpMethod.Put);
            }
            finally
            {
                submitting = false;
            }
        };
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
