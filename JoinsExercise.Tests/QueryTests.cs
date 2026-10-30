using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace JoinsExercise.Tests;

// Runs each of your .sql files against your own Northwind database and compares what it returns with the
// file's Expect line. Nothing is changed: every file runs inside a transaction that is rolled back.
// You don't need to change this file.
[TestClass]
public sealed class QueryTests
{
    // The NORTHWIND_CONNECTION environment variable, if set, points the tests at another database.
    private static readonly string connectionString =
        Environment.GetEnvironmentVariable("NORTHWIND_CONNECTION")
        ?? "Server=localhost;Database=Northwind;Integrated Security=True;TrustServerCertificate=True";

    // Runs one file (its GO lines split it into batches) and returns the last result it shows.
    private static DataTable Run(string file)
    {
        string sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Queries", file));
        var results = new List<DataTable>();
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (string batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var command = new SqlCommand(batch, connection, transaction);
            using var reader = command.ExecuteReader();
            while (!reader.IsClosed && reader.FieldCount > 0)
            {
                var result = new DataTable();
                result.Load(reader);   // reads one result, then moves on to the next
                results.Add(result);
            }
        }
        transaction.Rollback();
        Assert.IsNotEmpty(results, $"{file} shows no result yet. Write your query under the question.");
        return results[^1];
    }

    // True when one of the row's values is the text, or the row reads it across its values (a first and a
    // last name in two columns), so your column names and order don't matter.
    private static bool Has(DataRow row, string value) =>
        row.ItemArray.Any(cell => string.Equals(cell?.ToString(), value, StringComparison.Ordinal))
        || string.Join(" ", row.ItemArray.Select(cell => cell?.ToString())).Contains(value, StringComparison.Ordinal);

    // True when one of the row's values is the number: 263.5000 is the same answer as 263.50.
    private static bool Has(DataRow row, decimal value) =>
        row.ItemArray.Any(cell => cell is not null and not DBNull && decimal.TryParse(Convert.ToString(cell, CultureInfo.InvariantCulture),
            NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) && Math.Abs(number - value) < 0.005m);

    private static DataRow[] Rows(DataTable result) => result.Rows.Cast<DataRow>().ToArray();

    [TestMethod]
    public void Query01_ProductSuppliers_ListsEveryProductWithItsSupplier()
    {
        DataRow[] rows = Rows(Run("01_Product_Suppliers.sql"));

        Assert.HasCount(77, rows, "01: every product is listed once, so 77 rows.");
        Assert.IsTrue(Has(rows[0], "Alice Mutton") && Has(rows[0], "Pavlova, Ltd."),
            "01: sorted by product name, the first row is Alice Mutton, from Pavlova, Ltd.");
    }

    [TestMethod]
    public void Query02_BeverageSuppliers_ListsTheBeveragesDearestFirst()
    {
        DataRow[] rows = Rows(Run("02_Beverage_Suppliers.sql"));

        Assert.HasCount(12, rows, "02: Northwind has 12 beverages.");
        Assert.IsTrue(Has(rows[0], "Côte de Blaye") && Has(rows[0], 263.50m) && Has(rows[0], "Aux joyeux ecclésiastiques"),
            "02: the dearest first: Côte de Blaye, 263.50, from Aux joyeux ecclésiastiques.");
    }

    [TestMethod]
    public void Query03_OrdersPerEmployee_PutsMargaretPeacockFirst()
    {
        DataRow[] rows = Rows(Run("03_Orders_Per_Employee.sql"));

        Assert.HasCount(9, rows, "03: one row per employee, so 9 rows.");
        Assert.IsTrue(Has(rows[0], "Margaret") && Has(rows[0], "Peacock") && Has(rows[0], 156m),
            "03: most orders first: Margaret Peacock, with 156.");
    }

    [TestMethod]
    public void Query04_OrdersPerShipper_CountsEachShippersOrders()
    {
        DataRow[] rows = Rows(Run("04_Orders_Per_Shipper.sql"));

        Assert.HasCount(3, rows, "04: one row per shipper, so 3 rows.");
        Assert.IsTrue(Has(rows[0], "United Package") && Has(rows[0], 326m), "04: the first row is United Package, 326.");
        Assert.IsTrue(Has(rows[1], "Federal Shipping") && Has(rows[1], 255m), "04: the second row is Federal Shipping, 255.");
        Assert.IsTrue(Has(rows[2], "Speedy Express") && Has(rows[2], 249m), "04: the last row is Speedy Express, 249.");
    }

