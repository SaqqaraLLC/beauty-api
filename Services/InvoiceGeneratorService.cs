using Beauty.Api.Models.Company;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Beauty.Api.Services;

public class InvoiceGeneratorService
{
    public byte[] GenerateInvoice(Invoice invoice)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var subtotalCents  = invoice.LineItems.Sum(l => l.TotalCents);
        var commissionRate = 0.15m;                               // 15% Saqqara platform fee
        var commissionCents = (int)(subtotalCents * commissionRate);
        var totalCents     = subtotalCents + commissionCents;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Element(c => ComposeHeader(c, invoice));
                page.Content().Element(c => ComposeContent(c, invoice, subtotalCents, commissionCents, totalCents));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, Invoice invoice)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("SAQQARA LLC")
                        .FontSize(20).Bold().FontColor("#C9A227");
                    c.Item().Text("saqqarallc.com")
                        .FontSize(9).FontColor("#888888");
                    c.Item().PaddingTop(2).Text("support@saqqarallc.com")
                        .FontSize(9).FontColor("#888888");
                });

                row.ConstantItem(160).Column(c =>
                {
                    c.Item().AlignRight().Text("INVOICE")
                        .FontSize(22).Bold().FontColor("#333333");
                    c.Item().AlignRight().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(); });
                        void Row(string label, string value)
                        {
                            t.Cell().AlignRight().Text(label).FontSize(9).FontColor("#888888");
                            t.Cell().AlignRight().Text(value).FontSize(9).Bold();
                        }
                        Row("Invoice #:", invoice.InvoiceNumber);
                        Row("Issued:", invoice.IssuedAt.ToString("MMM d, yyyy"));
                        Row("Due:", invoice.DueAt.ToString("MMM d, yyyy"));
                        Row("Status:", invoice.Status.ToString().ToUpper());
                    });
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#C9A227");
        });
    }

    private static void ComposeContent(
        IContainer container, Invoice invoice,
        int subtotalCents, int commissionCents, int totalCents)
    {
        container.PaddingTop(20).Column(col =>
        {
            // Bill to
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("BILL TO").FontSize(8).FontColor("#888888").Bold();
                    c.Item().PaddingTop(3).Text(invoice.Company?.CompanyName ?? "—")
                        .FontSize(11).Bold();
                    c.Item().Text(invoice.Company?.Email ?? "").FontSize(9).FontColor("#555555");
                    c.Item().Text(invoice.Company?.Phone ?? "").FontSize(9).FontColor("#555555");
                });

                if (invoice.CompanyBooking != null)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("FOR EVENT").FontSize(8).FontColor("#888888").Bold();
                        c.Item().PaddingTop(3).Text(invoice.CompanyBooking.Title)
                            .FontSize(11).Bold();
                        c.Item().Text(invoice.CompanyBooking.EventDate.ToString("MMMM d, yyyy"))
                            .FontSize(9).FontColor("#555555");
                        c.Item().Text(invoice.CompanyBooking.Location)
                            .FontSize(9).FontColor("#555555");
                    });
                }
            });

            // Line items table
            col.Item().PaddingTop(24).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.ConstantColumn(50);
                    c.ConstantColumn(80);
                    c.ConstantColumn(60);
                    c.ConstantColumn(80);
                });

                // Header row
                table.Header(h =>
                {
                    void H(string text) =>
                        h.Cell().Background("#1A1A1A").Padding(6)
                            .Text(text).FontSize(9).Bold().FontColor("#C9A227");
                    H("Description"); H("Qty"); H("Unit Price"); H("Discount"); H("Total");
                });

                // Data rows
                var bg = false;
                foreach (var line in invoice.LineItems)
                {
                    bg = !bg;
                    var rowBg = bg ? "#FAFAFA" : "#FFFFFF";
                    void Cell(string text, bool right = false)
                    {
                        var cell = table.Cell().Background(rowBg)
                            .BorderBottom(0.5f).BorderColor("#EEEEEE").Padding(6);
                        if (right) cell.AlignRight().Text(text).FontSize(9);
                        else cell.Text(text).FontSize(9);
                    }
                    Cell(line.Description);
                    Cell(line.Quantity.ToString(), right: true);
                    Cell($"${line.UnitPriceCents / 100m:F2}", right: true);
                    Cell(line.DiscountPercent.HasValue ? $"{line.DiscountPercent:0.##}%" : "—", right: true);
                    Cell($"${line.TotalCents / 100m:F2}", right: true);
                }
            });

            // Totals
            col.Item().PaddingTop(8).AlignRight().Width(260).Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(100); });

                void TotalRow(string label, string value, bool bold = false, string? color = null)
                {
                    var lt = table.Cell().AlignRight().PaddingVertical(3)
                        .Text(label).FontSize(9).FontColor(color ?? "#555555");
                    if (bold) lt.Bold();
                    var vt = table.Cell().AlignRight().PaddingVertical(3)
                        .Text(value).FontSize(9).FontColor(color ?? "#333333");
                    if (bold) vt.Bold();
                }

                TotalRow("Subtotal:", $"${subtotalCents / 100m:F2}");
                TotalRow("Platform Fee (15%):", $"${commissionCents / 100m:F2}");
                table.Cell().ColumnSpan(2).LineHorizontal(0.5f).LineColor("#CCCCCC");
                TotalRow("TOTAL DUE:", $"${totalCents / 100m:F2}", bold: true, color: "#C9A227");
            });

            // Notes
            if (!string.IsNullOrWhiteSpace(invoice.Notes))
            {
                col.Item().PaddingTop(20).Background("#F8F8F8").Padding(12).Column(c =>
                {
                    c.Item().Text("Notes").Bold().FontSize(9);
                    c.Item().PaddingTop(4).Text(invoice.Notes).FontSize(9).FontColor("#555555");
                });
            }

            // Payment instructions
            col.Item().PaddingTop(20).Column(c =>
            {
                c.Item().Text("PAYMENT INSTRUCTIONS").FontSize(9).Bold().FontColor("#888888");
                c.Item().PaddingTop(4).Text(
                    "Payment is due by the date shown above. To pay, log in to your Saqqara dashboard " +
                    "or contact support@saqqarallc.com. Late payments may incur a 1.5% monthly fee."
                ).FontSize(9).FontColor("#555555");
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text("Saqqara LLC · saqqarallc.com · EIN 46-3485577")
                .FontSize(8).FontColor("#AAAAAA");
            row.ConstantItem(80).AlignRight().Text(x =>
            {
                x.Span("Page ").FontSize(8).FontColor("#AAAAAA");
                x.CurrentPageNumber().FontSize(8).FontColor("#AAAAAA");
                x.Span(" of ").FontSize(8).FontColor("#AAAAAA");
                x.TotalPages().FontSize(8).FontColor("#AAAAAA");
            });
        });
    }
}
