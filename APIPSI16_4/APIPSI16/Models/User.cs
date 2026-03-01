using System;
using System.Collections.Generic;

namespace APIPSI16.Models;

public partial class User
{
    public int UserId { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public int? Nationality { get; set; }

    public int? JobPreference { get; set; }

    public string? ProfileBio { get; set; }

    public DateOnly? DoB { get; set; }

    public int? Role { get; set; }

    public string? PasswordHash { get; set; }

    public string? ProfilePictureUrl { get; set; }

    public string? BannerUrl { get; set; }

    public virtual ICollection<AuditLog> AuditLogs{ get; set; } = new List<AuditLog>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual ICollection<ChatUser> ChatUsers { get; set; } = new List<ChatUser>();

    public virtual ICollection<Chat> Chats { get; set; } = new List<Chat>();

    public virtual ICollection<CompanyMember> CompanyMembers { get; set; } = new List<CompanyMember>();

    public virtual ICollection<EmployerCandidateHistory> EmployerCandidateHistories { get; set; } = new List<EmployerCandidateHistory>();

    public virtual ICollection<InterviewRound> InterviewRounds { get; set; } = new List<InterviewRound>();

    public virtual ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();

    public virtual ICollection<Notification> NotificationActorUsers { get; set; } = new List<Notification>();

    public virtual ICollection<Notification> NotificationUsers { get; set; } = new List<Notification>();

    public virtual ICollection<Opportunity> Opportunities { get; set; } = new List<Opportunity>();

    public virtual ICollection<PostComment> PostComments { get; set; } = new List<PostComment>();

    public virtual ICollection<PostReaction> PostReactions { get; set; } = new List<PostReaction>();

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual ICollection<ProfileEducation> ProfileEducations { get; set; } = new List<ProfileEducation>();

    public virtual ICollection<ProfileExperience> ProfileExperiences { get; set; } = new List<ProfileExperience>();

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();

    public virtual ICollection<SkillEndorsement> SkillEndorsements { get; set; } = new List<SkillEndorsement>();

    public virtual ICollection<UserSkill> UserSkills { get; set; } = new List<UserSkill>();
}
