using System.Security.Cryptography;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services
{
    /// <summary>
    /// Cycle U W1. Everything here goes through <see cref="IPotRepository"/>,
    /// whose reads are membership-scoped — never through
    /// <c>IUserRelatedRepository</c>, which answers a different question and
    /// throws on exactly the cross-member writes a pot is made of.
    /// </summary>
    public class PotService : IPotService
    {
        /// <summary>
        /// W0's decision, and the reason for it: EUR matches
        /// <c>UserDataSeeder</c>'s default base currency and every member's
        /// <c>UserPreferences.BaseCurrency</c>, so nobody sees one figure on the
        /// dashboard and another inside the pot.
        /// </summary>
        public const string DefaultCurrencyCode = "EUR";

        /// <summary>W0's decision: low enough that W4's alert actually fires against seeded spending.</summary>
        public const decimal DefaultLowBalanceThreshold = 50m;

        /// <summary>
        /// Long enough to survive a weekend and a forgotten message; short enough
        /// that a link pasted into a chat a year ago is not still a door. Any
        /// member can issue another one in two taps.
        /// </summary>
        public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

        /// <summary>The label a membership wears once the account behind it is gone.</summary>
        public const string AnonymizedDisplayName = "Former member";

        // No I/O/0/1: the code is read off a screen and typed by hand often
        // enough that ambiguous glyphs are a support cost. 22 characters from 32
        // is 110 bits, which is not guessable at any rate a rate limiter would
        // let through.
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 22;

        /// <summary>How much of a pot's history one read returns. Paging is W2's problem, if anyone asks.</summary>
        private const int ActivityPageSize = 200;

        private readonly IPotRepository _pots;
        private readonly ICurrentUserServices _user;
        private readonly ISharedCategoryService _sharedCategories;

        public PotService(
            IPotRepository pots,
            ICurrentUserServices user,
            ISharedCategoryService sharedCategories)
        {
            _pots = pots;
            _user = user;
            _sharedCategories = sharedCategories;
        }

        // ── Reading ────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<PotDTO>> GetPotsAsync(CancellationToken cancellationToken = default)
        {
            var pots = await _pots.MemberPots()
                                  .OrderBy(p => p.Name)
                                  .ThenBy(p => p.Id)
                                  .ToListAsync(cancellationToken);

            // W2: every pot's balance and the reader's position, in a fixed
            // number of grouped aggregates rather than one pass per pot.
            var money = await _pots.MoneyForMemberPotsAsync(cancellationToken);

            var result = new List<PotDTO>(pots.Count);
            foreach (var pot in pots)
                result.Add(await ToDtoAsync(pot, money, cancellationToken));

            return result;
        }

        public async Task<PotDTO?> GetPotAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
            if (pot is null) return null;

            return await ToDtoAsync(pot, await _pots.MoneyForMemberPotsAsync(cancellationToken), cancellationToken);
        }

        public async Task<IReadOnlyList<PotMemberDTO>?> GetMembersAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            var mine = await _pots.FindMyMembershipAsync(potId, cancellationToken);
            if (mine is null) return null;

            var members = await _pots.MembershipsIn(potId)
                                     .OrderBy(m => m.CreatedAt)
                                     .ThenBy(m => m.Id)
                                     .ToListAsync(cancellationToken);

            return members.Select(m => ToDto(m, mine.Id)).ToList();
        }

        public async Task<IReadOnlyList<PotInviteDTO>?> GetInvitesAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            if (await _pots.FindMyMembershipAsync(potId, cancellationToken) is null) return null;

            var invites = await _pots.InvitesIn(potId)
                                     .Include(i => i.IssuedByMembership)
                                     .Include(i => i.RevokedByMembership)
                                     .OrderByDescending(i => i.CreatedAt)
                                     .ThenBy(i => i.Id)
                                     .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            return invites.Select(i => ToDto(i, now)).ToList();
        }

        public async Task<IReadOnlyList<PotActivityDTO>?> GetActivityAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            if (await _pots.FindMyMembershipAsync(potId, cancellationToken) is null) return null;

            var entries = await _pots.ActivityIn(potId)
                                     .Include(a => a.ActorMembership)
                                     .OrderByDescending(a => a.CreatedAt)
                                     .ThenByDescending(a => a.Id)
                                     .Take(ActivityPageSize)
                                     .ToListAsync(cancellationToken);

            return entries.Select(ToDto).ToList();
        }

        // ── The pot itself ─────────────────────────────────────────────────────

        public async Task<PotDTO> CreatePotAsync(CreatePotDTO dto, CancellationToken cancellationToken = default)
        {
            var userId = _user.UserId;
            var name = dto.Name.Trim();

            var pot = new Pot
            {
                Name = name,
                CurrencyCode = (dto.CurrencyCode ?? DefaultCurrencyCode).Trim().ToUpperInvariant(),
                LowBalanceThreshold = dto.LowBalanceThreshold ?? DefaultLowBalanceThreshold,
                CreatedByUserId = userId,
            };

            // The creator is a member like any other — flat trust means the only
            // thing being first buys them is the earliest CreatedAt, which is
            // W2's rounding tie-break and nothing else.
            var membership = new PotMembership
            {
                Pot = pot,
                UserId = userId,
                DisplayName = CurrentDisplayName(),
            };

            _pots.Add(pot);
            _pots.Add(membership);
            _pots.Add(Entry(pot, membership, PotActivityKind.PotCreated, pot.Id, name, after: name));

            // W2: opening a pot is joining one, so it is where the reserved
            // "Shared" category appears for a user who has never been in a group
            // before. Added and not saved — the SaveAsync below flushes both, so
            // a failed create cannot leave a user in no pots holding the
            // category that says otherwise.
            await _sharedCategories.EnsureForCurrentUserAsync(cancellationToken);

            await _pots.SaveAsync(cancellationToken);

            return await ToDtoAsync(pot, await _pots.MoneyForMemberPotsAsync(cancellationToken), cancellationToken);
        }

        public async Task<PotDTO?> UpdatePotAsync(Guid potId, UpdatePotDTO dto, CancellationToken cancellationToken = default)
        {
            var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
            if (pot is null) return null;

            var actor = await _pots.FindMyMembershipAsync(potId, cancellationToken);
            if (actor is null) return null;

            var name = dto.Name.Trim();

            // Two independent changes on one screen, logged separately: "renamed
            // the pot" and "moved the low-balance line" are different events to
            // the people reading the log, and collapsing them would hide one.
            if (!string.Equals(pot.Name, name, StringComparison.Ordinal))
            {
                _pots.Add(Entry(pot, actor, PotActivityKind.PotRenamed, pot.Id, name, pot.Name, name));
                pot.Name = name;
            }

            if (pot.LowBalanceThreshold != dto.LowBalanceThreshold)
            {
                _pots.Add(Entry(
                    pot, actor, PotActivityKind.LowBalanceThresholdChanged, pot.Id, name,
                    FormatThreshold(pot.LowBalanceThreshold, pot.CurrencyCode),
                    FormatThreshold(dto.LowBalanceThreshold, pot.CurrencyCode)));

                pot.LowBalanceThreshold = dto.LowBalanceThreshold;
            }

            _pots.Update(pot);
            await _pots.SaveAsync(cancellationToken);

            return await ToDtoAsync(pot, await _pots.MoneyForMemberPotsAsync(cancellationToken), cancellationToken);
        }

        // ── Invites ────────────────────────────────────────────────────────────

        public async Task<PotInviteDTO?> CreateInviteAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
            if (pot is null) return null;

            var actor = await _pots.FindMyMembershipAsync(potId, cancellationToken);
            if (actor is null) return null;

            var invite = new PotInvite
            {
                Pot = pot,
                Code = NewCode(),
                IssuedByMembership = actor,
                ExpiresAt = DateTime.UtcNow.Add(InviteLifetime),
            };

            _pots.Add(invite);
            // The code itself never goes in the log: every member can read the
            // log, and a revoked invite's code would still be sitting in it.
            _pots.Add(Entry(pot, actor, PotActivityKind.InviteCreated, invite.Id, InviteLabel(invite)));

            await _pots.SaveAsync(cancellationToken);

            invite.IssuedByMembership = actor;
            return ToDto(invite, DateTime.UtcNow);
        }

        public async Task<(PotInviteDTO? Invite, string? Error)> RevokeInviteAsync(
            Guid potId, Guid inviteId, CancellationToken cancellationToken = default)
        {
            var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
            if (pot is null) return (null, null);

            var actor = await _pots.FindMyMembershipAsync(potId, cancellationToken);
            if (actor is null) return (null, null);

            // Guarded loader: exists, belongs to this pot, and this pot is one of
            // mine. An invite id from another pot is a miss, not a leak.
            var invite = await _pots.FindInPotAsync<PotInvite>(potId, inviteId, cancellationToken);
            if (invite is null) return (null, null);

            if (invite.IsRevoked)
            {
                // Revoking twice is not an error worth refusing — the caller
                // wanted it dead and it is dead.
                invite.IssuedByMembership ??= actor;
                return (ToDto(invite, DateTime.UtcNow), null);
            }

            invite.RevokedAt = DateTime.UtcNow;
            invite.RevokedByMembershipId = actor.Id;

            _pots.Update(invite);
            _pots.Add(Entry(pot, actor, PotActivityKind.InviteRevoked, invite.Id, InviteLabel(invite)));

            await _pots.SaveAsync(cancellationToken);

            var refreshed = await _pots.InvitesIn(potId)
                                       .Include(i => i.IssuedByMembership)
                                       .Include(i => i.RevokedByMembership)
                                       .FirstAsync(i => i.Id == inviteId, cancellationToken);

            return (ToDto(refreshed, DateTime.UtcNow), null);
        }

        public async Task<PotInvitePreviewDTO?> PreviewInviteAsync(string code, CancellationToken cancellationToken = default)
        {
            var invite = await _pots.FindInviteByCodeAsync(Normalize(code), cancellationToken);
            if (invite is null) return null;

            var now = DateTime.UtcNow;
            var existing = await _pots.FindMembershipForUserAsync(invite.PotId, _user.UserId, cancellationToken);

            // Everything this returns is disclosed to whoever holds the code, and
            // that list is deliberately three fields long: the pot's name, its
            // currency, and who invited you — enough to know what you are
            // accepting. Not the member list, not the history, not a count.
            return new PotInvitePreviewDTO
            {
                PotName = invite.Pot.Name,
                CurrencyCode = invite.Pot.CurrencyCode,
                InvitedByDisplayName = invite.IssuedByMembership?.DisplayName ?? "A member",
                AlreadyAMember = existing is { LeftAt: null },
                IsRedeemable = invite.IsRedeemable(now),
                Problem = RedemptionProblem(invite, now),
            };
        }

        // ── Membership lifecycle ───────────────────────────────────────────────

        public async Task<(PotDTO? Pot, string? Error)> JoinAsync(string code, CancellationToken cancellationToken = default)
        {
            var invite = await _pots.FindInviteByCodeAsync(Normalize(code), cancellationToken);
            if (invite is null)
                return (null, "That invite link is not valid.");

            var now = DateTime.UtcNow;
            if (RedemptionProblem(invite, now) is string problem)
                return (null, problem);

            var userId = _user.UserId;
            var existing = await _pots.FindMembershipForUserAsync(invite.PotId, userId, cancellationToken);

            // Idempotent by construction: a second redemption of a live
            // membership writes nothing at all, so it cannot produce a duplicate
            // and cannot spam the activity log either.
            if (existing is { LeftAt: null })
                return (await GetPotAsync(invite.PotId, cancellationToken), null);

            var pot = invite.Pot;

            if (existing is not null)
            {
                // Rejoining reuses the original row. Keeping one row per person
                // per pot is what the unique index enforces, and what keeps
                // W2's join-order tie-break from shifting under a member who
                // stepped out and came back.
                existing.LeftAt = null;
                existing.DisplayName = CurrentDisplayName();
                _pots.Update(existing);
                _pots.Add(Entry(pot, existing, PotActivityKind.MemberJoined, existing.Id, existing.DisplayName));
            }
            else
            {
                var membership = new PotMembership
                {
                    PotId = invite.PotId,
                    UserId = userId,
                    DisplayName = CurrentDisplayName(),
                };

                _pots.Add(membership);
                _pots.Add(Entry(pot, membership, PotActivityKind.MemberJoined, membership.Id, membership.DisplayName));
            }

            // W2: the reserved "Shared" category, on first join and only on a
            // join. Idempotent, so rejoining does not add a second, and inside
            // the same flush as the membership for the reason above.
            await _sharedCategories.EnsureForCurrentUserAsync(cancellationToken);

            await _pots.SaveAsync(cancellationToken);

            return (await GetPotAsync(invite.PotId, cancellationToken), null);
        }

        public async Task<(bool Found, string? Error)> LeaveAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
            if (pot is null) return (false, null);

            var membership = await _pots.FindMyMembershipAsync(potId, cancellationToken);
            if (membership is null) return (false, null);

            // W3 adds the refusal this deliberately does not have yet: leaving
            // with a non-zero position. There are no positions in W1, so there is
            // nothing here to owe.
            membership.LeftAt = DateTime.UtcNow;
            _pots.Update(membership);

            // Written while the membership is still the actor, which is the
            // whole reason leaving stamps a row instead of deleting one.
            _pots.Add(Entry(pot, membership, PotActivityKind.MemberLeft, membership.Id, membership.DisplayName));

            await _pots.SaveAsync(cancellationToken);
            return (true, null);
        }

        public async Task<int> AnonymizeMembershipsAsync(string userId, CancellationToken cancellationToken = default)
        {
            var memberships = await _pots.MembershipsOfUserAsync(userId, cancellationToken);
            var createdPots = await _pots.PotsCreatedByAsync(userId, cancellationToken);

            if (memberships.Count == 0 && createdPots.Count == 0) return 0;

            var now = DateTime.UtcNow;

            foreach (var membership in memberships)
            {
                // Log first: the entry names who this was while the label is
                // still theirs, and the actor reference survives the change
                // because the row does.
                _pots.Add(new PotActivity
                {
                    PotId = membership.PotId,
                    ActorMembershipId = membership.Id,
                    Kind = PotActivityKind.MemberAnonymized,
                    TargetId = membership.Id,
                    TargetLabel = membership.DisplayName,
                    Before = membership.DisplayName,
                    After = AnonymizedDisplayName,
                });

                membership.UserId = null;
                membership.AnonymizedAt = now;
                membership.LeftAt ??= now;
                membership.DisplayName = AnonymizedDisplayName;
                _pots.Update(membership);
            }

            // The creator reference is a second Restrict FK to the same user, so
            // it has to be cleared too or the delete still fails. The pot keeps
            // naming its creator through the membership label above.
            foreach (var pot in createdPots)
            {
                pot.CreatedByUserId = null;
                _pots.Update(pot);
            }

            await _pots.SaveAsync(cancellationToken);
            return memberships.Count;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private string CurrentDisplayName()
        {
            var name = _user.UserName;
            return string.IsNullOrWhiteSpace(name) ? "Member" : name.Trim();
        }

        private static string NewCode() =>
            new(RandomNumberGenerator.GetItems<char>(CodeAlphabet, CodeLength));

        private static string Normalize(string code) =>
            (code ?? string.Empty).Trim().ToUpperInvariant();

        private static string InviteLabel(PotInvite invite) =>
            $"Invite expiring {invite.ExpiresAt:yyyy-MM-dd}";

        private static string FormatThreshold(decimal amount, string currencyCode) =>
            amount == 0m ? "off" : $"{amount:0.##} {currencyCode}";

        /// <summary>
        /// The sentence to show when a code cannot be redeemed, or null when it
        /// can. Revoked and expired are told apart on purpose: "ask them for a
        /// new link" and "that link was cancelled" are different next actions.
        /// </summary>
        private static string? RedemptionProblem(PotInvite invite, DateTime asOf)
        {
            if (invite.IsRevoked) return "That invite has been revoked.";
            if (invite.HasExpired(asOf)) return "That invite has expired. Ask a member for a new link.";
            return null;
        }

        private async Task<PotDTO> ToDtoAsync(
            Pot pot, IReadOnlyDictionary<Guid, PotMoney> money, CancellationToken cancellationToken)
        {
            var members = await _pots.MembershipsIn(pot.Id).ToListAsync(cancellationToken);
            var userId = _user.UserId;

            // Absent means no ledger rows yet, which is a balance of zero rather
            // than an unknown.
            var potMoney = money.TryGetValue(pot.Id, out var m) ? m : default;

            var mine = members.FirstOrDefault(m => m.UserId == userId && m.LeftAt is null);
            var creator = pot.CreatedByUserId is null
                ? null
                : members.FirstOrDefault(m => m.UserId == pot.CreatedByUserId);

            return new PotDTO
            {
                Id = pot.Id,
                Name = pot.Name,
                CurrencyCode = pot.CurrencyCode,
                LowBalanceThreshold = pot.LowBalanceThreshold,
                CreatedAt = pot.CreatedAt,
                CreatedByDisplayName = creator?.DisplayName,
                MemberCount = members.Count(m => m.LeftAt is null),
                MembershipId = mine?.Id ?? Guid.Empty,
                Balance = potMoney.Balance,
                YourPosition = potMoney.YourPosition,
                IsLow = pot.LowBalanceThreshold > 0m && potMoney.Balance < pot.LowBalanceThreshold,
            };
        }

        private static PotMemberDTO ToDto(PotMembership membership, Guid myMembershipId) => new()
        {
            Id = membership.Id,
            DisplayName = membership.DisplayName,
            JoinedAt = membership.CreatedAt,
            LeftAt = membership.LeftAt,
            IsActive = membership.IsActive,
            IsAnonymized = membership.IsAnonymized,
            IsYou = membership.Id == myMembershipId,
        };

        private static PotInviteDTO ToDto(PotInvite invite, DateTime asOf) => new()
        {
            Id = invite.Id,
            Code = invite.Code,
            CreatedAt = invite.CreatedAt,
            ExpiresAt = invite.ExpiresAt,
            RevokedAt = invite.RevokedAt,
            IssuedByDisplayName = invite.IssuedByMembership?.DisplayName ?? "A member",
            RevokedByDisplayName = invite.RevokedByMembership?.DisplayName,
            IsRedeemable = invite.IsRedeemable(asOf),
            HasExpired = invite.HasExpired(asOf),
        };

        private static PotActivityDTO ToDto(PotActivity activity) => new()
        {
            Id = activity.Id,
            Kind = activity.Kind,
            ActorDisplayName = activity.ActorMembership?.DisplayName,
            TargetId = activity.TargetId,
            TargetLabel = activity.TargetLabel,
            Before = activity.Before,
            After = activity.After,
            OccurredAt = activity.CreatedAt,
        };

        private static PotActivity Entry(
            Pot pot,
            PotMembership actor,
            PotActivityKind kind,
            Guid? targetId = null,
            string? targetLabel = null,
            string? before = null,
            string? after = null) => new()
            {
                Pot = pot,
                ActorMembership = actor,
                Kind = kind,
                TargetId = targetId,
                TargetLabel = targetLabel,
                Before = before,
                After = after,
            };
    }
}
