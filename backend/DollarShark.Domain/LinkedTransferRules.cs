namespace DollarShark.Domain;

public enum TransferPairError
{
    SelfLink,
    DifferentOwner,
    SameAccount,
    NotTransfer,
    AlreadyLinked,
    CurrencyMismatch,
    AmountMismatch,
    SameDirection,
    ReversedDirection
}

public static class LinkedTransferRules
{
    public static TransferPairError? Validate(Transaction first, Transaction second)
    {
        if (first.Id == second.Id) return TransferPairError.SelfLink;
        if (string.IsNullOrWhiteSpace(first.UserId) || !string.Equals(first.UserId, second.UserId, StringComparison.Ordinal) ||
            !string.Equals(first.UserId, first.FinancialAccount.UserId, StringComparison.Ordinal) ||
            !string.Equals(second.UserId, second.FinancialAccount.UserId, StringComparison.Ordinal))
            return TransferPairError.DifferentOwner;
        if (first.FinancialAccountId == second.FinancialAccountId) return TransferPairError.SameAccount;
        if (first.TransactionType != TransactionType.Transfer || second.TransactionType != TransactionType.Transfer)
            return TransferPairError.NotTransfer;
        if (first.LinkedTransferTransactionId.HasValue || second.LinkedTransferTransactionId.HasValue)
            return TransferPairError.AlreadyLinked;
        if (!string.Equals(first.Currency, second.Currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(first.Currency, first.FinancialAccount.Currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(second.Currency, second.FinancialAccount.Currency, StringComparison.OrdinalIgnoreCase))
            return TransferPairError.CurrencyMismatch;
        if (first.Amount == 0 || second.Amount == 0 || decimal.Abs(first.Amount) != decimal.Abs(second.Amount))
            return TransferPairError.AmountMismatch;
        if (Math.Sign(first.Amount) == Math.Sign(second.Amount)) return TransferPairError.SameDirection;
        if (first.Amount > 0 || second.Amount < 0) return TransferPairError.ReversedDirection;
        return null;
    }
}
