namespace Beauty.Api.Email;

public static class IcsBuilder
{
    private static string Escape(string s)
        => s.Replace("\\", "\\\\")
            .Replace(",", "\\,")
            .Replace(";", "\\;")
            .Replace("\n", "\\n");

    public static (string FileName, byte[] Bytes, string Mime) Build(
        string customerEmail,
        string subject,
        DateTime startUtc,
        DateTime endUtc,
        string location,
        string description)
    {
        var uid = $"{Guid.NewGuid()}@saqqarallc.com";

        var lines = new[]
        {
            "BEGIN:VCALENDAR",
            "PRODID:-//Saqqara LLC//Booking//EN",
            "VERSION:2.0",
            "METHOD:REQUEST",
            "BEGIN:VEVENT",
            $"UID:{uid}",
            $"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}",
            $"DTSTART:{startUtc:yyyyMMddTHHmmssZ}",
            $"DTEND:{endUtc:yyyyMMddTHHmmssZ}",
            $"SUMMARY:{Escape(subject)}",
            $"LOCATION:{Escape(location)}",
            $"DESCRIPTION:{Escape(description)}",
            "ORGANIZER:mailto:no-reply@saqqarallc.com",
            $"ATTENDEE;CN=Customer;ROLE=REQ-PARTICIPANT:mailto:{customerEmail}",
            "END:VEVENT",
            "END:VCALENDAR"
        };

        var ics = string.Join("\r\n", lines) + "\r\n";
        return ("booking.ics", System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar");
    }
}