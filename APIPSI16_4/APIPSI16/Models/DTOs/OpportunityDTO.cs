using System;

namespace APIPSI16.DTOs
{
    public class OpportunityDTO
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public int? CompanyId { get; set; }
        public byte? EmploymentType { get; set; }
        public byte? SeniorityLevel { get; set; }
        public string? Location { get; set; }
        public byte? RemoteOption { get; set; }
        public string? RequiredJobRoleIds { get; set; }
    }
}