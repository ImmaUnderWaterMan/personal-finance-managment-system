using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PersonalFinance.Wpf;

public partial class MainWindow : Window
{
    private readonly List<FinanceTransaction> transactions = [];



    private readonly Dictionary<string, decimal> openingBalances = new()
    {
        ["Основной счёт"] = 24500m,
        ["Накопления"] = 120000m,
        ["Наличные"] = 5000m
    };



    private readonly Dictionary<string, decimal> budgetLimits = new()
    {
        ["Продукты"] = 15000m,
        ["Транспорт"] = 5000m,
        ["Развлечения"] = 7000m
    };



    private string page = "Обзор";

    public MainWindow()
    {
        InitializeComponent();

        var today = DateTime.Today;
        transactions.AddRange([
            new(today.AddDays(-Math.Min(today.Day - 1, 3)), "Заработная плата", "Зарплата", "Основной счёт", 85000m, true),
            new(today, "Покупки в супермаркете", "Продукты", "Основной счёт", 3450m, false),
            new(today, "Проездной билет", "Транспорт", "Основной счёт", 2800m, false),
            new(today.AddDays(-Math.Min(today.Day - 1, 1)), "Кино и ужин", "Развлечения", "Основной счёт", 2400m, false),
            new(today.AddDays(-Math.Min(today.Day - 1, 2)), "Продукты на неделю", "Продукты", "Наличные", 4250m, false)
        ]);

        Refresh();
    }

    internal static string Money(decimal amount) =>
        amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    private IEnumerable<FinanceTransaction> MonthTransactions =>
        transactions.Where(t =>
            t.Date.Year == DateTime.Today.Year &&
            t.Date.Month == DateTime.Today.Month);

    private void Refresh()
    {
        PeriodLabel.Text = DateTime.Today.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
        BalanceLabel.Text = Money(openingBalances.Values.Sum() + transactions.Sum(t => t.IsIncome ? t.Amount : -t.Amount));
        IncomeLabel.Text = Money(MonthTransactions.Where(t => t.IsIncome).Sum(t => t.Amount));
        ExpenseLabel.Text = Money(MonthTransactions.Where(t => !t.IsIncome).Sum(t => t.Amount));

        AccountsList.ItemsSource = openingBalances.Select(a => new
        {
            Name = a.Key,
            BalanceText = Money(a.Value + transactions.Where(t => t.Account == a.Key).Sum(t => t.IsIncome ? t.Amount : -t.Amount))
        }).ToList();

        BudgetList.ItemsSource = budgetLimits.Select(b =>
        {
            var spent = MonthTransactions.Where(t => !t.IsIncome && t.Category == b.Key).Sum(t => t.Amount);
            return new
            {
                Category = b.Key,
                Summary = $"{Money(spent)} из {Money(b.Value)}",
                Percent = Math.Min(100m, spent / b.Value * 100m),
                BarColor = spent > b.Value ? "#B65539" : "#805EFF",
                Status = spent > b.Value ? "Превышение: " + Money(spent - b.Value) : "Осталось: " + Money(b.Value - spent)
            };
        }).ToList();

        ReportList.ItemsSource = MonthTransactions.Where(t => !t.IsIncome)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(g => g.Amount)
            .Select(g => new { g.Category, AmountText = Money(g.Amount) }).ToList();

        ReportTotal.Text = "Разница доходов и расходов за месяц: " + Money(MonthTransactions.Sum(t => t.IsIncome ? t.Amount : -t.Amount));

        var income = MonthTransactions.Where(t => t.IsIncome).Sum(t => t.Amount);
        var expenses = MonthTransactions.Where(t => !t.IsIncome).Sum(t => t.Amount);
        SavingsGauge.Value = income > 0 ? (double)Math.Clamp((income - expenses) / income * 100m, 0m, 100m) : 0;

        var budgetSpent = MonthTransactions.Where(t => !t.IsIncome && budgetLimits.ContainsKey(t.Category)).Sum(t => t.Amount);
        BudgetGauge.Value = (double)(budgetSpent / budgetLimits.Values.Sum() * 100m);

        var categories = MonthTransactions.Where(t => !t.IsIncome)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(g => g.Amount).ToList();
        var maxExpense = categories.Count > 0 ? categories.Max(t => t.Amount) : 1m;

        ChartList.ItemsSource = categories.Select((g, i) => new
        {
            g.Category,
            AmountText = Money(g.Amount),
            Height = (double)(g.Amount / maxExpense * 115m),
            Color = i == 0 ? "#805EFF" : "#A0AFDA"
        }).ToList();

        FilterTransactions();
        UpdatePage();
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
            button.Background = button.Tag.ToString() == page
                ? new SolidColorBrush(Color.FromRgb(128, 94, 255))
                : Brushes.Transparent;
            button.Foreground = button.Tag.ToString() == page
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(64, 72, 103));
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (OperationsGrid is not null)
            FilterTransactions();
    }

    private void FilterTransactions()
    {
        var query = SearchBox.Text.Trim();
        var filtered = transactions.Where(t => $"{t.Description} {t.Category} {t.Account}".Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(t => t.Date).ToList();
        OperationsGrid.ItemsSource = filtered;
        EmptyLabel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddTransaction(object sender, RoutedEventArgs e)
    {
        var dialog = new TransactionWindow(openingBalances.Keys.ToArray()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        transactions.Insert(0, dialog.Result);

        Refresh();
    }
}

public record FinanceTransaction(
    DateTime Date,
    string Description,
    string Category,
    string Account,
    decimal Amount,
    bool IsIncome)
{
    public string DateText => Date.ToString("dd.MM.yyyy");
    public string AmountText => (IsIncome ? "+ " : "− ") + MainWindow.Money(Amount);
}
