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
        /// example Estate/Block A) adds detail for one part of the estate.
        /// </summary>
        [HttpGet]
        public IActionResult Get(string? subnet, string? root)
        {
            try
            {
                return Ok(TopologyReport.Build(_topology.Current, _topology.HealLog(), _topology.Version, subnet, root));
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
