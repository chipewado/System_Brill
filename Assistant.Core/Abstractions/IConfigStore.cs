namespace Assistant.Core.Abstractions
{
    /// <summary>
    /// Abstraction for persisting and retrieving configuration data without coupling Core to file systems or formats.
    /// </summary>
    public interface IConfigStore
    {
        void Save<T>(string key, T data);
        T? Load<T>(string key);
        bool Exists(string key);
        bool Delete(string key);
    }
}
