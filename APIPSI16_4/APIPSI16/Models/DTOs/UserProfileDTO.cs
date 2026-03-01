using System;
using System.Collections.Generic;

namespace APIPSI16.Models.DTOs
{
    public class UserProfileDTO
    {
        public int UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int? Nationality { get; set; }
        public int? JobPreference { get; set; }
        public string? ProfileBio { get; set; }
        public DateOnly? DoB { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public string? BannerUrl { get; set; }
        public int? Role { get; set; }
        
        public List<SkillDTO>? Skills { get; set; }
        public List<ProfileExperienceDTO>? Experiences { get; set; }
        public List<ProfileEducationDTO>? Educations { get; set; }
    }

    public class SkillDTO
    {
        public int SkillId { get; set; }
        public string? Name { get; set; }
        public int EndorsementCount { get; set; }
    }

    public class ProfileExperienceDTO
    {
        public int ExperienceId { get; set; }
        public string? JobTitle { get; set; }
        public string? CompanyName { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? Description { get; set; }
    }

    public class ProfileEducationDTO
    {
        public int EducationId { get; set; }
        public string? Institution { get; set; }
        public string? Degree { get; set; }
        public string? FieldOfStudy { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
    }
}
