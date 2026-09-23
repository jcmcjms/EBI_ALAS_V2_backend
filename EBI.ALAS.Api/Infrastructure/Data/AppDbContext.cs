using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.SystemSettings;

namespace EBI.ALAS.Api.Infrastructure.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<LoanAction> LoanActions => Set<LoanAction>();
    public DbSet<OutstandingLoan> OutstandingLoans => Set<OutstandingLoan>();
    public DbSet<BuyOut> BuyOuts => Set<BuyOut>();
    public DbSet<EbiReloan> EbiReloans => Set<EbiReloan>();
    public DbSet<IncomingLoan> IncomingLoans => Set<IncomingLoan>();
    public DbSet<LoanSubmissionIdempotency> LoanSubmissionIdempotencies => Set<LoanSubmissionIdempotency>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LoanDeviation> LoanDeviations => Set<LoanDeviation>();
    public DbSet<DeviationRemark> DeviationRemarks => Set<DeviationRemark>();
    public DbSet<DocumentRemark> DocumentRemarks => Set<DocumentRemark>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<LoanProductChecklist> LoanProductChecklists => Set<LoanProductChecklist>();
    public DbSet<ApprovalAuthority> ApprovalAuthorities => Set<ApprovalAuthority>();
    public DbSet<DeviationCatalogItem> DeviationCatalog => Set<DeviationCatalogItem>();
    public DbSet<UserBranchCoverage> UserBranchCoverages => Set<UserBranchCoverage>();
    public DbSet<WorkflowQueueItem> WorkflowQueueItems => Set<WorkflowQueueItem>();
    public DbSet<DocumentChecklist> DocumentChecklists => Set<DocumentChecklist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
