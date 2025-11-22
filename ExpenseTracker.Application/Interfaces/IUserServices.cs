namespace ExpenseTracker.Application.Interfaces
{
    public interface IUserServices
    {
        /// <summary>
        /// Gets the currently authenticated user's ID.
        /// </summary>
        string UserId { get; }

        /// <summary>
        /// Optionally, gets the current user's username.
        /// </summary>
        string UserName { get; }

        /// <summary>
        /// Whether the current user is authenticated.
        /// </summary>
        bool IsAuthenticated { get; }
    }
}