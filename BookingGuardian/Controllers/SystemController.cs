using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingGuardian.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("system")]
    public class SystemController : Controller
    {
        [HttpGet("tester")]
        public IActionResult Tester()
        {
            return View();
        }
    }
}
