#:project Smart-X.Shared/Smart-X.Shared.csproj
using Smart_X.Shared;

int failures = 0;

void Check(string label, string path, bool expected)
{
    var actual = DeploymentNode.ValidateNode(DeploymentNode.FromPath(path));
    var ok = actual == expected;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-28} path=\"{path}\" expected={expected} actual={actual}");
    if (!ok) failures++;
}

Check("single segment", "Estate", true);
Check("multi-segment valid", "Estate/Block A/Row 3", true);
Check("deep valid (5 levels)", "Estate/Block A/Row 3/Vine 12/Sensor", true);
Check("empty segment (double slash)", "Estate//Row 3", false);
Check("whitespace-only segment", "Estate/   /Row 3", false);
Check("trailing slash", "Estate/Block A/", false);
Check("empty path", "", false);
Check("null-ish path", "   ", false);

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"All checks passed." : $"{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;
