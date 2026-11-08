using DollarShark.Domain;

namespace DollarShark.Application.Categories;

/// <summary>
/// Common Canadian merchants a user can add with one click. They are copied into the user's own rules,
/// so every one of them can be edited or deleted afterwards. Category IDs are the shared default categories
/// seeded by migration (see CategorySeedData); a test keeps the two in sync.
/// </summary>
public static class StarterCategoryRules
{
    public static readonly Guid Groceries = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Restaurants = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Transportation = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid Utilities = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid Shopping = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid Subscriptions = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid HealthAndFitness = Guid.Parse("10000000-0000-0000-0000-000000000008");
    public static readonly Guid Travel = Guid.Parse("10000000-0000-0000-0000-000000000009");
    public static readonly Guid Salary = Guid.Parse("10000000-0000-0000-0000-000000000010");
    public static readonly Guid Transfers = Guid.Parse("10000000-0000-0000-0000-000000000012");

    public sealed record StarterRule(string Pattern, CategoryRuleMatchType MatchType, Guid CategoryId);

    private static StarterRule C(string pattern, Guid categoryId) => new(pattern, CategoryRuleMatchType.Contains, categoryId);
    private static StarterRule S(string pattern, Guid categoryId) => new(pattern, CategoryRuleMatchType.StartsWith, categoryId);

    // Patterns are deliberately specific to avoid false matches (e.g. "PETRO-CANADA", not "PETRO";
    // "SHELL"/"ESSO"/"IGA" only at the start so "SEASHELL"/"ESPRESSO" don't match). When two rules match, the longer one wins,
    // so "UBER EATS" → Restaurants beats "UBER" → Transportation and "AMAZON PRIME" → Subscriptions beats "AMAZON".
    public static readonly IReadOnlyList<StarterRule> All =
    [
        // Groceries
        C("SUPERSTORE", Groceries), C("LOBLAWS", Groceries), C("NO FRILLS", Groceries), C("NOFRILLS", Groceries),
        C("SOBEYS", Groceries), C("FOOD BASICS", Groceries), C("FRESHCO", Groceries), C("SAFEWAY", Groceries),
        C("FARM BOY", Groceries), C("LONGO", Groceries), C("T&T SUPERMARKET", Groceries), C("COSTCO", Groceries),
        C("ZEHRS", Groceries), C("FORTINOS", Groceries), C("WHOLE FOODS", Groceries), C("SAVE ON FOODS", Groceries),
        C("SAVE-ON-FOODS", Groceries), S("IGA", Groceries),

        // Restaurants & food delivery
        C("TIM HORTONS", Restaurants), C("STARBUCKS", Restaurants), C("MCDONALD", Restaurants), C("SUBWAY", Restaurants),
        C("UBER EATS", Restaurants), C("UBEREATS", Restaurants), C("DOORDASH", Restaurants), C("SKIPTHEDISHES", Restaurants),
        C("PIZZA PIZZA", Restaurants), C("DOMINO", Restaurants), C("CHIPOTLE", Restaurants), C("POPEYES", Restaurants),
        C("BURGER KING", Restaurants), C("SECOND CUP", Restaurants), C("HARVEY'S", Restaurants), C("SWISS CHALET", Restaurants),

        // Transportation & fuel
        C("UBER", Transportation), C("LYFT", Transportation), C("PRESTO", Transportation), C("PETRO-CANADA", Transportation),
        C("PETRO CANADA", Transportation), S("ESSO", Transportation), S("SHELL", Transportation), C("HUSKY", Transportation),
        C("PIONEER", Transportation), C("ULTRAMAR", Transportation), C("IMPARK", Transportation), C("GREEN P", Transportation),

        // Shopping
        C("WALMART", Shopping), C("AMAZON", Shopping), C("AMZN", Shopping), C("CANADIAN TIRE", Shopping),
        C("BEST BUY", Shopping), C("IKEA", Shopping), C("WINNERS", Shopping), C("HOME DEPOT", Shopping),
        C("DOLLARAMA", Shopping), C("HUDSON'S BAY", Shopping), C("SPORT CHEK", Shopping), C("STAPLES", Shopping),

        // Subscriptions
        C("NETFLIX", Subscriptions), C("SPOTIFY", Subscriptions), C("DISNEY PLUS", Subscriptions), C("DISNEYPLUS", Subscriptions),
        C("AMAZON PRIME", Subscriptions), C("PRIME VIDEO", Subscriptions), C("APPLE.COM/BILL", Subscriptions),
        C("YOUTUBE PREMIUM", Subscriptions),

        // Phone, internet & utilities
        C("ROGERS", Utilities), C("BELL CANADA", Utilities), C("TELUS", Utilities), C("FIDO", Utilities),
        C("KOODO", Utilities), C("FREEDOM MOBILE", Utilities), C("VIRGIN PLUS", Utilities), C("HYDRO ONE", Utilities),
        C("TORONTO HYDRO", Utilities), C("ENBRIDGE", Utilities), C("FORTISBC", Utilities),

        // Health & fitness
        C("SHOPPERS DRUG MART", HealthAndFitness), C("REXALL", HealthAndFitness), C("GOODLIFE", HealthAndFitness),
        C("PLANET FITNESS", HealthAndFitness),

        // Travel
        C("AIR CANADA", Travel), C("WESTJET", Travel), C("PORTER AIRLINES", Travel), C("AIRBNB", Travel),
        C("EXPEDIA", Travel), C("BOOKING.COM", Travel), C("VIA RAIL", Travel),

        // Income & card payments
        C("PAYROLL", Salary),
        C("PAYMENT RECEIVED - THANK YOU", Transfers), C("PAYMENT - THANK YOU", Transfers), C("PAYMENT THANK YOU", Transfers),
    ];
}
