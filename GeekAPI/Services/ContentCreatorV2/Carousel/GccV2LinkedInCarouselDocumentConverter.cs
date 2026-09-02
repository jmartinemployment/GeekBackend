using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Carousel;

public static class GccV2LinkedInCarouselDocumentConverter
{
    private static readonly string[] DefaultRoles =
    [
        GccV2LinkedInCarouselRoles.Cover,
        GccV2LinkedInCarouselRoles.Problem,
        GccV2LinkedInCarouselRoles.Teach,
        GccV2LinkedInCarouselRoles.Teach,
        GccV2LinkedInCarouselRoles.Teach,
        GccV2LinkedInCarouselRoles.Framework,
        GccV2LinkedInCarouselRoles.Cta,
    ];

    public static LinkedInCarouselDraft FromContentDocument(ContentDocument document, string title)
    {
        var allSections = new List<Section> { document.Lede };
        allSections.AddRange(document.Sections);

        var slides = new List<CarouselSlide>();
        for (var i = 0; i < allSections.Count; i++)
        {
            var section = allSections[i];
            var role = i < DefaultRoles.Length ? DefaultRoles[i] : GccV2LinkedInCarouselRoles.Teach;
            if (i == allSections.Count - 1 && allSections.Count > 1)
                role = GccV2LinkedInCarouselRoles.Cta;

            var bullets = ExtractBullets(section);
            slides.Add(new CarouselSlide(i, role, section.Heading, bullets));
        }

        var caption = BuildCaption(document, title);
        var filename = SlugHelper.Slugify(title).Replace('-', '_');
        return new LinkedInCarouselDraft(slides, caption, [], filename);
    }

    private static IReadOnlyList<string> ExtractBullets(Section section)
    {
        var bullets = new List<string>();
        foreach (var paragraph in section.Paragraphs)
        {
            if (paragraph is ListParagraph list)
            {
                foreach (var item in list.Items)
                    bullets.Add(string.Join("", item.Select(r => r.Text)).Trim());
            }
            else if (paragraph is TextParagraph text)
            {
                var line = string.Join("", text.Runs.Select(r => r.Text)).Trim();
                if (line.Length > 0)
                    bullets.Add(line);
            }
        }

        return bullets.Take(5).ToList();
    }

    private static string BuildCaption(ContentDocument document, string title)
    {
        var lede = document.Lede.Paragraphs.OfType<TextParagraph>().FirstOrDefault();
        var intro = lede is null
            ? title
            : string.Join(" ", lede.Runs.Select(r => r.Text)).Trim();
        return $"{intro}\n\nWhat would you add from your experience?";
    }
}
