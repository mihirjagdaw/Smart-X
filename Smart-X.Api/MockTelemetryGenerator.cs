namespace Smart_X.Api
{
    public static class MockTelemetryGenerator
    {
        public static float[][] GenerateBatches(int deviceCount, int minReadingsPerBatch, int maxReadingsPerBatch)
        {
            var random = new Random();
            var batches = new float[deviceCount][]; // array of devices
            // i ~ index of devices
            for (int i = 0; i < deviceCount; i++)
            {
                int readingCount = random.Next(minReadingsPerBatch, maxReadingsPerBatch + 1);
                batches[i] = new float[readingCount]; // current device's readings

                // j ~ index of readings
                for (int j = 0; j < readingCount; j++)
                {
                    // random value for sensor reading (this case it is nighttime canopy temperature, °C, for a vineyard block)
                    batches[i][j] = (float)(random.NextDouble() * 20) + 11;
                }

            }
            return batches;
        }
    }
}
