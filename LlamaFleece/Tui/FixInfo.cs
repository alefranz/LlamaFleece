public class FixInfo
{
    public string Key { get; }
    public string Name { get; }
    public string Shorthand { get; }
    public bool Enabled { get; set; }

    public FixInfo(string key, string name, string shorthand, bool enabled)
    {
        Key = key;
        Name = name;
        Shorthand = shorthand;
        Enabled = enabled;
    }

    public FixInfo Clone() => new(Key, Name, Shorthand, Enabled);
}