    [TestMethod]
    public void Query05_May2018Orders_ListsTheMonthsOrders()
    {
        DataRow[] rows = Rows(Run("05_May_2018_Orders.sql"));

        Assert.HasCount(14, rows, "05: 14 orders were placed in May 2018.");
        Assert.IsTrue(Has(rows[0], 11064m) && Has(rows[0], "Save-a-lot Markets") && Has(rows[0], "Davolio"),
            "05: the first row is order 11064, for Save-a-lot Markets, taken by Davolio.");
    }

    [TestMethod]
    public void Query06_CustomersWithoutOrders_FindsTheTwo()
    {
        DataRow[] rows = Rows(Run("06_Customers_Without_Orders.sql"));

        Assert.HasCount(2, rows, "06: two customers have never ordered. A LEFT JOIN keeps them; IS NULL finds them.");
        Assert.IsTrue(rows.Any(row => Has(row, "FISSA Fabrica Inter. Salchichas S.A.")), "06: FISSA Fabrica Inter. Salchichas S.A. is one of them.");
        Assert.IsTrue(rows.Any(row => Has(row, "Paris spécialités")), "06: Paris spécialités is the other.");
    }

    [TestMethod]
    public void Query07_SalesByCountry_ListsTheTopFiveBiggestFirst()
    {
        DataRow[] rows = Rows(Run("07_Sales_By_Country.sql"));

        Assert.HasCount(5, rows, "07: the top five countries, so 5 rows.");
        (string Country, decimal Sales)[] expected =
            [("USA", 263566.98m), ("Germany", 244640.63m), ("Austria", 139496.63m), ("Brazil", 114968.48m), ("France", 85498.76m)];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.IsTrue(Has(rows[i], expected[i].Country) && Has(rows[i], expected[i].Sales),
                $"07: row {i + 1} is {expected[i].Country}, {expected[i].Sales}.");
        }
    }

    [TestMethod]
    public void Query08_UkManagers_PairsEachEmployeeWithTheirManager()
    {
        DataRow[] rows = Rows(Run("08_UK_Managers.sql"));

        Assert.HasCount(4, rows, "08: four employees work in the UK.");
        Assert.IsTrue(Has(rows[0], "Steven") && Has(rows[0], "Buchanan") && Has(rows[0], "Andrew") && Has(rows[0], "Fuller"),
            "08: the first is Steven Buchanan, whose manager is Andrew Fuller. A self join pairs them.");
    }

    [TestMethod]
    public void Query09_AboveAverage_CountsTheProducts()
    {
        DataRow[] rows = Rows(Run("09_Above_Average.sql"));

        Assert.HasCount(1, rows, "09: one row, the count.");
        Assert.IsTrue(Has(rows[0], 25m), "09: 25 products cost more than the average.");
    }

    [TestMethod]
    public void Query10_ChaiCustomers_ListsEveryCustomerWhoBoughtChai()
    {
        DataRow[] rows = Rows(Run("10_Chai_Customers.sql"));

        Assert.HasCount(31, rows, "10: 31 customers bought Chai. Is each listed once?");
        Assert.IsTrue(Has(rows[0], "Berglunds snabbköp"), "10: sorted by name, the first is Berglunds snabbköp.");
    }

    [TestMethod]
    public void Query11_LateShipments_CountsOnTimeAndLatePerShipper()
    {
        DataRow[] rows = Rows(Run("11_Late_Shipments.sql"));

        Assert.HasCount(3, rows, "11: one row per shipper, so 3 rows.");
        Assert.IsTrue(Has(rows[0], "Federal Shipping") && Has(rows[0], 240m) && Has(rows[0], 9m), "11: Federal Shipping, 240 on time and 9 late.");
        Assert.IsTrue(Has(rows[1], "Speedy Express") && Has(rows[1], 233m) && Has(rows[1], 12m), "11: Speedy Express, 233 on time and 12 late.");
        Assert.IsTrue(Has(rows[2], "United Package") && Has(rows[2], 299m) && Has(rows[2], 16m), "11: United Package, 299 on time and 16 late.");
    }

    [TestMethod]
    public void Query12_BonusStockAlerts_NamesEachProductsAlert()
    {
        DataRow[] rows = Rows(Run("12_Bonus_Stock_Alerts.sql"));

        Assert.HasCount(3, rows, "12: three products need an alert.");
        Assert.IsTrue(Has(rows[0], "Gorgonzola Telino") && Has(rows[0], "Out of stock"), "12: Gorgonzola Telino is out of stock.");
        Assert.IsTrue(Has(rows[1], "Nord-Ost Matjeshering") && Has(rows[1], "Reorder"), "12: Nord-Ost Matjeshering needs a reorder.");
        Assert.IsTrue(Has(rows[2], "Outback Lager") && Has(rows[2], "Reorder"), "12: Outback Lager needs a reorder.");
    }
}
