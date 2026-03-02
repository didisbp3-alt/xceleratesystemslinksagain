using Microsoft.AspNetCore.Mvc;
using APIPSI16.Services;

namespace XcelerateLinks.Mvc.Controllers
{
    public class SubscriptionsController : ApiControllerBase
    {
        public SubscriptionsController(IHttpClientFactory httpFactory, ISessionService sessionService)
            : base(httpFactory, sessionService)
        {
        }

        public async Task<IActionResult> Index()
        {
            if (!await ValidateSessionAsync())
                return RedirectToAction("Login", "Account");

            return View();
        }
    }
}
