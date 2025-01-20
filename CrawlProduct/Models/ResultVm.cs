namespace CrawlProduct.Models;

public class ResultVm
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}
public class ResultVm<T>
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public T Data { get; set; }
}