using APIPSI16.DTOs;

public class CompanyProfileDTO
{
    public int CompanyId { get; set; }
    public string Name { get; set; }
    public string? Industry { get; set; }
    public string? Location { get; set; }
    public string? CompanyLogoUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
    public List<OpportunityDTO> Opportunities { get; set; } = new();
}