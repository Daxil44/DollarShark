namespace DollarShark.Application.Statements;

public sealed class StatementFileTooLargeException(long maximumBytes)
    : Exception($"Statement file exceeds the configured limit of {maximumBytes} bytes.");
