namespace EBI.ALAS.Api.Common.Constants;
public static class Roles
{
    public const string Encoder = "Encoder";
    public const string Recommender = "Recommender";
    public const string Evaluator = "Evaluator";
    public const string Approver = "Approver";
    public const string Admin = "Admin";
    /// <summary>Least-privilege system actor — exactly one workflow edge:
    /// ForIncompleteDocuments → ForChecking (auto-return when documents sync).</summary>
    public const string System = "System";
    public static class DisplayNames
    {
        public const string Encoder = "Encoder (AO/CAA)";
        public const string Recommender = "Recommender (Branch Head)";
        public const string Evaluator = "Evaluator (Credit Checker)";
        public const string Approver = "Approver (Area Head)";
        public const string Admin = "Administrator";
        public const string System = "System (document sync)";
    }
}
