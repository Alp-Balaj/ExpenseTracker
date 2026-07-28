namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// Optional window over a list endpoint, bound from the query string.
    /// <para>
    /// Paging is deliberately opt-in: with neither value supplied the endpoint
    /// returns the whole list, exactly as it did before this existed. A default
    /// page size would silently truncate every client that aggregates the full
    /// history itself (dashboard totals, charts, CSV export) without any of them
    /// asking for it — so the client has to say when it wants a page.
    /// </para>
    /// </summary>
    public sealed class PageQuery
    {
        /// <summary>Largest window a single request may ask for.</summary>
        public const int MaxTake = 500;

        /// <summary>
        /// Response header carrying the total row count behind a list response,
        /// before <see cref="Skip"/>/<see cref="Take"/>. Clients page by reading
        /// this rather than by discovering a short page, which keeps the
        /// response body a plain array whether or not a window was asked for.
        /// </summary>
        public const string TotalCountHeader = "X-Total-Count";

        /// <summary>Rows to skip. Null or 0 starts at the first row.</summary>
        public int? Skip { get; init; }

        /// <summary>Rows to return. Null means "no window" — every remaining row.</summary>
        public int? Take { get; init; }

        /// <summary>Null when the values are usable, otherwise the 400 message.</summary>
        public string? Validate()
        {
            if (Skip is < 0)
                return "skip must be 0 or greater.";

            if (Take is < 1)
                return "take must be 1 or greater.";

            if (Take is > MaxTake)
                return $"take must be {MaxTake} or less.";

            return null;
        }
    }
}
