using System.ComponentModel.DataAnnotations;

namespace Smart_X.Client.Models
{
    public class TelemetryFormModel
    {
        [Required(ErrorMessage = "Please select a device.")]
        public string DeviceId { get; set; } = "";

        public string ValueType { get; set; } = "float";

        public float? FloatValue { get; set; }

        public int? IntValue { get; set; }

        public bool BoolValue { get; set; }
    }
}
