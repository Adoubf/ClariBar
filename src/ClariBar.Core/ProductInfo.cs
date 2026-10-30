using System.Reflection;

namespace ClariBar.Core;

public static class ProductInfo
{
    public static string DisplayVersion {get;}=typeof(ProductInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
