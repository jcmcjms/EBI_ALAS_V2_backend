using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
namespace EBI.ALAS.Tests;
public class ApproverPartitionsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WorkflowQueueService _sut;
    public ApproverPartitionsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var loanRepo = Substitute.For<ILoanRepository>();
        loanRepo.GetUsersByRoleAndBranchAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<User>());
        var notifications = Substitute.For<INotificationService>();
        var time = Substitute.For<ITimeProvider>();
        time.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var queueOptions = Substitute.For<IOptionsMonitor<QueueOptions>>();
        queueOptions.CurrentValue.Returns(new QueueOptions { LeaseTtlMinutes = 30 });
        _sut = new WorkflowQueueService(
            _db, loanRepo, notifications,
            time, queueOptions);
    }
    public void Dispose() => _db.Dispose();
    [Fact]
    public async Task EnqueueAsync_Approval_WithNullTier_Throws()
    {
        var loan = new LoanApplication
        {
            Id = 1, BranchCode = "007", RequiredApprovalTier = null,
            FirstName = "Test", LastName = "User"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnqueueAsync(loan, "ForApproval", CancellationToken.None));
    }
    [Fact]
    public async Task EnqueueAsync_Approval_WithTier_Succeeds()
    {
        var loan = new LoanApplication
        {
            Id = 2, BranchCode = "007", RequiredApprovalTier = 3,
            FirstName = "Test", LastName = "User"
        };
        await _sut.EnqueueAsync(loan, "ForApproval", CancellationToken.None);
        var item = await _db.WorkflowQueueItems.SingleAsync();
        Assert.Equal("APP:007:3", item.PartitionKey);
        Assert.Equal(QueueStage.Approval, item.Stage);
    }
    [Fact]
    public async Task GetDeskAsync_GlobalApprover_SeesAllBranches()
    {
        _db.Branches.AddRange(
            new Branch { Code = "001", Name = "Branch 1" },
            new Branch { Code = "002", Name = "Branch 2" },
            new Branch { Code = "003", Name = "Branch 3" });
        await _db.SaveChangesAsync();
        _db.ApprovalAuthorities.Add(new ApprovalAuthority
        {
            Key = "CreditHead",
            DisplayName = "Credit Head",
            Tier = 3,
            Priority = 1,
            AllowNew = true,
            AllowRenewal = true,
            MaxSeverity = DeviationSeverity.Major,
            MaxTotalExposure = 1_500_000m,
            ScopeType = AuthorityScope.Global,
        });
        _db.Users.Add(new User
        {
            Id = 10,
            Username = "credith",
            BranchId = "001",
            Role = Roles.Approver,
            ApprovalAuthorityKey = "CreditHead",
        });
        await _db.SaveChangesAsync();
        _db.WorkflowQueueItems.AddRange(
            new WorkflowQueueItem
            {
                LoanApplicationId = 101, Stage = QueueStage.Approval,
                PartitionKey = "APP:001:3", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 101, BranchCode = "001", RequiredApprovalTier = 3,
                    Status = "ForApproval", LamId = "LAM-001",
                    FirstName = "Alice", LastName = "Smith"
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 102, Stage = QueueStage.Approval,
                PartitionKey = "APP:002:3", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 102, BranchCode = "002", RequiredApprovalTier = 3,
                    Status = "ForApproval", LamId = "LAM-002",
                    FirstName = "Bob", LastName = "Jones"
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 103, Stage = QueueStage.Approval,
                PartitionKey = "APP:003:3", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 103, BranchCode = "003", RequiredApprovalTier = 3,
                    Status = "ForApproval", LamId = "LAM-003",
                    FirstName = "Carol", LastName = "Lee"
                }
            });
        await _db.SaveChangesAsync();
        var desk = await _sut.GetDeskAsync(10, Roles.Approver, "001", CancellationToken.None);
        Assert.Equal(3, desk.Items.Count);
        Assert.Contains("Global", desk.ScopeDescription);
        Assert.Contains("3 branches", desk.ScopeDescription);
    }
    [Fact]
    public async Task GetDeskAsync_BranchApprover_SeesOnlyHomeBranch()
    {
        _db.Branches.AddRange(
            new Branch { Code = "001", Name = "Branch 1" },
            new Branch { Code = "002", Name = "Branch 2" });
        await _db.SaveChangesAsync();
        _db.ApprovalAuthorities.Add(new ApprovalAuthority
        {
            Key = "BranchHead",
            DisplayName = "Branch Head",
            Tier = 1,
            Priority = 1,
            AllowNew = true,
            AllowRenewal = true,
            MaxSeverity = DeviationSeverity.Minor,
            MaxTotalExposure = 500_000m,
            ScopeType = AuthorityScope.Branch,
        });
        _db.Users.Add(new User
        {
            Id = 20,
            Username = "branchhead",
            BranchId = "001",
            Role = Roles.Approver,
            ApprovalAuthorityKey = "BranchHead",
        });
        await _db.SaveChangesAsync();
        _db.WorkflowQueueItems.AddRange(
            new WorkflowQueueItem
            {
                LoanApplicationId = 201, Stage = QueueStage.Approval,
                PartitionKey = "APP:001:1", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 201, BranchCode = "001", RequiredApprovalTier = 1,
                    Status = "ForApproval", LamId = "LAM-201",
                    FirstName = "Dave", LastName = "Kim"
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 202, Stage = QueueStage.Approval,
                PartitionKey = "APP:002:1", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 202, BranchCode = "002", RequiredApprovalTier = 1,
                    Status = "ForApproval", LamId = "LAM-202",
                    FirstName = "Eve", LastName = "Cruz"
                }
            });
        await _db.SaveChangesAsync();
        var desk = await _sut.GetDeskAsync(20, Roles.Approver, "001", CancellationToken.None);
        Assert.Single(desk.Items);
        Assert.Equal(201, desk.Items[0].LoanId);
        Assert.Contains("Branch 001", desk.ScopeDescription);
    }
    [Fact]
    public async Task GetDeskAsync_AreaApprover_SeesCoveredBranches()
    {
        _db.Branches.AddRange(
            new Branch { Code = "001", Name = "Branch 1" },
            new Branch { Code = "002", Name = "Branch 2" },
            new Branch { Code = "003", Name = "Branch 3" });
        await _db.SaveChangesAsync();
        _db.ApprovalAuthorities.Add(new ApprovalAuthority
        {
            Key = "AreaHead",
            DisplayName = "Area Head",
            Tier = 2,
            Priority = 1,
            AllowNew = true,
            AllowRenewal = true,
            MaxSeverity = DeviationSeverity.Minor,
            MaxTotalExposure = 800_000m,
            ScopeType = AuthorityScope.Area,
        });
        _db.Users.Add(new User
        {
            Id = 30,
            Username = "areahead",
            BranchId = "001",
            Role = Roles.Approver,
            ApprovalAuthorityKey = "AreaHead",
        });
        await _db.SaveChangesAsync();
        _db.UserBranchCoverages.AddRange(
            new UserBranchCoverage { UserId = 30, BranchCode = "001" },
            new UserBranchCoverage { UserId = 30, BranchCode = "002" });
        await _db.SaveChangesAsync();
        _db.WorkflowQueueItems.AddRange(
            new WorkflowQueueItem
            {
                LoanApplicationId = 301, Stage = QueueStage.Approval,
                PartitionKey = "APP:001:2", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 301, BranchCode = "001", RequiredApprovalTier = 2,
                    Status = "ForApproval", LamId = "LAM-301",
                    FirstName = "Frank", LastName = "Tan"
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 302, Stage = QueueStage.Approval,
                PartitionKey = "APP:002:2", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 302, BranchCode = "002", RequiredApprovalTier = 2,
                    Status = "ForApproval", LamId = "LAM-302",
                    FirstName = "Grace", LastName = "Lim"
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 303, Stage = QueueStage.Approval,
                PartitionKey = "APP:003:2", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 303, BranchCode = "003", RequiredApprovalTier = 2,
                    Status = "ForApproval", LamId = "LAM-303",
                    FirstName = "Hank", LastName = "Go"
                }
            });
        await _db.SaveChangesAsync();
        var desk = await _sut.GetDeskAsync(30, Roles.Approver, "001", CancellationToken.None);
        Assert.Equal(2, desk.Items.Count);
        Assert.Contains("001", desk.ScopeDescription);
        Assert.Contains("002", desk.ScopeDescription);
    }
    [Fact]
    public async Task GetDeskAsync_NoAuthority_ReturnsEmptyWithMessage()
    {
        _db.Users.Add(new User
        {
            Id = 40,
            Username = "noauth",
            BranchId = "001",
            Role = Roles.Approver,
            ApprovalAuthorityKey = null,
        });
        await _db.SaveChangesAsync();
        var desk = await _sut.GetDeskAsync(40, Roles.Approver, "001", CancellationToken.None);
        Assert.Empty(desk.Items);
        Assert.Contains("No authority", desk.ScopeDescription);
    }
    [Fact]
    public async Task GetDeskAsync_Recommender_SeesOwnBranchOnly()
    {
        _db.WorkflowQueueItems.AddRange(
            new WorkflowQueueItem
            {
                LoanApplicationId = 401, Stage = QueueStage.Recommendation,
                PartitionKey = "REC:001", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 401, BranchCode = "001", RequiredApprovalTier = null,
                    Status = "ForRecommendation", LamId = "LAM-401",
                    FirstName = "Ivy", LastName = "Santos"
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 402, Stage = QueueStage.Recommendation,
                PartitionKey = "REC:002", State = QueueItemState.Active,
                EnqueuedAt = DateTime.UtcNow,
                LoanApplication = new LoanApplication
                {
                    Id = 402, BranchCode = "002", RequiredApprovalTier = null,
                    Status = "ForRecommendation", LamId = "LAM-402",
                    FirstName = "Jake", LastName = "Reyes"
                }
            });
        await _db.SaveChangesAsync();
        var desk = await _sut.GetDeskAsync(50, Roles.Recommender, "001", CancellationToken.None);
        Assert.Single(desk.Items);
        Assert.Equal(401, desk.Items[0].LoanId);
    }
    [Fact]
    public async Task GetDeskAsync_IncludesQueuedBacklog()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.WorkflowQueueItems.AddRange(
            new WorkflowQueueItem
            {
                LoanApplicationId = 601, Stage = QueueStage.Evaluation,
                PartitionKey = "EVA:006", State = QueueItemState.Active,
                EnqueuedAt = t0,
                LoanApplication = new LoanApplication
                {
                    Id = 601, BranchCode = "006", LamId = "LAM-601",
                    Status = "ForChecking", FirstName = "Rejen", LastName = "Manliguis",
                    ProductCode = "A16", Product = "AFOS-RPSU 1-7YR",
                    ProposedAmount = 322_000, TermDays = 730,
                    ApplicationDate = t0,
                }
            },
            new WorkflowQueueItem
            {
                LoanApplicationId = 602, Stage = QueueStage.Evaluation,
                PartitionKey = "EVA:006", State = QueueItemState.Queued,
                EnqueuedAt = t0.AddMinutes(5),
                LoanApplication = new LoanApplication
                {
                    Id = 602, BranchCode = "006", LamId = "LAM-602",
                    Status = "ForChecking", FirstName = "Maria", LastName = "Cruz",
                    ProductCode = "A16", Product = "AFOS-RPSU 1-7YR",
                    ProposedAmount = 386_000, TermDays = 730,
                    ApplicationDate = t0.AddMinutes(5),
                }
            });
        await _db.SaveChangesAsync();
        var desk = await _sut.GetDeskAsync(99, Roles.Evaluator, "006", CancellationToken.None);
        Assert.Equal(2, desk.Items.Count);
        Assert.Equal(601, desk.Items[0].LoanId);
        Assert.True(desk.Items[0].IsHead);
        Assert.Equal(1, desk.Items[0].Position);
        Assert.Equal(602, desk.Items[1].LoanId);
        Assert.False(desk.Items[1].IsHead);
        Assert.Equal(2, desk.Items[1].Position);
        Assert.Equal("006", desk.Items[1].BranchCode);
        Assert.Equal(386_000, desk.Items[1].ProposedAmount);
    }
}
