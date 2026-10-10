namespace Assistant.Core.Data
{
    /// <summary>
    /// Filtering strategy applied to token rarity collections.
    /// </summary>
    public enum FilterMode
    {
        /// <summary>
        /// Only tokens matching the specified rarities are accepted.
        /// An empty rarity set matches no tokens.
        /// </summary>
        Inclusive,

        /// <summary>
        /// Tokens matching the specified rarities are excluded.
        /// An empty rarity set matches all rarities.
        /// </summary>
        Exclusive
    }
}
