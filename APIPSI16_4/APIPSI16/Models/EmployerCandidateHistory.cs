using System;
using System.Collections.Generic;

namespace APIPSI16.Models;

public partial class EmployerCandidateHistory
{
    public int EmployerCandidateHistoryId { get; set; }

    public int CompanyId { get; set; }

    public int UserId { get; set; }

    public int? OpportunityId { get; set; }

    public string Outcome { get; set; } = null!;

    public string? StageReached { get; set; }

    public string? Notes { get; set; }

    public DateTime LastContactAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual Opportunity? Opportunity { get; set; }

    public virtual User User { get; set; } = null!;
}
