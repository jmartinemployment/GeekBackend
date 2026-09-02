using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GeekAPI.Services.ContentCreatorV2.Carousel;

public static class GccV2LinkedInCarouselPdfService
{
    public const float PageWidth = 1080f;
    public const float PageHeight = 1350f;
    public const float SafePaddingHorizontal = 60f;
    public const float SafePaddingVertical = 80f;

    static GccV2LinkedInCarouselPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(LinkedInCarouselDraft draft, LinkedInCarouselBrandStyle style)
    {
        var document = Document.Create(container =>
        {
            foreach (var slide in draft.Slides)
            {
                container.Page(page =>
                {
                    page.Size(PageWidth, PageHeight);
                    page.MarginHorizontal(SafePaddingHorizontal);
                    page.MarginVertical(SafePaddingVertical);
                    page.DefaultTextStyle(x => x
                        .FontFamily(Fonts.Arial)
                        .FontSize(24)
                        .FontColor(style.TextColor));

                    page.Background().Background(GetSlideBackground(slide.Role, style));

                    page.Content().Element(c => ComposeSlide(c, slide, style, draft.Slides.Count));
                });
            }
        });

        return document.GeneratePdf();
    }

    private static string GetSlideBackground(string role, LinkedInCarouselBrandStyle style)
    {
        if (string.Equals(role, GccV2LinkedInCarouselRoles.Cover, StringComparison.OrdinalIgnoreCase))
            return style.PrimaryColor;

        return string.Equals(role, GccV2LinkedInCarouselRoles.Cta, StringComparison.OrdinalIgnoreCase)
            ? "#F1F5F9"
            : "#FFFFFF";
    }

    private static void ComposeSlide(
        IContainer container,
        CarouselSlide slide,
        LinkedInCarouselBrandStyle style,
        int totalSlides)
    {
        var isCover = string.Equals(slide.Role, GccV2LinkedInCarouselRoles.Cover, StringComparison.OrdinalIgnoreCase);
        var titleColor = isCover ? "#FFFFFF" : style.PrimaryColor;
        var bodyColor = isCover ? "#E2E8F0" : style.TextColor;

        container.Column(column =>
        {
            column.Spacing(16);

            if (isCover)
            {
                column.Item().Text(slide.Title).FontSize(44).Bold().FontColor(titleColor);
                if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                    column.Item().PaddingTop(12).Text(slide.Subtitle).FontSize(28).FontColor(bodyColor);
            }
            else
            {
                column.Item().Text(slide.Title).FontSize(36).Bold().FontColor(titleColor);
                if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                    column.Item().Text(slide.Subtitle).FontSize(22).FontColor(bodyColor);
            }

            if (slide.Bullets.Count > 0)
            {
                column.Item().PaddingTop(20).Column(bullets =>
                {
                    foreach (var bullet in slide.Bullets)
                    {
                        bullets.Item().PaddingBottom(10).Row(row =>
                        {
                            row.ConstantItem(18).Text("•").FontSize(26).FontColor(isCover ? "#FFFFFF" : style.SecondaryColor);
                            row.RelativeItem().Text(bullet).FontSize(26).LineHeight(1.35f).FontColor(bodyColor);
                        });
                    }
                });
            }

            column.Item().AlignBottom().AlignRight().Text($"{slide.Index + 1} / {totalSlides}")
                .FontSize(14)
                .FontColor(isCover ? "#CBD5E1" : Colors.Grey.Medium);

            if (string.Equals(slide.Role, GccV2LinkedInCarouselRoles.Cta, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(style.CompanyName))
            {
                column.Item().AlignBottom().PaddingTop(24).Text(style.CompanyName)
                    .FontSize(18)
                    .FontColor(style.SecondaryColor);
            }
        });
    }
}
