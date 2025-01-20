using System.Text.Json;
using System.Text.RegularExpressions;
using CrawlProduct.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrawlProduct.Api;

[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    private readonly AzureOpenAISettings _settings;
    private readonly HttpClient _httpClient;
    private static readonly List<Product> _crawledProducts = new();
    private static readonly List<TransformedProduct> _transformedProducts = new();
    
    public ProductController(IOptions<AzureOpenAISettings> settings)
    {
        _settings = settings.Value;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.Endpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);
    }
    
    [HttpGet("crawled-products")]
    public Task<List<Product>> CrawledProduct()
    {
        return Task.FromResult(_crawledProducts);
    } 
    [HttpGet("transform-products")]
    public Task<List<TransformedProduct>> TransformProducts()
    {
        return Task.FromResult(_transformedProducts);
    }

    [HttpPost("crawl-product-openai")]
    public async Task<IActionResult> CrawlOpenAi([FromQuery] string promptUrl)
    {
        var requestBody = new
        {
            messages = new[]
            {
                new { role = "system", content = """
                                                 Sen bir e-ticaret ürün veri dönüştürücüsüsün. 
                                                                 Sana gelen veriyi kesinlikle aşağıdaki JSON formatında dönüştürmelisin:
                                                                 {
                                                                     "name": string,
                                                                     "description": string,
                                                                     "sku": string,
                                                                     "parentSku": string,
                                                                     "attributes": [
                                                                         {
                                                                             "key": string,
                                                                             "name": string
                                                                         }
                                                                     ],
                                                                     "category": string,
                                                                     "brand": string,
                                                                     "originalPrice": decimal,
                                                                     "discountedPrice": decimal,
                                                                     "images": [string]
                                                                 }
                                                                 
                                                                 Önemli kurallar:
                                                                 - Sadece bu JSON şemasını kullan
                                                                 - Tüm alanları doldur
                                                                 - Fiyatlar decimal formatında olmalı
                                                                 - category değeri hiyerarşik olmalı (örn: 'Aksesuar > Çanta > Omuz Çantası')
                                                                 - Ekstra açıklama veya metin ekleme, sadece JSON döndür
                                                                 - attributes dizisi ürünün özelliklerini içermeli
                                                 """ },
                new { role = "user", content = promptUrl }
            },
            max_tokens = 1000,
            temperature = 0.1
        };

        var response = await _httpClient.PostAsJsonAsync(_settings.Endpoint, requestBody);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var output = result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return Ok(new { response = output });
        }
        Console.WriteLine(await response.Content.ReadAsStringAsync());
        return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    
    [HttpPost("ask")]
    public async Task<IActionResult> AskOpenAI([FromQuery] string promptUrl)
    {
        var requestBody = new
        {
            messages = new[]
            {
                new { role = "user", content = promptUrl }
            },
            max_tokens = 500,
            temperature = 0.1
        };

        var response = await _httpClient.PostAsJsonAsync(_settings.Endpoint, requestBody);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var output = result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return Ok(new { response = output });
        }

        return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [HttpPost("translate-product")]
    public async Task<string> TranslateProduct([FromBody] Product product)
    {
        var tranformProduct = new TransformedProduct
        {
            Name = product.Name,
            Description = product.Description,
            Sku = product.Sku,
            ParentSku = product.ParentSku,
            Attributes = product.Attributes,
            Category = product.Category,
            Brand = product.Brand,
            OriginalPrice = product.OriginalPrice,
            DiscountedPrice = product.DiscountedPrice,
            Images = product.Images,
            Score = 0
        };
        
        var requestBody = new
        {
            messages = new[]
            {
                new { role = "system", content = """
                    Sen bir e-ticaret ürün çevirmen ve değerlendiricisisin. Sana gelen ürün bilgilerini aşağıdaki kurallara göre İngilizce'ye çevir:
                    
                    1. name: Birebir çeviri yapma, ürün açıklaması ve özelliklerine bakarak SEO uyumlu İngilizce isim üret
                    2. description: HTML taglerini temizle, birebir çeviri yapma, SEO uyumlu yeni bir açıklama oluştur
                    3. sku ve parentSku: Aynen kalsın
                    4. attributes: Tüm key ve name değerlerini İngilizce'ye çevir
                    5. category: Kategori hiyerarşisini İngilizce'ye çevir
                    6. brand: Marka adını İngilizce'ye çevir
                    7. Fiyat ve görsel bilgileri aynen kalsın
                    8. Score:  bunu aşağıdaki kriterlere göre hesapla ve puan ver
                
                    Ayrıca 0-100 arası bir skor hesapla, bunu aşağıdaki maddelere göre yap:
                    - Ürün isminin açıklayıcılığı (25 puan)
                    - Açıklamanın detayı ve SEO uyumu (35 puan)
                    - Görsel sayısı (20 puan)
                    - Özellik ve marka zenginliği (20 puan)
                    
                    Yanıtı JSON formatında döndür.
                """ },
                new { role = "user", content = JsonSerializer.Serialize(tranformProduct) }
            },
            max_tokens = 1000,
            temperature = 0.1
        };

        var response = await _httpClient.PostAsJsonAsync(_settings.Endpoint, requestBody);

        if (!response.IsSuccessStatusCode)
            return "";
        
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var translatedContent = result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        
        return translatedContent;
    }

    [HttpGet("crawl-product")]
    public async Task<ActionResult<List<Product>>> CrawlProduct([FromQuery] string url)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            var response = await _httpClient.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(response);

            // Script içeriğini bulmak için regex pattern'i güncellendi
            var pattern = @"window\.__PRODUCT_DETAIL_APP_INITIAL_STATE__\s*=\s*({.+?});";
            var match = Regex.Match(response, pattern, RegexOptions.Singleline);

            if (!match.Success)
                return NotFound("Ürün bilgileri bulunamadı.");

            var jsonData = match.Groups[1].Value;
            //var dff = await AskOpenAI(jsonData);
            
            // JSON'ı parse et
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            try
            {
                var productData = JsonSerializer.Deserialize<JsonElement>(jsonData, options);
                var variants = new List<Product>();

                var product = productData.GetProperty("product");
                // Ana ürün ve varyantları için döngü
                if (product.TryGetProperty("variants", out JsonElement variantsElement))
                {
                    foreach (var variant in variantsElement.EnumerateArray())
                    {
                        try
                        {
                            var variantProduct = new Product
                            {
                                Name = product.GetProperty("name").GetString() ?? "",
                                Description = product.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                                Sku = variant.TryGetProperty("id", out var id) ? id.ToString() : "",
                                ParentSku = product.TryGetProperty("id", out var parentId) ? parentId.ToString() : "",
                                Brand = product.TryGetProperty("brand", out var brand) ? 
                                    brand.TryGetProperty("name", out var brandName) ? brandName.GetString() ?? "" : "" : "",
                                Category = product.TryGetProperty("categoryHierarchy", out var catHier) ?
                                    string.Join(" > ", catHier.EnumerateArray().Select(c => c.GetString() ?? "")) : "",
                                Attributes = new List<ProductAttribute>(),
                                OriginalPrice = variant.TryGetProperty("price", out var price) ? 
                                    price.TryGetProperty("originalPrice", out var origPrice) ? 
                                        origPrice.ValueKind == JsonValueKind.Object ? 
                                            origPrice.GetProperty("value").GetDecimal() : 
                                            origPrice.GetDecimal() 
                                        : 0 
                                    : 0,
                                DiscountedPrice = variant.TryGetProperty("price", out var discPrice) ? 
                                    discPrice.TryGetProperty("discountedPrice", out var discountedPrice) ? 
                                        discountedPrice.ValueKind == JsonValueKind.Object ? 
                                            discountedPrice.GetProperty("value").GetDecimal() : 
                                            discountedPrice.GetDecimal() 
                                        : 0 
                                    : 0,
                                Images = variant.TryGetProperty("images", out var images) ? 
                                    images.EnumerateArray().Select(img => img.GetString() ?? "").ToList() : new List<string>()
                            };

                            // Attributes'ları güvenli bir şekilde ekle
                            if (variant.TryGetProperty("attributes", out JsonElement attributes))
                            {
                                variantProduct.Attributes = attributes.EnumerateArray()
                                    .Select(attr => new ProductAttribute
                                    {
                                        Key = attr.GetProperty("key").GetString(),
                                        Name = attr.GetProperty("value").GetString()
                                    }).ToList();
                            }
                            var df=  await TranslateProduct(variantProduct);
                            variants.Add(variantProduct);
                            _crawledProducts.Add(variantProduct);
                        }
                        catch (Exception variantEx)
                        {
                            Console.WriteLine($"Variant parsing error: {variantEx.Message}");
                            continue; // Bir varyant başarısız olursa diğerlerine devam et
                        }
                    }
                }

                return Ok(variants);
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(
                    $"JSON parse hatası: {jsonEx.Message}\nJSON Data: {jsonData.Substring(0, Math.Min(100, jsonData.Length))}...");
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Ürün bilgileri çekilirken hata oluştu: {ex.Message}");
        }
    }

    [HttpGet("transform-product")]
    public async Task<ActionResult<Product>> TransformProduct([FromQuery] string sku)
    {
        var product = _crawledProducts.FirstOrDefault(p => p.Sku == sku || p.ParentSku == sku);
        if (product is null)
        {
            return NotFound();
        }
        
        var translatedProduct=  await TranslateProduct(product);
        var serializedProduct = JsonSerializer.Deserialize<TransformedProduct>(translatedProduct);
        _transformedProducts.Add(serializedProduct);
        return Ok(serializedProduct);
    }
}