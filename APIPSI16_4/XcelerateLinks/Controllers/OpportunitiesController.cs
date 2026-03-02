using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using APIPSI16.Models;
using APIPSI16.Services;

namespace XcelerateLinks.Mvc.Controllers
{
    public class OpportunitiesController : ApiControllerBase
    {
        private readonly ILogger<OpportunitiesController> _logger;

        public OpportunitiesController(IHttpClientFactory httpFactory, ILogger<OpportunitiesController> logger, ISessionService sessionService)
            : base(httpFactory, sessionService)
        {
            _logger = logger;
        }

        // Role-dispatched: admin → Index (table), user/employer → UserIndex (job search)
        public async Task<IActionResult> Index(string? q = null, string? location = null, byte? employmentType = null, byte? remoteOption = null, bool recommended = false)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            var client = CreateAuthorizedClient();
            var resp = await client.GetAsync("api/opportunities");
            if (!resp.IsSuccessStatusCode)
            {
                ViewBag.Error = await SafeReadStringAsync(resp) ?? "Unable to load opportunities.";
                return View(IsAdmin() ? "Index" : "UserIndex", Array.Empty<Opportunity>());
            }

            IEnumerable<Opportunity> opportunities = await resp.Content.ReadFromJsonAsync<IEnumerable<Opportunity>>()
                                                      ?? Array.Empty<Opportunity>();

            if (IsAdmin())
                return View(opportunities);

            // User / employer: apply filters
            if (!string.IsNullOrWhiteSpace(q))
                opportunities = opportunities.Where(o =>
                    (o.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (o.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(location))
                opportunities = opportunities.Where(o =>
                    (o.Location ?? "").Contains(location, StringComparison.OrdinalIgnoreCase));

            if (employmentType.HasValue)
                opportunities = opportunities.Where(o => o.EmploymentType == employmentType.Value);

            if (remoteOption.HasValue)
                opportunities = opportunities.Where(o => o.RemoteOption == remoteOption.Value);

            // Load recommended if requested
            IEnumerable<Opportunity>? recommendedOpps = null;
            if (recommended)
            {
                var recResp = await client.GetAsync("api/opportunities/recommended");
                if (recResp.IsSuccessStatusCode)
                    recommendedOpps = await recResp.Content.ReadFromJsonAsync<IEnumerable<Opportunity>>() ?? Array.Empty<Opportunity>();
            }

            ViewBag.Q = q;
            ViewBag.Location = location;
            ViewBag.EmploymentType = employmentType;
            ViewBag.RemoteOption = remoteOption;
            ViewBag.Recommended = recommended;
            ViewBag.RecommendedList = recommendedOpps;

            return View("UserIndex", opportunities);
        }

        public async Task<IActionResult> Browse(string? q = null, string? location = null, byte? employmentType = null, byte? remoteOption = null)
            => await Index(q, location, employmentType, remoteOption);

        public async Task<IActionResult> Details(int id)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            var client = CreateAuthorizedClient();
            var resp = await client.GetAsync($"api/opportunities/{id}");
            if (!resp.IsSuccessStatusCode)
                return RedirectToAction(nameof(Index));

            var opportunity = await resp.Content.ReadFromJsonAsync<Opportunity>();
            if (opportunity == null) return RedirectToAction(nameof(Index));

            // Load job roles for tag display
            var jrResp = await client.GetAsync("api/users/lookups/jobroles");
            if (jrResp.IsSuccessStatusCode)
                ViewBag.JobRoles = await jrResp.Content.ReadFromJsonAsync<IEnumerable<XcelerateLinks.Mvc.Controllers.UsersController.LookupItem>>() ?? Array.Empty<XcelerateLinks.Mvc.Controllers.UsersController.LookupItem>();
            else
                ViewBag.JobRoles = Array.Empty<XcelerateLinks.Mvc.Controllers.UsersController.LookupItem>();

            return View(opportunity);
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? companyId = null)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            var model = new Opportunity();
            var userId = GetCurrentUserId();
            if (userId.HasValue)
                model.CreatorId = userId.Value;

            // Pre-fill company if navigating from company Manage page
            if (companyId.HasValue)
                model.CompanyId = companyId.Value;

