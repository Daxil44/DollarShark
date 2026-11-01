using DollarShark.Application.Common;
using DollarShark.Domain;
using DollarShark.Application.Transactions;

namespace DollarShark.Application.Transfers;

public sealed record TransferLinkRequest(Guid OutgoingTransactionId, Guid IncomingTransactionId);
public sealed record TransferPairDto(LinkedTransferTransactionDto Outgoing, LinkedTransferTransactionDto Incoming);
public sealed record TransferSuggestionDto(LinkedTransferTransactionDto Outgoing, LinkedTransferTransactionDto Incoming,
    int DaysApart, string MatchReason);

public enum TransferOperationState { Succeeded, NotFound, Invalid, Conflict }
public sealed record TransferOperationResult(TransferOperationState State, TransferPairDto? Pair = null,
    TransferPairError? ValidationError = null);

public interface ITransferRepository
{
    Task<TransferOperationResult> LinkAsync(Guid outgoingTransactionId, Guid incomingTransactionId, CancellationToken cancellationToken);
    Task<TransferOperationResult> UnlinkAsync(Guid transactionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransferSuggestionDto>> GetSuggestionsAsync(CancellationToken cancellationToken);
}

public interface ITransferService
{
    Task<UseCaseResult<TransferPairDto>> LinkAsync(TransferLinkRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<bool>> UnlinkAsync(Guid transactionId, CancellationToken cancellationToken);
    Task<UseCaseResult<IReadOnlyList<TransferSuggestionDto>>> GetSuggestionsAsync(CancellationToken cancellationToken);
}

public sealed class TransferService(ITransferRepository repository) : ITransferService
{
    public async Task<UseCaseResult<TransferPairDto>> LinkAsync(TransferLinkRequest request, CancellationToken cancellationToken)
    {
        if (request.OutgoingTransactionId == Guid.Empty || request.IncomingTransactionId == Guid.Empty)
            return UseCaseResult<TransferPairDto>.Invalid(Error("transactionId", "Select two valid transactions."));
        if (request.OutgoingTransactionId == request.IncomingTransactionId)
            return UseCaseResult<TransferPairDto>.Invalid(Error("incomingTransactionId", "A transaction cannot be linked to itself."));

        var result = await repository.LinkAsync(request.OutgoingTransactionId, request.IncomingTransactionId, cancellationToken);
        if (result.ValidationError == TransferPairError.AlreadyLinked)
            return UseCaseResult<TransferPairDto>.Conflict("A transaction can belong to only one transfer pair. Unlink it first.");
        return result.State switch
        {
            TransferOperationState.Succeeded => UseCaseResult<TransferPairDto>.Ok(result.Pair!),
            TransferOperationState.NotFound => UseCaseResult<TransferPairDto>.NotFound("One or both transactions were not found."),
            TransferOperationState.Conflict => UseCaseResult<TransferPairDto>.Conflict("One or both transactions were linked by another request. Refresh and try again."),
            _ => UseCaseResult<TransferPairDto>.Invalid(ErrorFor(result.ValidationError ?? TransferPairError.NotTransfer))
        };
    }

    public async Task<UseCaseResult<bool>> UnlinkAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        if (transactionId == Guid.Empty) return UseCaseResult<bool>.Invalid(Error("transactionId", "A valid transaction ID is required."));
        var result = await repository.UnlinkAsync(transactionId, cancellationToken);
        return result.State switch
        {
            TransferOperationState.Succeeded => UseCaseResult<bool>.NoContent(true),
            TransferOperationState.NotFound => UseCaseResult<bool>.NotFound("Transaction was not found."),
            TransferOperationState.Conflict => UseCaseResult<bool>.Conflict("The transfer relationship changed. Refresh and try again."),
            _ => UseCaseResult<bool>.Invalid(Error("transactionId", "This transaction is not currently linked to a transfer."))
        };
    }

    public async Task<UseCaseResult<IReadOnlyList<TransferSuggestionDto>>> GetSuggestionsAsync(CancellationToken cancellationToken) =>
        UseCaseResult<IReadOnlyList<TransferSuggestionDto>>.Ok(await repository.GetSuggestionsAsync(cancellationToken));

    private static IReadOnlyDictionary<string, string[]> ErrorFor(TransferPairError error) => error switch
    {
        TransferPairError.SelfLink => Error("incomingTransactionId", "A transaction cannot be linked to itself."),
        TransferPairError.DifferentOwner => Error("transactionId", "Both transactions must belong to your account."),
        TransferPairError.SameAccount => Error("incomingTransactionId", "Transfers must use two different financial accounts."),
        TransferPairError.NotTransfer => Error("transactionType", "Both transactions must be classified as transfers."),
        TransferPairError.AlreadyLinked => Error("transactionId", "A transaction can belong to only one transfer pair. Unlink it first."),
        TransferPairError.CurrencyMismatch => Error("currency", "The transactions and their accounts must use the same currency."),
        TransferPairError.AmountMismatch => Error("amount", "Transfer amounts must be nonzero and have the same absolute value."),
        TransferPairError.SameDirection => Error("amount", "One transfer amount must be negative and the other positive."),
        TransferPairError.ReversedDirection => Error("transactionId", "Choose the negative transfer as outgoing and the positive transfer as incoming."),
        _ => Error("transactionId", "The transactions cannot be linked as a transfer.")
    };

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message) =>
        new Dictionary<string, string[]> { [key] = [message] };
}
