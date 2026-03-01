using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APIPSI16.Data;
using APIPSI16.Models;

namespace APIPSI16.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserSkillsController : ControllerBase
    {
        private readonly xcleratesystemslinks_SampleDBContext _db;
        public UserSkillsController(xcleratesystemslinks_SampleDBContext db) => _db = db;

        [HttpGet("user/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetForUser(int userId) =>
            Ok(await _db.UserSkills.Where(us => us.UserId == userId).Include(us => us.Skill).ToListAsync());

        [HttpPost]
        public async Task<IActionResult> Add([FromBody] UserSkill us)
        {
            if (await _db.UserSkills.AnyAsync(x => x.UserId == us.UserId && x.SkillId == us.SkillId))
                return Conflict("Already added");

            us.AddedAt = DateTime.UtcNow;
            await _db.UserSkills.AddAsync(us);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetForUser), new { userId = us.UserId }, us);
        }

        [HttpPost("{userSkillId}/endorse")]
        public async Task<IActionResult> Endorse(int userSkillId)
        {
            var endorserId = GetUserId();
            if (endorserId == null) return Unauthorized();

            var us = await _db.UserSkills.FindAsync(userSkillId);
            if (us == null) return NotFound();

            if (us.UserId == endorserId) return BadRequest("Cannot endorse yourself.");

            var already = await _db.SkillEndorsements.AnyAsync(se => se.UserSkillId == userSkillId && se.EndorserUserId == endorserId);
            if (already) return Conflict("Already endorsed");

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await _db.SkillEndorsements.AddAsync(new SkillEndorsement
                {
                    UserSkillId = userSkillId,
                    EndorserUserId = endorserId.Value,
                    CreatedAt = DateTime.UtcNow
                });

                us.EndorsementCount = us.EndorsementCount + 1;
                _db.UserSkills.Update(us);
                await _db.SaveChangesAsync();

                await _db.Notifications.AddAsync(new Notification
                {
                    UserId = us.UserId,
                    ActorUserId = endorserId.Value,
                    Type = "SkillEndorsed",
                    Payload = $"{{\"userSkillId\":{userSkillId}}}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return Ok(new { Status = "Endorsed" });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Remove(int id)
        {
            var us = await _db.UserSkills.FindAsync(id);
            if (us == null) return NotFound();
            _db.UserSkills.Remove(us);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private int? GetUserId()
        {
            var sid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(sid, out var id) ? id : (int?)null;
        }
    }
}