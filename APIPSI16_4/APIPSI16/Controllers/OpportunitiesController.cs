    using APIPSI16.Data;
using APIPSI16.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace APIPSI16.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require JWT for all actions
    public class OpportunitiesController : ControllerBase
    {
        private readonly xcleratesystemslinks_SampleDBContext _context;

        public OpportunitiesController(xcleratesystemslinks_SampleDBContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // GET: api/Opportunities/recommended  – personalised for current user
        [HttpGet("recommended")]
        public async Task<IActionResult> GetRecommended()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized();

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null) return NotFound();

            var q = _context.Opportunities.Include(o => o.Company).AsQueryable();

            // Match on EmploymentType or SeniorityLevel based on user's job preference
            if (user.JobPreference.HasValue)
                q = q.Where(o => o.EmploymentType == (byte?)user.JobPreference.Value);

            var results = await q.Select(o => new
            {
                o.Id, o.Title, o.Location, o.EmploymentType, o.SeniorityLevel, o.RemoteOption,
                o.CompanyId, CompanyName = o.Company != null ? o.Company.Name : null
            }).ToListAsync();

            return Ok(results);
        }

        // GET: api/Opportunities
        // All authenticated users can view opportunities
        [HttpGet]
        public async Task<IActionResult> GetOpportunities()
        {
            var opportunities = await _context.Opportunities
                .Select(o => new
                {
                    o.Id,
                    o.Title,
                    o.Location,
                    o.EmploymentType,
                    o.SeniorityLevel,
                    o.RemoteOption,
                    o.CompanyId,
                    CompanyName = o.Company.Name
                })
                .ToListAsync();

            return Ok(opportunities);
        }

        // GET: api/Opportunities/5
        // All authenticated users can view opportunity details
        [HttpGet("{id}")]
        public async Task<IActionResult> GetOpportunity(int id)
        {
            var opportunity = await _context.Opportunities
                .Include(o => o.Company)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (opportunity == null) return NotFound();

            return Ok(opportunity);
        }

        // POST: api/Opportunities
        // Only admins and employers can create opportunities
        [HttpPost]
        [Authorize(Roles = "0,2")] // Admin or Employer
        public async Task<IActionResult> CreateOpportunity([FromBody] Opportunity opportunity)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            // Employers can only create opportunities for companies they're members of
            if (userRole == "2" && opportunity.CompanyId.HasValue)
            {
                if (!currentUserId.HasValue) return Unauthorized();

                var isMember = await _context.CompanyMembers
                    .AnyAsync(cm => cm.CompanyId == opportunity.CompanyId.Value && cm.UserId == currentUserId.Value);

                if (!isMember)
                    return Forbid("You can only create opportunities for companies you're a member of.");
            }

            _context.Add(opportunity);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetOpportunity), new { id = opportunity.Id }, opportunity);
        }

        // PUT: api/Opportunities/5
        // Admins can update any; employers can update their company's opportunities
        [HttpPut("{id}")]
        [Authorize(Roles = "0,2")] // Admin or Employer
        public async Task<IActionResult> UpdateOpportunity(int id, [FromBody] Opportunity opportunity)
        {
            if (id != opportunity.Id) return BadRequest();

            var existingOpp = await _context.Opportunities.FindAsync(id);
            if (existingOpp == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            // Employers can only update opportunities for companies they're members of
            if (userRole == "2" && existingOpp.CompanyId.HasValue)
            {
                if (!currentUserId.HasValue) return Unauthorized();

                var isMember = await _context.CompanyMembers
                    .AnyAsync(cm => cm.CompanyId == existingOpp.CompanyId.Value && cm.UserId == currentUserId.Value);

                if (!isMember)
                    return Forbid("You can only update opportunities for companies you're a member of.");
            }

            _context.Entry(existingOpp).State = EntityState.Detached;
            _context.Entry(opportunity).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OpportunityExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/Opportunities/5
        // Only admins can delete opportunities
        [HttpDelete("{id}")]
        [Authorize(Roles = "0")] // Admin only
        public async Task<IActionResult> DeleteOpportunity(int id)
        {
            var opportunity = await _context.Opportunities.FindAsync(id);
            if (opportunity == null) return NotFound();

            _context.Opportunities.Remove(opportunity);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool OpportunityExists(int id)
        {
            return _context.Opportunities.Any(o => o.Id == id);
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
    }
}