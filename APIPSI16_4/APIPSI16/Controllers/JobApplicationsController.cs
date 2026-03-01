using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        public JobApplicationsController(xcleratesystemslinks_SampleDBContext db) => _db = db;

        // POST api/jobapplications/apply
        // Payload: { OpportunityId: int, Name: "optional applicant-provided name (varchar(50))" }
        [HttpPost("apply")]
        public async Task<IActionResult> Apply([FromBody] ApplyDto dto)
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            if (dto.OpportunityId <= 0) return BadRequest("OpportunityId is required.");

            // optional: validate Name length
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
                CoverLetter = dto.CoverLetter,
                PhoneNumber = dto.PhoneNumber,
                LinkedInUrl = dto.LinkedInUrl,
                PortfolioUrl = dto.PortfolioUrl,
                YearsOfExperience = dto.YearsOfExperience,
                OpenToRemote = dto.OpenToRemote
            };

            await _db.JobApplications.AddAsync(app);
            await _db.SaveChangesAsync();

            var opportunity = await _db.Opportunities.FindAsync(dto.OpportunityId);
            if (opportunity != null)
            {
                // best-effort notify: if the opportunity has a CreatorId or CompanyId property, adjust accordingly
                var notifyUserId = opportunity.CreatorId ?? 0;
                if (notifyUserId != 0)
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

            return CreatedAtAction(nameof(Get), new { id = app.JobApplicationId }, app);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var app = await _db.JobApplications.FindAsync(id);
            if (app == null) return NotFound();
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

        // GET: api/jobapplications (admin: all, user: their own)
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var uid = GetUserId();
            if (uid == null) return Unauthorized();

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "0") return Forbid();

            var list = await _db.JobApplications
                .Include(a => a.Opportunity)
                .Include(a => a.User)
                .Select(a => new
                {
                    a.JobApplicationId,
                    a.OpportunityId,
                    OpportunityTitle = a.Opportunity.Title,
                    a.UserId,
                    UserName = a.User.Name,
                    a.Status,
                    a.AppliedAt,
                    a.UpdatedAt,
                    a.Name
                })
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
                .Where(a => a.UserId == userId)
                .Select(a => new
                {
                    a.JobApplicationId,
                    a.OpportunityId,
                    a.UserId,
                    a.Status,
                    a.AppliedAt,
                    a.UpdatedAt,
                    a.Name
                })
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