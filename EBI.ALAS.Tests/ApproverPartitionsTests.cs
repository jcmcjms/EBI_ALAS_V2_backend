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
using Moq;
using Xunit;

namespace EBI.ALAS.Tests;

/// <summary>
/// Tests for the scope-aware approval partition logic and the enqueue guard.
/// Uses EF Core InMemory to exercise the real query path.
/// </summary>
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

        var loanRepo = new Mock<ILoanRepository>();
        loanRepo.Setup(r => r.GetUsersByRoleAndBranchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        var notifications = new Mock<INotificationService>();
        var realtime = new Mock<IRealtimeNotificationService>();
        var time = new Mock<ITimeProvider>();
        time.Setup(t => t.UtcNow).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var queueOptions = new Mock<IOptionsMonitor<QueueOptions>>();
        queueOptions.Setup(q => q.CurrentValue).Returns(new QueueOptions { LeaseTtlMinutes = 30 });

        _sut = new WorkflowQueueService(
            _db, loanRepo.Object, notifications.Object,
            realtime.Object, time.Object, queueOptions.Object);
    }

    public void Dispose() => _db.Dispose();

    // ── EnqueueAsync guard ─────────────────────────────────────────────────

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

    // ── GetDeskAsync — Global authority sees all branches ──────────────────

    [Fact]
    public async Task GetDeskAsync_GlobalApprover_SeesAllBranches()
    {
        // Seed branches
        _db.Branches.AddRange(
            new Branch { Code = "001", Name = "Branch 1" },
            new Branch { Code = "002", Name = "Branch 2" },
            new Branch { Code = "003", Name = "Branch 3" });
        await _db.SaveChangesAsync();

        // Seed authority (Tier 3, Global)
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

        // Seed user (home branch 001, but Global scope)
        _db.Users.Add(new User
        {
            Id = 10,
            Username = "credith",
            BranchId = "001",
            Role = Roles.Approver,
            ApprovalAuthorityKey = "CreditHead",
        });
        await _db.SaveChangesAsync();

        // Seed queue items in different branches
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

    // ── GetDeskAsync — Branch authority sees only home branch ─────────────

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

    // ── GetDeskAsync — Area authority sees covered branches ────────────────

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

        // Coverage: branches 001 and 002 (not 003)
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

    // ── GetDeskAsync — user with no authority gets empty desk ──────────────

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

    // ── GetDeskAsync — Recommender uses branch correctly ──────────────────

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
}