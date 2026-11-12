namespace Smart_X.Shared.DTOs
{
    // Status is one of: Gateway, Online, Critical (online with a critical reading), Down (has gone silent),
    // Isolated (online, but every route to the gateway runs through devices that are down).
    // HopsFromRoot is the fewest hops from the chosen root; RouteHops, NextHop and RouteCost describe the
    // device's power-efficient route to the gateway along the minimum spanning tree.
    public record TopologyNode(
        string Id,
        string Description,
        SensorCategory? Category,
        string Location,
        string Zone,
        double X,
        double Y,
        string Status,
        int? HopsFromRoot,
        int? RouteHops,
        string? NextHop,
        double? RouteCost,
        bool InSubnet);

    // Cost is nanojoules per bit for one hop. OnMst means the link is part of the power-efficient backbone;
    // Backhaul means it is longer than the normal radio range; Down means one end has gone silent.
    public record TopologyLink(string A, string B, double DistanceMetres, double Cost, bool OnMst, bool Backhaul, bool Down);

    public record TopologySummary(
        int Nodes,
        int Online,
        int Down,
        int Isolated,
        int Links,
        int MstLinks,
        double MstTotalCost,
        double AllLinksCost,
        double SavingPercent,
        double CollectCostViaMst,
        double CollectCostViaCheapest,
        double ExtraPercentViaMst,
        double KruskalTotalCost,
        bool MstVerified);

    // How one subnet is doing: members, how many still reach the root, how many are down or cut off, and how
    // many separate islands the subnet's own healthy members form.
    public record SubnetDetail(string Name, int Members, int Connected, int Down, int CutOff, int Islands);

    // Kind is one of: Joined, Left, Down, Up, CutOff, Reconnected, Rerouted.
    public record HealEvent(DateTimeOffset At, string Kind, string Mac, string Message);

    public record TopologyResponse(
        string Root,
        string? Subnet,
        List<string> Subnets,
        TopologySummary Summary,
        SubnetDetail? SubnetDetail,
        List<TopologyNode> Nodes,
        List<TopologyLink> Links,
        List<HealEvent> HealLog,
        long Version);

    public record RouteHop(string Id, string Description, double LinkCost);

    // A route from a device to the gateway, hop by hop, with what it costs in nanojoules per bit.
    public record RoutePath(List<RouteHop> Hops, double TotalCost);

    // What the device would do if the first hop of its route failed. Route is null when it would have no way out.
    public record BackupRoute(string FailedHop, RoutePath? Route);

    public record NeighbourInfo(string Id, string Description, string Status, double LinkCost, double DistanceMetres, string? NextHop, double? RouteCost);

    public record RouteResponse(
        string Id,
        string Description,
        string Status,
        string Message,
        int? FewestHops,
        RoutePath? PowerEfficientRoute,
        RoutePath? CheapestRoute,
        double? ExtraPercentViaMst,
        BackupRoute? Backup,
        List<NeighbourInfo> Neighbours);
}
