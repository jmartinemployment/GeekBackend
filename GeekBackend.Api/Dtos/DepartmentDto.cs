namespace GeekBackend.Api.Dtos;

public class DepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public string? IconName { get; set; }
    public int SortOrder { get; set; }
    public List<UseCaseDto> UseCases { get; set; } = new();

    public DepartmentDto(int id, string name, string slug, string description, string? iconName, int sortOrder)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Description = description;
        IconName = iconName ?? string.Empty;
        SortOrder = sortOrder;
        UseCases = new();
    }
}

// public record DepartmentDto(int Id, string Name, string Slug, string Description, string? IconName, int SortOrder);