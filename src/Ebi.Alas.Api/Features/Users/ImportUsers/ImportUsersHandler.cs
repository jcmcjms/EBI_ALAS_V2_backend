using System.Security.Cryptography;
using Ebi.Alas.Api.Features.Approvals;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.ImportUsers;

public sealed class ImportUsersHandler(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ApprovalAuthorityAssigner authorityAssigner)
{
    public const int MaxImportRows = 500;
    public const int MinPasswordLength = 12;

    public async Task<ImportUsersResult> ImportAsync(Stream xlsx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(xlsx);

        var errors = new List<ImportUsersValidationError>();
        var createdUsernames = new List<string>();
        var createdCredentials = new List<ImportUsersCredential>();
        var totalRows = 0;

        IReadOnlyList<UserExcel.Row> rows;
        try
        {
            rows = UserExcel.Parse(xlsx);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            errors.Add(new ImportUsersValidationError(0, "File", "Excel file is empty or corrupted."));
            return new ImportUsersResult(0, 0, 1, errors, createdUsernames, createdCredentials);
        }

        // Prefetch existing usernames once; per-row AnyAsync was an N+1 (up to MaxImportRows).
        var existingUsernames = await db.Users
            .Select(u => u.UserName)
            .ToListAsync(cancellationToken);
        var takenUsernames = new HashSet<string>(existingUsernames, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (totalRows >= MaxImportRows)
            {
                errors.Add(new ImportUsersValidationError(
                    row.RowNumber, "Row", $"Import is limited to {MaxImportRows} rows."));
                break;
            }

            totalRows++;
            ValidateRow(row, errors);

            if (errors.Any(e => e.RowNumber == row.RowNumber))
            {
                continue;
            }

            var username = row.Username!.Trim();
            if (!takenUsernames.Add(username))
            {
                errors.Add(new ImportUsersValidationError(row.RowNumber, "Username", "Username already exists."));
                continue;
            }

            var temporaryPassword = string.IsNullOrWhiteSpace(row.Password)
                ? GenerateTemporaryPassword()
                : row.Password!;

            var user = User.Create(
                username,
                passwordHasher.Hash(temporaryPassword),
                row.FirstName!.Trim(),
                row.LastName!.Trim(),
                row.MiddleName?.Trim() ?? string.Empty,
                row.Email ?? string.Empty,
                row.BranchCode!.Trim(),
                TryParseRole(row.Role, out var parsedRole) ? parsedRole : throw new InvalidOperationException("Role was validated."),
                timeProvider.GetUtcNow(),
                row.JobTitle?.Trim());

            if (!row.MustChangePassword)
            {
                user.ClearMustChangePassword(timeProvider.GetUtcNow());
            }

            db.Users.Add(user);
            await authorityAssigner.AssignAsync(user, cancellationToken);
            createdUsernames.Add(user.UserName);
            createdCredentials.Add(new ImportUsersCredential(user.UserName, temporaryPassword));
        }

        await db.SaveChangesAsync(cancellationToken);

        var failed = errors.Select(e => e.RowNumber).Where(n => n > 0).Distinct().Count();
        return new ImportUsersResult(
            totalRows,
            createdUsernames.Count,
            failed,
            errors,
            createdUsernames,
            createdCredentials);
    }

    private static void ValidateRow(UserExcel.Row row, List<ImportUsersValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(row.Username))
        {
            errors.Add(new ImportUsersValidationError(row.RowNumber, "Username", "Username is required."));
        }
        else if (row.Username.Trim().Length is < 3 or > 50)
        {
            errors.Add(new ImportUsersValidationError(row.RowNumber, "Username", "Username must be 3-50 characters."));
        }

        if (string.IsNullOrWhiteSpace(row.FirstName))
        {
            errors.Add(new ImportUsersValidationError(row.RowNumber, "First Name", "First name is required."));
        }

        if (string.IsNullOrWhiteSpace(row.LastName))
        {
            errors.Add(new ImportUsersValidationError(row.RowNumber, "Last Name", "Last name is required."));
        }

        if (string.IsNullOrWhiteSpace(row.BranchCode))
        {
            errors.Add(new ImportUsersValidationError(row.RowNumber, "Branch Code", "Branch code is required."));
        }

        if (string.IsNullOrWhiteSpace(row.Role))
        {
            errors.Add(new ImportUsersValidationError(row.RowNumber, "Role", "Role is required."));
        }
        else if (!TryParseRole(row.Role, out _))
        {
            errors.Add(new ImportUsersValidationError(
                row.RowNumber,
                "Role",
                $"Invalid role: {row.Role}. Use Encoder, Recommender, Evaluator, Approver, or Admin."));
        }
    }

    public static bool TryParseRole(string? raw, out UserRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim().Trim('﻿', ' ').Trim();
        var paren = value.IndexOf('(');
        if (paren > 0)
        {
            value = value[..paren].Trim();
        }

        if (value.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
        {
            value = nameof(UserRole.Admin);
        }

        // Bank titles / authority keys used in the official import workbook.
        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (s_jobTitleToRole.TryGetValue(compact, out var mapped))
        {
            value = mapped;
        }

        // Excel often stores enum names as numbers; require the catalog name.
        if (value.Length > 0 && value.All(char.IsDigit))
        {
            return false;
        }

        if (!Enum.TryParse<UserRole>(value, ignoreCase: true, out role))
        {
            return false;
        }

        // System is infrastructure-only; not an importable officer role.
        return role != UserRole.System && role != 0;
    }

    private static readonly Dictionary<string, string> s_jobTitleToRole = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AccountOfficer"] = nameof(UserRole.Encoder),
        ["CreditChecker"] = nameof(UserRole.Evaluator),
        ["BranchHead"] = nameof(UserRole.Recommender),
        ["BranchManager"] = nameof(UserRole.Recommender),
        ["SeniorBranchHead"] = nameof(UserRole.Recommender),
        ["OIC"] = nameof(UserRole.Recommender),
        ["OICLevel1"] = nameof(UserRole.Recommender),
        ["OfficerInCharge"] = nameof(UserRole.Recommender),
        ["AreaHead"] = nameof(UserRole.Approver),
        ["AreaHeadLevelH"] = nameof(UserRole.Approver),
        ["CEOPresident"] = nameof(UserRole.Approver),
        ["COO"] = nameof(UserRole.Approver),
        ["RBGHead"] = nameof(UserRole.Approver),
        ["ProductHead"] = nameof(UserRole.Approver),
        ["CreditHead"] = nameof(UserRole.Approver),
    };

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!?*.";
        var all = upper + lower + digits + special;

        Span<char> chars = stackalloc char[MinPasswordLength];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        for (var i = 4; i < chars.Length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
