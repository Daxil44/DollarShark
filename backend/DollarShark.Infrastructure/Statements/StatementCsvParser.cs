using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using DollarShark.Application.Statements;

namespace DollarShark.Infrastructure.Statements;

public sealed class StatementCsvParser : IStatementCsvParser
{
    private static readonly string[] TransactionDateHeaders = ["transaction date", "transactiondate", "trans date", "transdate", "date"];
    private static readonly string[] PostedDateHeaders = ["posted date", "posteddate", "posting date", "postingdate", "post date", "postdate"];
    private static readonly string[] DescriptionHeaders = ["description", "transaction description", "transactiondescription", "details", "memo", "merchant", "payee", "narrative"];
    private static readonly string[] SignedAmountHeaders = ["amount", "transaction amount", "transactionamount", "signed amount", "signedamount"];
    private static readonly string[] DebitHeaders = ["debit", "debit amount", "debitamount", "withdrawal", "withdrawals", "money out"];
    private static readonly string[] CreditHeaders = ["credit", "credit amount", "creditamount", "deposit", "deposits", "money in"];
    private static readonly string[] CurrencyHeaders = ["currency", "currency code", "currencycode"];
    private static readonly string[] ReferenceHeaders = ["reference", "reference number", "referencenumber", "reference id", "referenceid", "transaction id", "transactionid", "check number", "checknumber", "ref no"];
    private static readonly string[] DateFormats = [
        "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy",
        "yyyyMMdd", "d-MMM-yyyy", "dd-MMM-yyyy", "MMM d, yyyy", "MMMM d, yyyy"
    ];
    private static readonly Regex NumericAmount = new(
        @"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d{1,4})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<StatementCsvPreview> ParseAsync(Stream content, int maxRows, CancellationToken cancellationToken)
    {
        if (maxRows < 1) throw new ArgumentOutOfRangeException(nameof(maxRows));

        using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t", "|"],
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = _ => throw new StatementCsvFormatException("The CSV contains malformed quoted data."),
            MissingFieldFound = null
        };

        try
        {
            using var csv = new CsvReader(reader, configuration);
            if (!await csv.ReadAsync()) throw new StatementCsvFormatException("The CSV file is empty.");
            csv.ReadHeader();
            var headers = csv.HeaderRecord;
            if (headers is null || headers.Length == 0 || headers.All(string.IsNullOrWhiteSpace))
                throw new StatementCsvFormatException("The CSV header row is empty.");

            var mapping = ResolveColumns(headers);
            var candidates = new List<StatementCsvCandidate>();
            var dataRows = 0;
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++dataRows > maxRows) throw new StatementCsvRowLimitException(maxRows);
                var rowNumber = Math.Max(csv.Parser.Row, 2);
                if (csv.Parser.Count != headers.Length)
                {
                    candidates.Add(InvalidRow(rowNumber,
                        $"This row has {csv.Parser.Count} fields; the header has {headers.Length}."));
                    continue;
                }
                candidates.Add(ParseRow(csv, mapping, rowNumber));
            }

