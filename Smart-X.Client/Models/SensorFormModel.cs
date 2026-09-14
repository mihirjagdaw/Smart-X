using System.ComponentModel.DataAnnotations;
using Smart_X.Shared;

namespace Smart_X.Client.Models
{
    public class SensorFormModel
    {
        [Required(ErrorMessage = "MAC address is required.")]
        [RegularExpression(@"^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$", ErrorMessage = "Format: AA:BB:CC:DD:EE:FF")]
        public string MacAddress { get; set; } = "";

        [Required(ErrorMessage = "Location is required.")]
        public string Location { get; set; } = "";

        [Required(ErrorMessage = "Description is required.")]
        public string Description { get; set; } = "";

        public SensorCategory Category { get; set; }
    }
}
