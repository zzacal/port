namespace Gallery.Web.Data;

public sealed class MongoOptions
{
    public string? ConnectionString { get; set; }
    public string? Database { get; set; }

    /// <summary>Returns the names of required settings that are null or empty. Never returns values.</summary>
    public IReadOnlyList<string> Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ConnectionString)) missing.Add("Mongo:ConnectionString");
        if (string.IsNullOrWhiteSpace(Database))         missing.Add("Mongo:Database");
        return missing;
    }
}
