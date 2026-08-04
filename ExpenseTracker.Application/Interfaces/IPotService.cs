using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// Cycle U W1: the group, before there is any money in it.
    /// <para>
    /// Every method that takes a <c>potId</c> returns null when the caller is not
    /// a current member of it — and returns exactly the same null when no such
    /// pot exists. The controller turns that into <b>404, never 403</b>: a 403
    /// would confirm that someone else's pot is there, and the existence of a
    /// group you were not invited to is itself private.
    /// </para>
    /// <para>
    /// Where a call can fail for a reason worth explaining ("that invite was
    /// revoked"), it returns a message alongside; the controller answers 400.
    /// Null id and null message together always mean 404.
    /// </para>
    /// </summary>
    public interface IPotService
    {
        Task<IReadOnlyList<PotDTO>> GetPotsAsync(CancellationToken cancellationToken = default);

        Task<PotDTO?> GetPotAsync(Guid potId, CancellationToken cancellationToken = default);

        Task<PotDTO> CreatePotAsync(CreatePotDTO dto, CancellationToken cancellationToken = default);

        Task<PotDTO?> UpdatePotAsync(Guid potId, UpdatePotDTO dto, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PotMemberDTO>?> GetMembersAsync(Guid potId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PotInviteDTO>?> GetInvitesAsync(Guid potId, CancellationToken cancellationToken = default);

        Task<PotInviteDTO?> CreateInviteAsync(Guid potId, CancellationToken cancellationToken = default);

        /// <summary>Any member may revoke any invite, including one they did not issue.</summary>
        Task<(PotInviteDTO? Invite, string? Error)> RevokeInviteAsync(
            Guid potId, Guid inviteId, CancellationToken cancellationToken = default);

        /// <summary>
        /// What to show someone holding a link. Null only when the code matches
        /// nothing at all; a revoked or expired code still resolves, so the join
        /// screen can say which of the two it is instead of "not found".
        /// </summary>
        Task<PotInvitePreviewDTO?> PreviewInviteAsync(string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems a code. Idempotent: redeeming twice leaves exactly one
        /// membership and succeeds both times.
        /// </summary>
        Task<(PotDTO? Pot, string? Error)> JoinAsync(string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Leaves a pot. The membership row stays — stamped, not deleted — so the
        /// activity log keeps its attributions and W2's join-order tie-break is
        /// unaffected.
        /// </summary>
        Task<(bool Found, string? Error)> LeaveAsync(Guid potId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PotActivityDTO>?> GetActivityAsync(Guid potId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Strips one user out of every pot they are in, leaving the rows and the
        /// group's history behind under a neutral label. This is the step that
        /// has to run <em>before</em> Identity deletes the user: the membership
        /// FK is <c>Restrict</c>, so a delete that skipped it fails at the
        /// database instead of cascading a group's history away.
        /// </summary>
        /// <returns>How many memberships were anonymized.</returns>
        Task<int> AnonymizeMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    }
}
