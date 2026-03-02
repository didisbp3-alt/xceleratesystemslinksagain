using APIPSI16.Data;
using APIPSI16.Filters;
using APIPSI16.Models;
using APIPSI16.Models.DTOs;
using APIPSI16.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace APIPSI16.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require JWT for all actions
    public class UsersController : ControllerBase
    {
        private readonly xcleratesystemslinks_SampleDBContext _context;
        private readonly IFileStorageService _fileStorage;

        public UsersController(xcleratesystemslinks_SampleDBContext context, IFileStorageService fileStorage)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        }

        // GET: api/Users
        // - If no filters supplied: Admin (0) only -> returns all users
        // - If filters supplied (jobPreference and/or nationality): Admin (0) and Employer (2) can query
        [HttpGet]
        public async Task<IActionResult> GetUsers([FromQuery] int? jobPreference, [FromQuery] int? nationality)
        {
            var userRole = GetCurrentUserRole();

            if (jobPreference.HasValue || nationality.HasValue)
            {
                if (userRole != "0" && userRole != "2")
                    return Forbid();

                IQueryable<User> q = _context.Users;

                if (jobPreference.HasValue)
                    q = q.Where(u => u.JobPreference == jobPreference.Value);

                if (nationality.HasValue)
                    q = q.Where(u => u.Nationality == nationality.Value);

                var filtered = await q
                    .Select(u => new UserDTO
                    {
                        UserId = u.UserId,
                        Name = u.Name,
                        Email = u.Email,
                        Nationality = u.Nationality,
                        JobPreference = u.JobPreference,
                        ProfileBio = u.ProfileBio,
                        DoB = u.DoB,
                        PhoneNumber = u.PhoneNumber,
                        ProfilePictureUrl = u.ProfilePictureUrl
                    })
                    .ToListAsync();

                return Ok(filtered);
            }

            if (userRole != "0")
                return Forbid();

            var users = await _context.Users
                .Select(u => new UserDTO
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Email = u.Email,
                    Nationality = u.Nationality,
                    JobPreference = u.JobPreference,
                    ProfileBio = u.ProfileBio,
                    DoB = u.DoB,
                    PhoneNumber = u.PhoneNumber,
                    ProfilePictureUrl = u.ProfilePictureUrl
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var user = await _context.Users
                .Where(u => u.UserId == id)
                .Select(u => new UserDTO
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Email = u.Email,
                    Nationality = u.Nationality,
                    JobPreference = u.JobPreference,
                    ProfileBio = u.ProfileBio,
                    DoB = u.DoB,
                    PhoneNumber = u.PhoneNumber,
                    Role = u.Role,
                    ProfilePictureUrl = u.ProfilePictureUrl,
                    BannerUrl = u.BannerUrl
                })
                .FirstOrDefaultAsync();

            if (user == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userRole != "0" && currentUserId != id)
                return Forbid();

            return Ok(user);
        }

        // GET: api/Users/5/profile
        // Get complete user profile with skills, experiences, and educations
        [HttpGet("{id}/profile")]
        public async Task<IActionResult> GetUserProfile(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            var skills = await _context.UserSkills
                .Where(us => us.UserId == id)
                .Include(us => us.Skill)
                .Select(us => new SkillDTO
                {
                    SkillId = us.SkillId,
                    Name = us.Skill.Name,
                    EndorsementCount = _context.SkillEndorsements.Count(se => se.UserSkillId == us.UserSkillId)
                })
                .ToListAsync();

            var experiences = await _context.ProfileExperiences
                .Where(pe => pe.UserId == id)
                .Select(pe => new ProfileExperienceDTO
                {
                    ExperienceId = pe.ExperienceId,
                    JobTitle = pe.Title,
                    CompanyName = pe.CompanyName,
                    StartDate = pe.StartDate,
                    EndDate = pe.EndDate,
                    Description = pe.Description
                })
                .ToListAsync();

            var educations = await _context.ProfileEducations
                .Where(pe => pe.UserId == id)
                .Select(pe => new ProfileEducationDTO
                {
                    EducationId = pe.EducationId,
                    Institution = pe.School,
                    Degree = pe.Degree,
                    FieldOfStudy = pe.FieldOfStudy,
                    StartDate = pe.StartYear.HasValue ? new DateOnly(pe.StartYear.Value, 1, 1) : (DateOnly?)null,
                    EndDate = pe.EndYear.HasValue ? new DateOnly(pe.EndYear.Value, 1, 1) : (DateOnly?)null
                })
                .ToListAsync();

            var profileDto = new UserProfileDTO
            {
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Nationality = user.Nationality,
                JobPreference = user.JobPreference,
                ProfileBio = user.ProfileBio,
                DoB = user.DoB,
                ProfilePictureUrl = user.ProfilePictureUrl,
                BannerUrl = user.BannerUrl,
                Role = user.Role,
                Skills = skills,
                Experiences = experiences,
                Educations = educations
            };

            return Ok(profileDto);
        }

        // POST: api/Users/5/upload-picture
        // Upload profile picture for a user
        [HttpPost("{id}/upload-picture")]
        [SwaggerFileUpload]
        public async Task<IActionResult> UploadProfilePicture(int id, IFormFile file)
        {
            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userRole != "0" && currentUserId != id)
                return Forbid();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (!_fileStorage.ValidateImageFile(file, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            try
            {
                // Delete old profile picture if exists
                if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
                {
                    await _fileStorage.DeleteFileAsync(user.ProfilePictureUrl);
                }

                // Save new profile picture
                var fileUrl = await _fileStorage.SaveFileAsync(file, "profiles");
                user.ProfilePictureUrl = fileUrl;
                await _context.SaveChangesAsync();

                return Ok(new { success = true, fileUrl = fileUrl, message = "Profile picture uploaded successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error uploading file: {ex.Message}" });
            }
        }

        // POST: api/Users/5/upload-banner
        // Upload banner image for a user
        [HttpPost("{id}/upload-banner")]
        [SwaggerFileUpload]
        public async Task<IActionResult> UploadBanner(int id, IFormFile file)
        {
            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userRole != "0" && currentUserId != id)
                return Forbid();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (!_fileStorage.ValidateImageFile(file, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            try
            {
                if (!string.IsNullOrEmpty(user.BannerUrl))
                {
                    await _fileStorage.DeleteFileAsync(user.BannerUrl);
                }

                var fileUrl = await _fileStorage.SaveFileAsync(file, "banners");
                user.BannerUrl = fileUrl;
                await _context.SaveChangesAsync();

                return Ok(new { success = true, fileUrl = fileUrl, message = "Banner uploaded successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error uploading file: {ex.Message}" });
            }
        }

        // POST: api/Users/me/request-employer – user uploads documents to request employer role
        [HttpPost("me/request-employer")]
        [SwaggerFileUpload]
        public async Task<IActionResult> RequestEmployerRole(IFormFile? document, [FromForm] int? companyId = null)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized();

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null) return NotFound();

            // Store doc if provided (accepts images and PDFs)
            string? docUrl = null;
            if (document != null && document.Length > 0 && document.Length <= 5 * 1024 * 1024)
            {
                docUrl = await _fileStorage.SaveFileAsync(document, "employer-requests");
            }

            // Set pending employer status: Role = 3 means "Pending Employer"
            user.Role = 3;

            // Optionally join the company as pending member
            if (companyId.HasValue)
            {
                var alreadyMember = await _context.CompanyMembers
                    .AnyAsync(m => m.CompanyId == companyId.Value && m.UserId == uid.Value);
                if (!alreadyMember)
                {
                    _context.CompanyMembers.Add(new CompanyMember
                    {
                        CompanyId = companyId.Value,
                        UserId = uid.Value,
                        Role = 0, // 0 = pending, 1 = member, 2 = admin
                        StartDate = DateOnly.FromDateTime(DateTime.UtcNow)
                    });
                }
            }

            await _context.SaveChangesAsync();

            await _context.AuditLogs.AddAsync(new AuditLog
            {
                UserId = uid.Value,
                Action = "RequestEmployerRole",
                TargetType = "User",
                TargetId = uid.Value,
                CreatedAt = DateTime.UtcNow
            });

            // Create notifications for all admins so they see the pending request
            var adminIds = await _context.Users
                .Where(u => u.Role == 0)
                .Select(u => u.UserId)
                .ToListAsync();

            var now = DateTime.UtcNow;
            _context.Notifications.AddRange(adminIds.Select(adminId => new Notification
            {
                UserId = adminId,
                ActorUserId = uid.Value,
                Type = "EmployerRequest",
                Payload = uid.Value.ToString(),
                IsRead = false,
                CreatedAt = now
            }));

            await _context.SaveChangesAsync();

            return Ok(new { message = "Pedido submetido. Aguarda aprovação do administrador.", documentUrl = docUrl });
        }

        // POST: api/Users/{id}/approve-employer – admin/company-manager approves employer role
        [HttpPost("{id}/approve-employer")]
        [Authorize(Roles = "0,2")] // Admin or Employer
        public async Task<IActionResult> ApproveEmployerRole(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.Role = 2; // Employer
            await _context.SaveChangesAsync();

            await _context.AuditLogs.AddAsync(new AuditLog
            {
                UserId = GetCurrentUserId() ?? 0,
                Action = "ApproveEmployerRole",
                TargetType = "User",
                TargetId = id,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Ok(new { message = "Utilizador aprovado como empregador." });
        }

        // POST: api/Users/{id}/reject-employer – admin can reject
        [HttpPost("{id}/reject-employer")]
        [Authorize(Roles = "0")]
        public async Task<IActionResult> RejectEmployerRole(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.Role = 1; // Back to regular user
            await _context.SaveChangesAsync();

            return Ok(new { message = "Pedido de empregador rejeitado." });
        }

        // GET: api/Users/pending-employers – list users with Role = 3 (pending) for admin
        [HttpGet("pending-employers")]
        [Authorize(Roles = "0")]
        public async Task<IActionResult> GetPendingEmployers()
        {
            var pending = await _context.Users
                .Where(u => u.Role == 3)
                .Select(u => new UserDTO
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Email = u.Email,
                    ProfilePictureUrl = u.ProfilePictureUrl,
                    Role = u.Role
                })
                .ToListAsync();

            return Ok(pending);
        }

        // GET: api/Users/network – public user listing for the network/discovery page
        // Available to all authenticated users; returns only non-sensitive fields
        [HttpGet("network")]
        public async Task<IActionResult> GetNetworkUsers([FromQuery] string? search = null)
        {
            IQueryable<User> q = _context.Users.Where(u => u.Role != 0); // exclude admins

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(u => (u.Name ?? "").Contains(search) || (u.ProfileBio ?? "").Contains(search));

            var users = await q
                .Select(u => new UserDTO
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    ProfileBio = u.ProfileBio,
                    ProfilePictureUrl = u.ProfilePictureUrl,
                    BannerUrl = u.BannerUrl,
                    Role = u.Role
                })
                .ToListAsync();

            return Ok(users);
        }

        // GET: api/Users/me/companies – employer's company memberships
        [HttpGet("me/companies")]
        public async Task<IActionResult> GetMyCompanies()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized();

            var memberships = await _context.CompanyMembers
                .Where(cm => cm.UserId == uid.Value)
                .Include(cm => cm.Company)
                .Select(cm => new
                {
                    cm.CompanyId,
                    CompanyName = cm.Company.Name,
                    cm.Title,
                    cm.Role
                })
                .ToListAsync();

            return Ok(memberships);
        }

        // POST: api/Users
        // Only admins can create users via this endpoint.
        // (Self-registration should be done through /api/Auth/register)
        [HttpPost]
        [Authorize(Roles = "0")]
        public async Task<IActionResult> CreateUser([FromBody] User user)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetUser), new { id = user.UserId }, user);
        }

        // PUT: api/Users/5
        // Admins can update any user; users can update only their own record.
        // Non-admins cannot change Role or PasswordHash through this endpoint.
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] User updated)
        {
            if (id != updated.UserId) return BadRequest();

            var existing = await _context.Users.FindAsync(id);
            if (existing == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            // Only admin or owner can update
            if (userRole != "0" && existing.UserId != currentUserId)
                return Forbid();

            // Non-admins: only allow a subset of fields to be changed
            if (userRole != "0")
            {
                existing.Name = updated.Name;
                existing.PhoneNumber = updated.PhoneNumber;
                existing.Nationality = updated.Nationality;
                existing.JobPreference = updated.JobPreference;
                existing.ProfileBio = updated.ProfileBio;
                existing.DoB = updated.DoB;
                // Do NOT allow non-admins to change Email, Role, PasswordHash
            }
            else
            {
                // Admin can update most fields; don't automatically accept a plaintext password here
                existing.Name = updated.Name;
                existing.Email = updated.Email;
                existing.PhoneNumber = updated.PhoneNumber;
                existing.Nationality = updated.Nationality;
                existing.JobPreference = updated.JobPreference;
                existing.ProfileBio = updated.ProfileBio;
                existing.DoB = updated.DoB;
                existing.Role = updated.Role;
                // If you want admins to reset passwords, provide a dedicated endpoint that accepts a hashed password
            }

            try
            {
                _context.Entry(existing).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/Users/5
        // Admin only
        [HttpDelete("{id}")]
        [Authorize(Roles = "0")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(u => u.UserId == id);
        }

        private int? GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }

        private string? GetCurrentUserRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value;
        }
        // GET: api/users/lookups/nationalities
        [HttpGet("lookups/nationalities")]
        [AllowAnonymous]
        public async Task<IActionResult> GetNationalities()
        {
            var list = await _context.Nationalities
                .OrderBy(n => n.Name)
                .Select(n => new { n.NationalityId, n.Name })
                .ToListAsync();
            return Ok(list);
        }

        // GET: api/users/lookups/jobroles
        [HttpGet("lookups/jobroles")]
        [AllowAnonymous]
        public async Task<IActionResult> GetJobRoles()
        {
            var list = await _context.JobRoles
                .OrderBy(j => j.Name)
                .Select(j => new { j.JobRoleId, j.Name })
                .ToListAsync();
            return Ok(list);
        }

        // GET: api/users/stats – real-time platform stats
        [HttpGet("stats")]
        [AllowAnonymous]
        public async Task<IActionResult> GetStats()
        {
            var userCount = await _context.Users.CountAsync(u => u.Role != 0);
            var companyCount = await _context.Companies.CountAsync();
            var oppCount = await _context.Opportunities.CountAsync();
            var activeConnections = await _context.Connections.CountAsync(c => c.Status == 1);
            return Ok(new { UserCount = userCount, CompanyCount = companyCount, OppCount = oppCount, ActiveConnections = activeConnections });
        }

    }
}