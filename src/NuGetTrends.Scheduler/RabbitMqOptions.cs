namespace NuGetTrends.Scheduler
{
    public class RabbitMqOptions
    {
        public string? Hostname { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public int Port { get; set; } = 5672;
    }
}
