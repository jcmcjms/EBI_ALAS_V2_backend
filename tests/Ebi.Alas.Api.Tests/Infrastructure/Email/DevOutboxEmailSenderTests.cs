using Ebi.Alas.Api.Infrastructure.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Infrastructure.Email;

public sealed class DevOutboxEmailSenderTests
{
    private sealed class DevEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task SendAsync_WritesEmlFile()
    {
        var outbox = Path.Combine(Path.GetTempPath(), $"alas-outbox-{Guid.NewGuid():N}");
        var options = Options.Create(new EmailOptions
        {
            Host = "unused",
            From = "alas@example.invalid",
            Provider = "DevOutbox",
            DevOutboxPath = outbox
        });

        var sender = new DevOutboxEmailSender(
            options,
            new DevEnvironment(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DevOutboxEmailSender>.Instance);

        await sender.SendAsync(
            "user@example.invalid",
            "ALAS temporary password",
            "text body",
            "<p>html body</p>",
            CancellationToken.None);

        var files = Directory.GetFiles(outbox, "*.eml");
        Assert.Single(files);
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("user@example.invalid", content);
        Assert.Contains("ALAS temporary password", content);
        Assert.Contains("text body", content);

        Directory.Delete(outbox, recursive: true);
    }

    [Fact]
    public async Task SendAsync_InProduction_Throws()
    {
        var options = Options.Create(new EmailOptions
        {
            Host = "unused",
            From = "alas@example.invalid",
            Provider = "DevOutbox",
            DevOutboxPath = Path.Combine(Path.GetTempPath(), $"alas-outbox-{Guid.NewGuid():N}")
        });

        var sender = new DevOutboxEmailSender(
            options,
            new DevEnvironment { EnvironmentName = Environments.Production },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DevOutboxEmailSender>.Instance);

        await Assert.ThrowsAsync<EmailSendException>(() =>
            sender.SendAsync("user@example.invalid", "s", "t", "h", CancellationToken.None));
    }
}
