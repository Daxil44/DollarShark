using System.Net;

namespace DollarShark.Api.Email;

public static class AccountEmails
{
    public static EmailMessage Confirmation(string to, string link) => Build(
        to,
        "Confirm your DollarShark email",
        "Confirm your email address",
        "Thanks for signing up. Confirm this email address to start using your DollarShark workspace.",
        "Confirm email",
        link,
        "If you didn't create a DollarShark account, you can ignore this email.");

    public static EmailMessage PasswordReset(string to, string link) => Build(
        to,
        "Reset your DollarShark password",
        "Reset your password",
        "We received a request to reset the password for your DollarShark account. This link expires in 3 hours.",
        "Choose a new password",
        link,
        "If you didn't ask to reset your password, you can ignore this email — your password won't change.");

    private static EmailMessage Build(string to, string subject, string heading, string intro, string buttonText, string link, string footer)
    {
        var text = $"{heading}\n\n{intro}\n\n{buttonText}: {link}\n\n{footer}\n\n— DollarShark";
        var safeLink = WebUtility.HtmlEncode(link);
        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:0;background:#f4f7f6;font-family:Segoe UI,Arial,sans-serif;color:#172c2b;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f7f6;padding:32px 12px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border:1px solid #e5ece9;border-radius:16px;overflow:hidden;">
                    <tr><td style="background:#123c46;padding:20px 28px;color:#ffffff;font-size:20px;font-weight:800;letter-spacing:-0.5px;">Dollar<span style="color:#48c78e;">Shark</span></td></tr>
                    <tr><td style="padding:28px;">
                      <h1 style="margin:0 0 12px;font-size:22px;">{WebUtility.HtmlEncode(heading)}</h1>
                      <p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#4f625e;">{WebUtility.HtmlEncode(intro)}</p>
                      <a href="{safeLink}" style="display:inline-block;background:#16845d;color:#ffffff;text-decoration:none;font-weight:700;padding:12px 22px;border-radius:10px;">{WebUtility.HtmlEncode(buttonText)}</a>
                      <p style="margin:24px 0 0;font-size:12px;line-height:1.6;color:#7b8986;">If the button doesn't work, paste this link into your browser:<br><span style="word-break:break-all;">{safeLink}</span></p>
                      <p style="margin:20px 0 0;font-size:12px;color:#7b8986;">{WebUtility.HtmlEncode(footer)}</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
        return new EmailMessage(to, subject, text, html);
    }
}
