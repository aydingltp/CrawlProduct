namespace CrawlProduct.Models;

public class Product
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Sku { get; set; }
    public string? ParentSku { get; set; }
    public List<ProductAttribute> Attributes { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public decimal? OriginalPrice { get; set; }
    public decimal? DiscountedPrice { get; set; }
    public List<string> Images { get; set; }
}

public class TransformedProduct : Product
{
    public decimal Score { get; set; }
}

public class ProductAttribute
{
    public string? Key { get; set; }
    public string? Name { get; set; }
}