using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class QueueDefaultContractTests
{
    [Fact]
    public void DefaultQueueStatuses_AreValidLoanStatusNames()
    {
        var names = QueueDefaults.Statuses;

        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            Assert.True(Enum.TryParse<LoanStatus>(name, out _), $"Unknown status '{name}'");
        }
    }

    [Fact]
    public void DefaultQueueStatuses_ExcludeTerminalStatuses()
    {
        var names = QueueDefaults.Statuses;

        Assert.DoesNotContain(nameof(LoanStatus.Rejected), names);
        Assert.DoesNotContain(nameof(LoanStatus.Cancelled), names);
        Assert.DoesNotContain(nameof(LoanStatus.Disbursed), names);
        Assert.DoesNotContain(nameof(LoanStatus.OnGoing), names);
    }

    [Fact]
    public void DefaultQueueStatuses_IncludeActivePipeline()
    {
        var names = QueueDefaults.Statuses;

        Assert.Contains(nameof(LoanStatus.ForRecommendation), names);
        Assert.Contains(nameof(LoanStatus.ForChecking), names);
        Assert.Contains(nameof(LoanStatus.ForApproval), names);
        Assert.Contains(nameof(LoanStatus.ForRevision), names);
        Assert.Contains(nameof(LoanStatus.ForIncompleteDocuments), names);
        Assert.Contains(nameof(LoanStatus.ForDisbursement), names);
    }
}
