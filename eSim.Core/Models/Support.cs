namespace eSim.Core.Models;

public class ContactRequest
{
    public string? name { get; set; }
    public string? email { get; set; }
    public string? phone { get; set; }
    public string? topic { get; set; }
    public string? topickey { get; set; }
    public string? message { get; set; }

    // Honeypot: MUST be empty
    public string? company { get; set; }
}
