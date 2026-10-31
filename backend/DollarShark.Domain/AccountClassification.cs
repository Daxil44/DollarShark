namespace DollarShark.Domain;

/// <summary>
/// Describes the account-side balance convention independently from transaction reporting types.
/// Asset account balances are positive owned value; liability balances are positive amounts owed.
/// </summary>
public enum AccountClassification
{
    Asset,
    Liability
}
