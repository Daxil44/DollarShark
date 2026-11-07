using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace DollarShark.Api.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class EmailOptions
{
    public const string LogProvider = "Log";
    public const string SmtpProvider = "Smtp";

    /// <summary>"Smtp" sends real email; "Log" writes messages (including links) to the application log for local development only.</summary>
    public string Provider { get; set; } = LogProvider;
    public string FromAddress { get; set; } = "no-reply@dollarshark.local";
    public string FromName { get; set; } = "DollarShark";
    public SmtpSettings Smtp { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Uses STARTTLS on port 587 (Gmail, Outlook, most providers) or implicit TLS where the provider requires it.</summary>
    public bool EnableSsl { get; set; } = true;
}

/// <summary>Development-only sender: writes the message to the log so links can be copied without a mailbox.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning("DEVELOPMENT EMAIL (not sent) to {To}\nSubject: {Subject}\n\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrEmpty(settings.Smtp.Username) ? null : new NetworkCredential(settings.Smtp.Username, settings.Smtp.Password),
        };
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// Emails are queued and sent in the background so request timing does not reveal whether an account exists
/// and a slow mail server never blocks sign-up or password reset.
/// </summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    public bool TryEnqueue(EmailMessage message) => channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) => channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class EmailDispatchService(EmailQueue queue, IServiceProvider services, ILogger<EmailDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Never log the body: it contains single-use account links.
                    logger.LogError(exception, "Failed to send email \"{Subject}\".", message.Subject);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
