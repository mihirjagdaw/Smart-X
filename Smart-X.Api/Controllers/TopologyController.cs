using Microsoft.AspNetCore.Mvc;
using Smart_X.Api.Network;

namespace Smart_X.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TopologyController : ControllerBase
    {
        private readonly TopologyService _topology;

        public TopologyController(TopologyService topology)
        {
            _topology = topology;
        }

        /// <summary>
        /// The mesh: every device with its position, status and route, every link, the backbone, and the heal log.
        /// root (a MAC address) asks "who is connected?" from that device instead of the gateway; subnet (for
        /// example Estate/Block A) adds detail for one part of the estate. since is the version the caller already
        /// holds for this same subnet and root: if the mesh has not changed, the answer is 204 with no body, which
        /// saves the caller downloading and parsing the whole mesh every poll.
        /// </summary>
        [HttpGet]
        public IActionResult Get(string? subnet, string? root, long? since)
        {
            // Read the version before the analysis, so the body is never older than the version it is labelled with.
            var version = _topology.Version;
            // shortcut: the version restarts at 1 when the API restarts, so a client that held the same number from before would keep its old picture; give the version a per-run id if that ever matters.
            if (since == version) return NoContent();

            try
            {
                return Ok(TopologyReport.Build(_topology.Current, _topology.HealLog(), version, subnet, root));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>One device's route to the gateway, the cheapest alternative, and where it would hop if its next hop failed.</summary>
        [HttpGet("route/{mac}")]
        public IActionResult GetRoute(string mac)
        {
            try
            {
                return Ok(TopologyReport.Route(_topology.Current, mac));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }
}
