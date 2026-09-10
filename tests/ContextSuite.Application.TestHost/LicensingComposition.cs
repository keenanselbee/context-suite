using ContextSuite.Core.Licensing;

namespace ContextSuite.Application;

// Public UI tests retain isolated local trial behavior and never contact Polar.
public partial class App
{
    private static partial ILicenseService? CreateProductionLicensing() => null;
}
