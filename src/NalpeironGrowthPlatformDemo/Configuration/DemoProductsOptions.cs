using System.ComponentModel.DataAnnotations;

namespace NalpeironGrowthPlatformDemo.Configuration;

public sealed class DemoProductsOptions
{
    public const string SectionName = "Products";

    public string Eyebrow { get; set; } = "Products";

    public string Heading { get; set; } = "Select a product";

    public string Description { get; set; } = "Choose the product path for this sales walkthrough.";

    public List<DemoProductOptions> Items { get; set; } = [];
}

public sealed class DemoProductOptions
{
    [Required] public string Key { get; set; } = "";

    [Required] public string Name { get; set; } = "";

    [Required] public string PlatformName { get; set; } = "";

    public string Description { get; set; } = "";

    [Required] public string Path { get; set; } = "";

    [Required] public string ButtonLabel { get; set; } = "";

    public string LogoPath { get; set; } = "";

    public string AccentColor { get; set; } = "#4f46e5";

    public bool ShowInTopNav { get; set; } = true;

    public List<DemoProductVariantOptions> Variants { get; set; } = [];
}

public sealed class DemoProductVariantOptions
{
    [Required] public string Label { get; set; } = "";

    [Required] public string Path { get; set; } = "";

    [Required] public string LogoPath { get; set; } = "";

    public BillingSystem? BillingSystem { get; set; }
}