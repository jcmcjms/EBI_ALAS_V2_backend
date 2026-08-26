namespace EBI.ALAS.Api.Common.Constants;

/// <summary>
/// Role constants for the application.
/// </summary>
public static class Roles
{
    public const string Encoder = "Encoder";
    public const string Recommender = "Recommender";
    public const string Evaluator = "Evaluator";
    public const string Approver = "Approver";
    public const string Admin = "Admin";

    /// <summary>
    /// Role display names for documentation purposes.
    /// </summary>
    public static class DisplayNames
    {
        public const string Encoder = "Encoder (AO/CAA)";
        public const string Recommender = "Recommender (Branch Head)";
        public const string Evaluator = "Evaluator (Credit Checker)";
        public const string Approver = "Approver (Area Head)";
        public const string Admin = "Administrator";
    }
}
