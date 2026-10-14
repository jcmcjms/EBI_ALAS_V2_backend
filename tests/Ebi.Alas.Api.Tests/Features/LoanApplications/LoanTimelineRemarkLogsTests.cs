using System.Text.Json;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Deviations;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanTimelineRemarkLogsTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"timeline-remarks-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static LoanApplication LoanWithForm(string formJson)
    {
        var now = DateTimeOffset.UtcNow;
        var loan = LoanApplication.Create(
            "LAM-TL", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);
        loan.SetFormSnapshot(formJson);
        return loan;
    }

    private static string FormJson() => JsonSerializer.Serialize(new
    {
        loans = new[]
        {
            new
            {
                verification = new { findings = "Verified employment and payroll." },
                deviations = new
                {
                    hasDeviations = true,
                    deviationDetails = new[] { "Lacking CIBI" },
                    deviationJustifications = new Dictionary<string, string>
                    {
                        ["Lacking CIBI"] = "CIBI requested, pending release.",
                    },
                    remarks = "For recomputation of interest.",
                    aoRecommendation = "Approve as recommended.",
                    otherRemarks = "No other material remarks.",
                    feeDeviationJustification = "Notary charged extra for annexes.",
                },
            },
        },
    });

    [Fact]
    public async Task Timeline_IncludesFormRemarksVerificationAndDeviationJustifications()
    {
        await using var db = Db();
        var loan = LoanWithForm(FormJson());
        db.LoanApplications.Add(loan);
        db.LoanDeviations.Add(LoanDeviation.Create(
            loan.Id, "DEV-1", "Lacking CIBI", severity: 1, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var events = await LoanReviewEndpoints.BuildTimelineEventsAsync(db, loan, CancellationToken.None);

        Assert.Contains(events, e => e.Type == "remark" && e.Subject == "Encoder remarks"
            && e.Comment!.Contains("For recomputation"));
        Assert.Contains(events, e => e.Type == "remark" && e.Subject == "Account officer recommendation"
            && e.Comment!.Contains("Approve as recommended"));
        Assert.Contains(events, e => e.Type == "remark" && e.Subject == "Other remarks"
            && e.Comment!.Contains("No other material remarks"));
        Assert.Contains(events, e => e.Type == "remark" && e.Subject == "Verification findings"
            && e.Comment!.Contains("Verified employment"));
        Assert.Contains(events, e => e.Type == "deviationRemark"
            && e.Comment!.Contains("CIBI requested"));
        Assert.Contains(events, e => e.Type == "remark"
            && e.Subject == "Fee deviation"
            && e.Comment!.Contains("Notary charged extra"));
    }
}
