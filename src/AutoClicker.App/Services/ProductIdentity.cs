using System.Reflection;

namespace AutoClicker.App.Services;

public static class ProductIdentity
{
    public const string Name = "AutoClicker";
    public static string BundleIdentifier => typeof(ProductIdentity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "ApplicationBundleIdentifier").Value!;
}
