using System;
using System.Collections.Generic;

namespace APIPSI16.Models;

public partial class Opportunity
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int? CreatorId { get; set; }

    public int? CompanyId { get; set; }

    public byte? EmploymentType { get; set; }

    public byte? SeniorityLevel { get; set; }

    public string? Location { get; set; }

    public byte? RemoteOption { get; set; }

    /// <summary>Comma-separated JobRoleId values (e.g. "1,3,7")</summary>
    public string? RequiredJobRoleIds { get; set; }

    public virtual Company? Company { get; set; }

    public virtual User? Creator { get; set; }

    public virtual ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
    public virtual ICollection<EmployerCandidateHistory> EmployerCandidateHistories { get; set; } = new List<EmployerCandidateHistory>();
}