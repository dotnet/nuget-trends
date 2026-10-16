using RabbitMQ.Client;

namespace NuGetTrends.Scheduler;

/// <summary>
/// A RabbitMQ consumer stopped receiving messages: its channel or connection was closed, or the broker cancelled it.
/// For example, the broker closes a channel with 406 PRECONDITION_FAILED when a delivery stays unacked past
/// consumer_timeout (30 minutes by default).
/// </summary>
public class RabbitMqConsumerLostException(string queueName, ShutdownEventArgs reason)
    : Exception($"Consumer on queue '{queueName}' lost: {reason.ReplyCode} {reason.ReplyText} (initiated by {reason.Initiator}).")
{
    public ushort ReplyCode { get; } = reason.ReplyCode;
}
