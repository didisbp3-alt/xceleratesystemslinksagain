using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using APIPSI16.Data;
using APIPSI16.Models;
using APIPSI16.Models.DTOs;

namespace APIPSI16.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class JobApplicationsController : ControllerBase
    {
        private readonly xcleratesystemslinks_SampleDBContext _db;
        private readonly ILogger<JobApplicationsController> _logger;

        public JobApplicationsController(xcleratesystemslinks_SampleDBContext db, ILogger<JobApplicationsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // POST api/jobapplications/apply
        [HttpPost("apply")]
        public async Task<IActionResult> Apply([FromBody] ApplyDto dto)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (dto.OpportunityId <= 0) return BadRequest("OpportunityId is required.");

            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name.Length > 50)
                return BadRequest("Name must be 50 characters or fewer.");

            if (await _db.JobApplications.AnyAsync(a => a.OpportunityId == dto.OpportunityId && a.UserId == uid))
                return Conflict("Already applied");

            var app = new JobApplication
            {
                OpportunityId = dto.OpportunityId,
                UserId = uid.Value,
                Status = 0,
                AppliedAt = DateTime.UtcNow,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name,
                CoverLetter = string.IsNullOrWhiteSpace(dto.CoverLetter) ? null : dto.CoverLetter,
                PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber,
                LinkedInUrl = string.IsNullOrWhiteSpace(dto.LinkedInUrl) ? null : dto.LinkedInUrl,
                PortfolioUrl = string.IsNullOrWhiteSpace(dto.PortfolioUrl) ? null : dto.PortfolioUrl,
                YearsOfExperience = dto.YearsOfExperience,
                OpenToRemote = dto.OpenToRemote,
                SelectedJobRoleIds = string.IsNullOrWhiteSpace(dto.SelectedJobRoleIds) ? null : dto.SelectedJobRoleIds
            };

            await _db.JobApplications.AddAsync(app);
            await _db.SaveChangesAsync();

            // Best-effort notification — failure here must not fail the application submission
            try
            {
                var opportunity = await _db.Opportunities.FindAsync(dto.OpportunityId);
                if (opportunity != null)
                {
                    var notifyUserId = opportunity.CreatorId ?? 0;
                    if (notifyUserId > 0)
                    {
                        await _db.Notifications.AddAsync(new Notification
                        {
                            UserId = notifyUserId,
                            ActorUserId = uid.Value,
                            Type = "JobApplied",
                            Payload = $"{{\"applicationId\":{app.JobApplicationId}}}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Best-effort notification failed for application {AppId} — application was still saved.", app.JobApplicationId);
            }

            return CreatedAtAction(nameof(Get), new { id = app.JobApplicationId }, app);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var app = await _db.JobApplications
                .Include(a => a.Opportunity).ThenInclude(o => o.Company)
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.JobApplicationId == id);
            if (app == null) return NotFound();

            // Allow the applicant themselves, admins, or company members to view
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0" && app.UserId != uid.Value)
            {
                // Check if requester is an employer-member of the opportunity's company
                var companyId = app.Opportunity?.CompanyId;
                if (companyId != null)
                {
                    var isMember = await _db.CompanyMembers
                        .AnyAsync(cm => cm.CompanyId == companyId.Value && cm.UserId == uid.Value);
                    if (!isMember) return Forbid();
                }
                else
                {
                    return Forbid();
                }
            }

            return Ok(app);
        }

        [HttpPost("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            var actorId = GetUserId();
            if (actorId == null) return Unauthorized();

            var app = await _db.JobApplications.FindAsync(id);
            if (app == null) return NotFound();

            app.Status = dto.NewStatus;
            app.UpdatedAt = DateTime.UtcNow;
            _db.JobApplications.Update(app);

            await _db.AuditLogs.AddAsync(new AuditLog { UserId = actorId.Value, Action = "UpdateApplicationStatus", TargetType = "JobApplication", TargetId = id, CreatedAt = DateTime.UtcNow });
            await _db.Notifications.AddAsync(new Notification
            {
                UserId = app.UserId,
                ActorUserId = actorId.Value,
                Type = "ApplicationStatusChanged",
                Payload = $"{{\"applicationId\":{id},\"newStatus\":{dto.NewStatus}}}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(app);
        }

        // POST: api/JobApplications/5/stage
        // Update application stage with audit trail
        [HttpPost("{id}/stage")]
        public async Task<IActionResult> UpdateStage(int id, [FromBody] UpdateStageDTO dto)
        {
            var actorId = GetUserId();
            if (actorId == null) return Unauthorized();

            var app = await _db.JobApplications
                .Include(a => a.Opportunity)
                .FirstOrDefaultAsync(a => a.JobApplicationId == id);
            
            if (app == null) return NotFound();

            // Verify user is authorized (company member or admin)
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0" && app.Opportunity?.CompanyId.HasValue == true)
            {
                var isMember = await _db.CompanyMembers
                    .AnyAsync(cm => cm.CompanyId == app.Opportunity.CompanyId.Value && cm.UserId == actorId.Value);
                if (!isMember) return Forbid();
            }

            var oldStage = app.Status;
            app.Status = dto.NewStage;
            app.UpdatedAt = DateTime.UtcNow;
            _db.JobApplications.Update(app);

            // Create audit log
            await _db.AuditLogs.AddAsync(new AuditLog
            {
                UserId = actorId.Value,
                Action = "UpdateApplicationStage",
                TargetType = "JobApplication",
                TargetId = id,
                Metadata = $"Stage changed from {oldStage} to {dto.NewStage}",
                CreatedAt = DateTime.UtcNow
            });

            // Notify applicant
            await _db.Notifications.AddAsync(new Notification
            {
                UserId = app.UserId,
                ActorUserId = actorId.Value,
                Type = "ApplicationStageChanged",
                Payload = $"{{\"applicationId\":{id},\"newStage\":{dto.NewStage}}}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(app);
        }

        // GET: api/JobApplications/company/5/pipeline
        // Get all applications for a company grouped by stage
        [HttpGet("company/{companyId}/pipeline")]
        [Authorize(Roles = "0,2")]
        public async Task<IActionResult> GetCompanyPipeline(int companyId)
        {
            var actorId = GetUserId();
            if (actorId == null) return Unauthorized();

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0")
            {
                var isMember = await _db.CompanyMembers
                    .AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == actorId.Value);
                if (!isMember) return Forbid();
            }

            var applications = await _db.JobApplications
                .Include(a => a.Opportunity)
                .Include(a => a.User)
                .Where(a => a.Opportunity.CompanyId == companyId)
                .Select(a => new
                {
                    a.JobApplicationId,
                    a.OpportunityId,
                    OpportunityTitle = a.Opportunity.Title,
                    a.UserId,
                    UserName = a.User.Name,
                    a.Status,
                    a.AppliedAt,
                    a.UpdatedAt
                })
                .ToListAsync();

            var pipeline = applications.GroupBy(a => a.Status)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Stage = g.Key,
                    StageName = GetStageName(g.Key),
                    Applications = g.ToList()
                });

            return Ok(pipeline);
        }

        // GET: api/jobapplications/for-company/{companyId}
        // Returns all applications for a company's opportunities — accessible by admin (role=0)
        // and by employers (role=2) who are verified company members.
        [HttpGet("for-company/{companyId}")]
        [Authorize(Roles = "0,2")]
        public async Task<IActionResult> ForCompany(int companyId, int? opportunityId = null)
        {
            var actorId = GetUserId();
            if (actorId == null) return Unauthorized();

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0")
            {
                var isMember = await _db.CompanyMembers
                    .AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == actorId.Value);
                if (!isMember) return Forbid();
            }

            var query = _db.JobApplications
                .Include(a => a.Opportunity).ThenInclude(o => o.Company)
                .Include(a => a.User)
                .Where(a => a.Opportunity != null && a.Opportunity.CompanyId == companyId);

            if (opportunityId.HasValue)
                query = query.Where(a => a.OpportunityId == opportunityId.Value);

            var list = await query
                .OrderByDescending(a => a.AppliedAt)
                .ToListAsync();

            return Ok(list);
        }

        // GET: api/jobapplications (admin only — full list)
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0") return Forbid();

            var list = await _db.JobApplications
                .Include(a => a.Opportunity).ThenInclude(o => o.Company)
                .Include(a => a.User)
                .OrderByDescending(a => a.AppliedAt)
                .ToListAsync();

            return Ok(list);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var app = await _db.JobApplications.FindAsync(id);
            if (app == null) return NotFound();

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0" && app.UserId != uid.Value) return Forbid();

            _db.JobApplications.Remove(app);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> ForUser(int userId)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (uid != userId && !User.IsInRole("0")) return Forbid();

            var list = await _db.JobApplications
                .Include(a => a.Opportunity).ThenInclude(o => o.Company)
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.AppliedAt)
                .ToListAsync();

            return Ok(list);
        }

        private int? GetUserId()
        {
            var sid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(sid, out var id) ? id : (int?)null;
        }

        private string GetStageName(byte stage)
        {
            return stage switch
            {
                0 => "Applied",
                1 => "Screening",
                2 => "Interview",
                3 => "Offer",
                4 => "Hired",
                5 => "Rejected",
                _ => "Unknown"
            };
        }
    }
}