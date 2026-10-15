namespace Smart_X.Shared
{
    public enum Severity
    {
        Normal,
        Critical
    }

    public static class AlertRules
    {
        public const float EnvironmentalMin = 20f;
        public const float EnvironmentalMax = 80f;
        public const float PowerMax = 3000f;

        public static Severity Evaluate(SensorCategory category, float value) => category switch
        {
            SensorCategory.Environmental => value < EnvironmentalMin || value > EnvironmentalMax ? Severity.Critical : Severity.Normal,
            SensorCategory.PowerConsumption => value > PowerMax ? Severity.Critical : Severity.Normal,
            _ => Severity.Normal
        };
    }
}
