using System.Text;
using DollarShark.Application.Statements;
using DollarShark.Infrastructure.Statements;

namespace DollarShark.Tests;

public sealed class StatementCsvParserTests
{
    private readonly StatementCsvParser parser = new();

    [Fact]
    public async Task ParsesUtf8BomQuotedSignedAmountsAndOptionalFields()
    {
        const string csv = "Transaction Date,Posted Date,Description,Amount,Currency,Reference Number\r\n" +
            "2026-01-05,2026-01-06,\"Coffee, \"\"The Good\"\" Shop\",-$12.34,CAD,REF-7\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

        var preview = await Parse(bytes);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(2, candidate.SourceRowNumber);
        Assert.Equal(new DateOnly(2026, 1, 5), candidate.TransactionDate);
        Assert.Equal(new DateOnly(2026, 1, 6), candidate.PostedDate);
        Assert.Equal("Coffee, \"The Good\" Shop", candidate.Description);
        Assert.Equal(-12.34m, candidate.Amount);
        Assert.Equal("CAD", candidate.Currency);
        Assert.Equal("REF-7", candidate.ReferenceNumber);
        Assert.Equal("Valid", candidate.Status);
        Assert.Equal(1, preview.ValidRows);
    }

    [Fact]
    public async Task MapsSeparateDebitAndCreditColumnsToExpenseAndIncomeSigns()
    {
        const string csv = "Date,Description,Debit,Credit\n2026-01-05,Groceries,25.70,\n2026-01-06,Paycheck,,1200.00\n";

        var preview = await Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(-25.70m, preview.Candidates[0].Amount);
        Assert.Equal(1200.00m, preview.Candidates[1].Amount);
        Assert.Equal(2, preview.ValidRows);
    }

    [Fact]
    public async Task DetectsSemicolonDelimitedCsvFiles()
    {
        const string csv = "Date;Description;Amount\r\n2026-01-05;Coffee;-4.25\r\n";

        var preview = await Parse(Encoding.UTF8.GetBytes(csv));

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("Valid", candidate.Status);
        Assert.Equal(new DateOnly(2026, 1, 5), candidate.TransactionDate);
        Assert.Equal(-4.25m, candidate.Amount);
    }

    [Fact]
    public async Task InvalidDatesAmountsAndCurrenciesReturnInvalidRowDiagnostics()
    {
        const string csv = "Date,Description,Amount,Currency\nnot-a-date,Coffee,twelve,US\n2026-01-05,Refund,5.00,USD\n";

        var preview = await Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(2, preview.Candidates.Count);
        Assert.Equal("Invalid", preview.Candidates[0].Status);
        Assert.Equal(3, preview.Candidates[0].ValidationMessages.Count);
        Assert.Equal("Valid", preview.Candidates[1].Status);
        Assert.Equal(1, preview.InvalidRows);
    }

    [Fact]
    public async Task EmptyOptionalsAreAcceptedAndMalformedFieldCountIsAnInvalidCandidate()
    {
        const string csv = "Date,Description,Amount,Posted Date,Currency,Reference Number\n" +
            "2026-01-05,Coffee,-4.20,,,\n" +
            "2026-01-06,Malformed,Extra,4.20\n";

        var preview = await Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(2, preview.Candidates.Count);
        Assert.Equal("Valid", preview.Candidates[0].Status);
        Assert.Null(preview.Candidates[0].PostedDate);
        Assert.Null(preview.Candidates[0].Currency);
        Assert.Equal("Invalid", preview.Candidates[1].Status);
        Assert.Contains("fields", Assert.Single(preview.Candidates[1].ValidationMessages));
    }

    [Fact]
    public async Task EmptyCsvMissingColumnsAndAmbiguousColumnsHaveClearErrors()
    {
        var empty = await Assert.ThrowsAsync<StatementCsvFormatException>(() => Parse(Encoding.UTF8.GetBytes("")));
        Assert.Contains("empty", empty.Message, StringComparison.OrdinalIgnoreCase);

        var missing = await Assert.ThrowsAsync<StatementCsvFormatException>(() => Parse(Encoding.UTF8.GetBytes("Date,Description\n")));
        Assert.Contains("amount", missing.Message, StringComparison.OrdinalIgnoreCase);

        var ambiguous = await Assert.ThrowsAsync<StatementCsvFormatException>(() =>
            Parse(Encoding.UTF8.GetBytes("Date,Transaction Date,Description,Amount\n")));
        Assert.Contains("more than one", ambiguous.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsMalformedQuotedCsvAndRowLimitOverflow()
    {
        var malformed = await Parse(Encoding.UTF8.GetBytes("Date,Description,Amount\n2026-01-01,\"unfinished,2.00\n"));
        Assert.Equal("Invalid", Assert.Single(malformed.Candidates).Status);
        Assert.Contains("fields", Assert.Single(malformed.Candidates[0].ValidationMessages), StringComparison.OrdinalIgnoreCase);

        const string csv = "Date,Description,Amount\n2026-01-01,A,1.00\n2026-01-02,B,2.00\n";
        await Assert.ThrowsAsync<StatementCsvRowLimitException>(() => Parse(Encoding.UTF8.GetBytes(csv), maxRows: 1));
    }

    private Task<StatementCsvPreview> Parse(byte[] bytes, int maxRows = 100) =>
        parser.ParseAsync(new MemoryStream(bytes), maxRows, CancellationToken.None);
}
