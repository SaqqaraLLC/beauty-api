using Beauty.Api.Models.Company;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Beauty.Api.Services;

public class ContractGeneratorService
{
    public byte[] GenerateBookingContract(CompanyBooking booking, string generatedByName)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Element(ComposeHeader);
                page.Content().Element(c => ComposeContent(c, booking));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();

        void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("SAQQARA LLC")
                            .FontSize(16).Bold().FontColor("#C9A227");
                        c.Item().Text("Service Agreement & Booking Contract")
                            .FontSize(11).FontColor("#444444");
                    });
                    row.ConstantItem(120).AlignRight().Column(c =>
                    {
                        c.Item().Text($"Contract #{booking.Id:D6}")
                            .FontSize(9).FontColor("#888888");
                        c.Item().Text(DateTime.UtcNow.ToString("MMMM d, yyyy"))
                            .FontSize(9).FontColor("#888888");
                    });
                });

                col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#C9A227");
            });
        }

        void ComposeContent(IContainer container, CompanyBooking booking)
        {
            container.PaddingTop(16).Column(col =>
            {
                // Parties
                col.Item().Text("PARTIES").Bold().FontSize(11);
                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); });
                    table.Cell().Text("Client (Company):").Bold();
                    table.Cell().Text(booking.Company?.CompanyName ?? "—");
                    table.Cell().Text("Platform:").Bold();
                    table.Cell().Text("Saqqara LLC — saqqarallc.com");
                    table.Cell().Text("Prepared by:").Bold();
                    table.Cell().Text(generatedByName);
                });

                col.Item().PaddingTop(16).Text("EVENT DETAILS").Bold().FontSize(11);
                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); });
                    table.Cell().Text("Event Title:").Bold();
                    table.Cell().Text(booking.Title);
                    table.Cell().Text("Date:").Bold();
                    table.Cell().Text(booking.EventDate.ToString("MMMM d, yyyy h:mm tt") + " UTC");
                    if (booking.EventEndDate.HasValue)
                    {
                        table.Cell().Text("End Date:").Bold();
                        table.Cell().Text(booking.EventEndDate.Value.ToString("MMMM d, yyyy h:mm tt") + " UTC");
                    }
                    table.Cell().Text("Location:").Bold();
                    table.Cell().Text(booking.Location);
                    if (!string.IsNullOrWhiteSpace(booking.Description))
                    {
                        table.Cell().Text("Description:").Bold();
                        table.Cell().Text(booking.Description);
                    }
                    if (!string.IsNullOrWhiteSpace(booking.PackageLabel))
                    {
                        table.Cell().Text("Package:").Bold();
                        table.Cell().Text(booking.PackageLabel +
                            (booking.PackageDiscountPercent.HasValue
                                ? $" ({booking.PackageDiscountPercent:0.##}% discount)"
                                : ""));
                    }
                });

                // Artist slots
                if (booking.ArtistSlots.Any())
                {
                    col.Item().PaddingTop(16).Text("ENGAGED ARTISTS").Bold().FontSize(11);
                    col.Item().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });

                        // Header
                        table.Header(h =>
                        {
                            h.Cell().Background("#F5F0E8").Padding(4).Text("Artist").Bold();
                            h.Cell().Background("#F5F0E8").Padding(4).Text("Service").Bold();
                            h.Cell().Background("#F5F0E8").Padding(4).Text("Fee").Bold();
                            h.Cell().Background("#F5F0E8").Padding(4).Text("Status").Bold();
                        });

                        foreach (var slot in booking.ArtistSlots)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor("#DDDDDD").Padding(4)
                                .Text(slot.ArtistName ?? $"Artist #{slot.ArtistId}");
                            table.Cell().BorderBottom(0.5f).BorderColor("#DDDDDD").Padding(4)
                                .Text(slot.ServiceRequested);
                            table.Cell().BorderBottom(0.5f).BorderColor("#DDDDDD").Padding(4)
                                .Text(slot.FeeCents.HasValue ? $"${slot.FeeCents / 100m:F2}" : "TBD");
                            table.Cell().BorderBottom(0.5f).BorderColor("#DDDDDD").Padding(4)
                                .Text(slot.Status.ToString());
                        }
                    });

                    var totalCents = booking.ArtistSlots
                        .Where(s => s.FeeCents.HasValue)
                        .Sum(s => s.FeeCents!.Value);

                    if (totalCents > 0)
                    {
                        col.Item().PaddingTop(4).AlignRight()
                            .Text($"Total: ${totalCents / 100m:F2}")
                            .Bold().FontSize(11);
                    }
                }

                // NDA clause
                if (booking.NdaRequired)
                {
                    col.Item().PaddingTop(16).Background("#FFF8E7").Padding(10).Column(c =>
                    {
                        c.Item().Text("NON-DISCLOSURE AGREEMENT").Bold().FontSize(10);
                        c.Item().PaddingTop(4).Text(
                            "All parties acknowledge that information shared in connection with this engagement, " +
                            "including event details, client identity, creative concepts, pricing, and any proprietary " +
                            "business information, is confidential. Artists and representatives engaged through Saqqara LLC " +
                            "agree not to disclose such information to third parties without prior written consent. " +
                            "This obligation survives the completion or cancellation of this booking."
                        ).FontSize(9).FontColor("#555555");
                    });
                }

                // %PURE Product Stipulation
                col.Item().PaddingTop(16).Background("#FAFAFA").Border(0.5f).BorderColor("#E8E0CC").Padding(10).Column(c =>
                {
                    c.Item().Text("100% PURE PRODUCT POLICY").Bold().FontSize(10).FontColor("#C9A227");
                    c.Item().PaddingTop(6).Column(items =>
                    {
                        var clauses = new[]
                        {
                            "All services performed through Saqqara LLC exclusively use 100% PURE brand products (\"%PURE\"), a certified clean beauty vendor.",
                            "Each client receives a personalized, sealed %PURE product kit used solely for their appointment. Products are never reused between clients under any circumstances.",
                            "The full cost of the %PURE product kit is included in the client's invoice at the published rate (wholesale cost plus an 80% platform markup). Promotional pricing may apply where a valid promo code was presented at the time of booking. Client retains all products following the service as part of the Saqqara experience.",
                            "Product kits are held at the designated service location. The location bears inventory responsibility until products are released for a confirmed booking.",
                            "In the event of a client or artist no-show after products have been prepared, the cost of those products (at wholesale) shall be deducted from the responsible party's deposit. Prepared products are non-returnable once opened or staged.",
                            "Saqqara LLC prohibits the resale, redistribution, or diversion of %PURE products through any unauthorized channel, including online marketplaces.",
                        };
                        foreach (var clause in clauses)
                            items.Item().PaddingTop(3).Row(r =>
                            {
                                r.ConstantItem(12).Text("•").FontSize(9).FontColor("#C9A227");
                                r.RelativeItem().Text(clause).FontSize(8.5f).FontColor("#444444");
                            });
                    });
                });

                // Terms
                col.Item().PaddingTop(16).Text("TERMS & CONDITIONS").Bold().FontSize(11);
                col.Item().PaddingTop(6).Column(terms =>
                {
                    var items = new[]
                    {
                        "Payment is due within 48 hours of booking confirmation unless otherwise agreed.",
                        "Cancellations made within 72 hours of the event date are subject to a 50% cancellation fee.",
                        "Saqqara LLC retains a platform commission per the agreed rate schedule.",
                        "Artists are independent contractors; Saqqara LLC is not liable for their conduct.",
                        "This agreement is governed by the laws of the State of Florida.",
                    };
                    foreach (var item in items)
                        terms.Item().Row(r =>
                        {
                            r.ConstantItem(12).Text("•");
                            r.RelativeItem().Text(item).FontSize(9).FontColor("#444444");
                        });
                });

                // Signatures
                col.Item().PaddingTop(24).Text("SIGNATURES").Bold().FontSize(11);
                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Company Representative").Bold().FontSize(9);
                        c.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor("#888888");
                        c.Item().PaddingTop(2).Text("Signature & Date").FontSize(8).FontColor("#888888");
                    });
                    row.ConstantItem(30);
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Saqqara LLC Representative").Bold().FontSize(9);
                        c.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor("#888888");
                        c.Item().PaddingTop(2).Text("Signature & Date").FontSize(8).FontColor("#888888");
                    });
                });
            });
        }

        void ComposeFooter(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text("Saqqara LLC · saqqarallc.com · support@saqqarallc.com")
                    .FontSize(8).FontColor("#AAAAAA");
                row.ConstantItem(80).AlignRight()
                    .Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#AAAAAA");
                        x.CurrentPageNumber().FontSize(8).FontColor("#AAAAAA");
                        x.Span(" of ").FontSize(8).FontColor("#AAAAAA");
                        x.TotalPages().FontSize(8).FontColor("#AAAAAA");
                    });
            });
        }
    }
}
