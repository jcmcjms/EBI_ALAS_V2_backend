using System.Threading.Channels;

namespace Alas.Infrastructure.Auditing;

public sealed class AuditChannel
{
    private readonly Channel<AuditLog> _channel = Channel.CreateBounded<AuditLog>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    
    public ChannelWriter<AuditLog> Writer => _channel.Writer;
    public ChannelReader<AuditLog> Reader => _channel.Reader;
}