            return new StatementCsvPreview(candidates, candidates.Count(row => row.Status == "Valid"), candidates.Count(row => row.Status == "Invalid"));
        }
        catch (StatementCsvFormatException)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            throw new StatementCsvFormatException("The CSV must be valid UTF-8 text.");
        }
        catch (CsvHelperException)
        {
            throw new StatementCsvFormatException("The CSV is malformed. Check its quoting and row structure.");
        }
    }

    private static ColumnMapping ResolveColumns(string[] headers)
    {
        var date = Resolve(headers, TransactionDateHeaders, "transaction date");
        var description = Resolve(headers, DescriptionHeaders, "description");
        var amount = Resolve(headers, SignedAmountHeaders, "signed amount", required: false);
        var debit = Resolve(headers, DebitHeaders, "debit", required: false);
        var credit = Resolve(headers, CreditHeaders, "credit", required: false);
        var postedDate = Resolve(headers, PostedDateHeaders, "posted date", required: false);
        var currency = Resolve(headers, CurrencyHeaders, "currency", required: false);
        var reference = Resolve(headers, ReferenceHeaders, "reference number", required: false);

        if (amount is not null && (debit is not null || credit is not null))
            throw new StatementCsvFormatException("The CSV contains both signed amount and debit/credit columns. Keep only one amount format so the mapping is unambiguous.");
        if (amount is null && debit is null && credit is null)
            throw new StatementCsvFormatException("Required columns are missing. Provide Amount, or both Debit and Credit columns.");
        if (amount is null && (debit is null || credit is null))
            throw new StatementCsvFormatException("Separate amount format requires both Debit and Credit columns.");

        return new ColumnMapping(date!.Value, postedDate, description!.Value, amount, debit, credit, currency, reference);
    }

    private static int? Resolve(string[] headers, string[] aliases, string fieldName, bool required = true)
    {
        var normalizedAliases = aliases.Select(NormalizeHeader).ToHashSet(StringComparer.Ordinal);
        var matches = headers.Select((header, index) => (header, index))
            .Where(item => normalizedAliases.Contains(NormalizeHeader(item.header)))
            .Select(item => item.index)
            .ToArray();
        if (matches.Length > 1)
            throw new StatementCsvFormatException($"The CSV has more than one possible {fieldName} column. Rename or remove the duplicate columns.");
        if (matches.Length == 0 && required)
            throw new StatementCsvFormatException($"A {fieldName} column could not be identified from the CSV headers.");
        return matches.Length == 0 ? null : matches[0];
    }

    private static StatementCsvCandidate ParseRow(CsvReader csv, ColumnMapping mapping, int rowNumber)
    {
        var errors = new List<string>();
        var rawDate = Field(csv, mapping.TransactionDate);
        var transactionDate = ParseDate(rawDate);
        if (transactionDate is null) errors.Add(string.IsNullOrWhiteSpace(rawDate) ? "Transaction date is required." : "Transaction date has an unsupported or invalid format.");

        DateOnly? postedDate = null;
        if (mapping.PostedDate is { } postedIndex)
        {
            var rawPostedDate = Field(csv, postedIndex);
            if (!string.IsNullOrWhiteSpace(rawPostedDate))
            {
                postedDate = ParseDate(rawPostedDate);
                if (postedDate is null) errors.Add("Posted date has an unsupported or invalid format.");
            }
        }

        var description = Field(csv, mapping.Description)?.Trim() ?? string.Empty;
        if (description.Length == 0) errors.Add("Description is required.");

        decimal? amount;
        if (mapping.Amount is { } amountIndex)
        {
            var rawAmount = Field(csv, amountIndex);
            amount = ParseAmount(rawAmount);
            if (amount is null) errors.Add(string.IsNullOrWhiteSpace(rawAmount) ? "Amount is required." : "Amount has an unsupported format.");
        }
        else
        {
            var rawDebit = Field(csv, mapping.Debit!.Value);
            var rawCredit = Field(csv, mapping.Credit!.Value);
            var debit = ParseAmount(rawDebit);
            var credit = ParseAmount(rawCredit);
            if (!string.IsNullOrWhiteSpace(rawDebit) && debit is null) errors.Add("Debit amount has an unsupported format.");
            if (!string.IsNullOrWhiteSpace(rawCredit) && credit is null) errors.Add("Credit amount has an unsupported format.");
            if (string.IsNullOrWhiteSpace(rawDebit) && string.IsNullOrWhiteSpace(rawCredit))
                errors.Add("A debit or credit amount is required.");
            if (debit is not null && credit is not null && debit != 0 && credit != 0)
                errors.Add("A row cannot contain both a non-zero debit and a non-zero credit amount.");
            amount = debit is null && credit is null ? null
                : debit is { } debitValue && debitValue != 0 ? -Math.Abs(debitValue)
                : credit is { } creditValue ? Math.Abs(creditValue)
                : 0m;
        }

        var currency = mapping.Currency is { } currencyIndex ? NullIfBlank(Field(csv, currencyIndex))?.ToUpperInvariant() : null;
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetter)))
            errors.Add("Currency must be a three-letter code, such as CAD or USD.");
        var reference = mapping.Reference is { } referenceIndex ? NullIfBlank(Field(csv, referenceIndex)) : null;

        return new StatementCsvCandidate(rowNumber, transactionDate, postedDate, description, amount, currency, reference,
            errors, errors.Count == 0 ? "Valid" : "Invalid");
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
    }

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        var parenthesizedNegative = normalized.StartsWith('(') && normalized.EndsWith(')');
        if (parenthesizedNegative) normalized = normalized[1..^1].Trim();
        normalized = new string(normalized.Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.CurrencySymbol && !char.IsWhiteSpace(character)).ToArray());
        if (parenthesizedNegative) normalized = "-" + normalized.TrimStart('+', '-');
        if (!NumericAmount.IsMatch(normalized) ||
            !decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var amount) || amount > 999_999_999_999_999.9999m || amount < -999_999_999_999_999.9999m)
            return null;
        return amount;
    }

    private static string? Field(CsvReader csv, int index) => csv.GetField(index);
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeHeader(string? value) => new((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static StatementCsvCandidate InvalidRow(int rowNumber, string error) =>
        new(rowNumber, null, null, string.Empty, null, null, null, [error], "Invalid");

    private sealed record ColumnMapping(
        int TransactionDate, int? PostedDate, int Description, int? Amount, int? Debit, int? Credit,
        int? Currency, int? Reference);
}
