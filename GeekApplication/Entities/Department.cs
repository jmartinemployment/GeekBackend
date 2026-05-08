namespace GeekApplication.Entities;

public class Department
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string Description { get; set; }
    public string? IconName { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual ICollection<UseCase> UseCases { get; set; } = [];
}
