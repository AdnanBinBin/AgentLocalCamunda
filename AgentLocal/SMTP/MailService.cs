using System.Net.Mail;
using System.Net;
using Microsoft.Extensions.Options;

namespace AgentLocal.SMTP
{
   
    public class MailService
    {
        private readonly MailConfig _config;

        public MailService(IOptions<MailConfig> config)
        {
            _config = config.Value;
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            try
            {
                using var message = new MailMessage();
                message.From = new MailAddress(_config.FromEmail, _config.FromName);
                message.To.Add(to);
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = isHtml;

                using var client = new SmtpClient(_config.SmtpServer, _config.Port);
                client.Credentials = new NetworkCredential(_config.Username, _config.Password);
                client.EnableSsl = true;

                await client.SendMailAsync(message);
                Console.WriteLine($"Email sent successfully to {to}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
                throw;
            }
        }

        public async Task SendEmailWithAttachmentAsync(string to, string subject, string body,
            string attachmentPath, bool isHtml = false)
        {
            try
            {
                using var message = new MailMessage();
                message.From = new MailAddress(_config.FromEmail, _config.FromName);
                message.To.Add(to);
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = isHtml;

                // Add attachment
                if (File.Exists(attachmentPath))
                {
                    var attachment = new Attachment(attachmentPath);
                    message.Attachments.Add(attachment);
                }

                using var client = new SmtpClient(_config.SmtpServer, _config.Port);
                client.Credentials = new NetworkCredential(_config.Username, _config.Password);
                client.EnableSsl = true;

                await client.SendMailAsync(message);
                Console.WriteLine($"Email with attachment sent successfully to {to}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email with attachment: {ex.Message}");
                throw;
            }
        }

        public async Task SendEmailToMultipleRecipientsAsync(List<string> toAddresses,
            string subject, string body, bool isHtml = false)
        {
            try
            {
                using var message = new MailMessage();
                message.From = new MailAddress(_config.FromEmail, _config.FromName);

                // Add multiple recipients
                foreach (var address in toAddresses)
                {
                    message.To.Add(address);
                }

                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = isHtml;

                using var client = new SmtpClient(_config.SmtpServer, _config.Port);
                client.Credentials = new NetworkCredential(_config.Username, _config.Password);
                client.EnableSsl = true;

                await client.SendMailAsync(message);
                Console.WriteLine($"Email sent successfully to multiple recipients");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email to multiple recipients: {ex.Message}");
                throw;
            }
        }
    }
}