            await LoadDropdownsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Opportunity model)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                await LoadDropdownsAsync();
                return View(model);
            }
            model.CreatorId ??= GetCurrentUserId();

            var client = CreateAuthorizedClient();
            var resp = await client.PostAsJsonAsync("api/opportunities", model);
            if (!resp.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await SafeReadStringAsync(resp) ?? "Unable to create opportunity.");
                await LoadDropdownsAsync();
                return View(model);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            var client = CreateAuthorizedClient();
            var resp = await client.GetAsync($"api/opportunities/{id}");
            if (!resp.IsSuccessStatusCode)
                return RedirectToAction(nameof(Index));

            var opportunity = await resp.Content.ReadFromJsonAsync<Opportunity>();
            if (opportunity == null) return RedirectToAction(nameof(Index));

            await LoadDropdownsAsync();
            return View(opportunity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Opportunity model)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            if (id != model.Id) return RedirectToAction(nameof(Index));
            if (!ModelState.IsValid)
            {
                await LoadDropdownsAsync();
                return View(model);
            }

            var client = CreateAuthorizedClient();
            var resp = await client.PutAsJsonAsync($"api/opportunities/{id}", model);
            if (!resp.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await SafeReadStringAsync(resp) ?? "Unable to update opportunity.");
                await LoadDropdownsAsync();
                return View(model);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            var client = CreateAuthorizedClient();
            var resp = await client.GetAsync($"api/opportunities/{id}");
            if (!resp.IsSuccessStatusCode)
                return RedirectToAction(nameof(Index));

            var opportunity = await resp.Content.ReadFromJsonAsync<Opportunity>();
            if (opportunity == null) return RedirectToAction(nameof(Index));
            return View(opportunity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            var client = CreateAuthorizedClient();
            var resp = await client.DeleteAsync($"api/opportunities/{id}");
            if (!resp.IsSuccessStatusCode)
                return RedirectToAction(nameof(Delete), new { id });

            return RedirectToAction(nameof(Index));
        }

        // Helper: load companies + users for dropdowns; for employers, restrict to their companies
        private async Task LoadDropdownsAsync()
        {
            var client = CreateAuthorizedClient();
            var isEmployer = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "2";

            // Companies
            if (isEmployer)
            {
                var myCompResp = await client.GetAsync("api/users/me/companies");
                if (myCompResp.IsSuccessStatusCode)
                {
                    var list = await myCompResp.Content.ReadFromJsonAsync<IEnumerable<CompanyDropItem>>();
                    ViewBag.Companies = list ?? Array.Empty<CompanyDropItem>();
                    ViewBag.IsEmployer = true;
                }
                else
                {
                    ViewBag.Companies = Array.Empty<CompanyDropItem>();
                    ViewBag.IsEmployer = true;
                }
            }
            else
            {
                var compResp = await client.GetAsync("api/companies");
                if (compResp.IsSuccessStatusCode)
                {
                    var companies = await compResp.Content.ReadFromJsonAsync<IEnumerable<Company>>();
                    ViewBag.Companies = companies?.Select(c => new CompanyDropItem { CompanyId = c.CompanyId, CompanyName = c.Name })
                                         ?? Array.Empty<CompanyDropItem>();
                }
                else
                {
                    ViewBag.Companies = Array.Empty<CompanyDropItem>();
                }
            }

            // Users (for CreatorId dropdown – admin only)
            if (IsAdmin())
            {
                var usersResp = await client.GetAsync("api/users");
                if (usersResp.IsSuccessStatusCode)
                {
                    var users = await usersResp.Content.ReadFromJsonAsync<IEnumerable<APIPSI16.Models.DTOs.UserDTO>>();
                    ViewBag.Users = users ?? Array.Empty<APIPSI16.Models.DTOs.UserDTO>();
                }
                else
                {
                    ViewBag.Users = Array.Empty<APIPSI16.Models.DTOs.UserDTO>();
                }
            }

            // Job roles
            var jobRolesResp = await client.GetAsync("api/users/lookups/jobroles");
            if (jobRolesResp.IsSuccessStatusCode)
            {
                var jr = await jobRolesResp.Content.ReadFromJsonAsync<IEnumerable<XcelerateLinks.Mvc.Controllers.UsersController.LookupItem>>();
                ViewBag.JobRoles = jr ?? Array.Empty<XcelerateLinks.Mvc.Controllers.UsersController.LookupItem>();
            }
            else
            {
                ViewBag.JobRoles = Array.Empty<XcelerateLinks.Mvc.Controllers.UsersController.LookupItem>();
            }
        }

        public record CompanyDropItem(int CompanyId = 0, string? CompanyName = null);
    }
}
