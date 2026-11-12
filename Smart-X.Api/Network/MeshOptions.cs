namespace Smart_X.Api.Network
{
    /// <summary>
    /// The physical assumptions behind the mesh model. Distances are in metres, energy in nanojoules per bit.
    /// The radio costs follow the first-order radio energy model that wireless-sensor-network research uses:
    /// sending a bit costs electronics energy plus an amplifier term that grows with the square of the distance,
    /// and receiving costs electronics energy. Set under "Mesh" in appsettings.json.
    /// </summary>
    public record MeshOptions
    {
        /// <summary>How far apart neighbouring devices are placed on the map.</summary>
        public double SpacingMetres { get; init; } = 40;

        /// <summary>Two devices can hear each other when they are no further apart than this.</summary>
        public double RadioRangeMetres { get; init; } = 80;

        /// <summary>Energy the transmitter and the receiver each spend per bit, whatever the distance.</summary>
        public double ElectronicsNjPerBit { get; init; } = 50;

        /// <summary>Extra transmit energy per bit per square metre, to overcome signal loss.</summary>
        public double AmplifierNjPerBitPerSquareMetre { get; init; } = 0.1;

        /// <summary>
        /// The energy for one bit to cross one hop: sender electronics, receiver electronics, and the amplifier.
        /// Because the amplifier term is squared, one long hop costs more than two short ones once the hop is
        /// longer than sqrt(4 * electronics / amplifier), about 45 m with the defaults. That is what makes
        /// relaying through neighbours the power-efficient choice in a mesh.
        /// </summary>
        public double HopCost(double distanceMetres) =>
            2 * ElectronicsNjPerBit + AmplifierNjPerBitPerSquareMetre * distanceMetres * distanceMetres;
    }
}
