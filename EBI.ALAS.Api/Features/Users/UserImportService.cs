using OfficeOpenXml;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.AuditLogs;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Users;

public interface IUserImportService
{
    Task<byte[]> ExportUsersAsync(ExportUsersParameters parameters, CancellationToken ct);
    Task<byte[]> GenerateTemplateAsync(CancellationToken ct);
    Task<UserImportResult> ImportUsersAsync(Stream file, int operatorId, string operatorName, CancellationToken ct);
}

public class UserImportService : IUserImportService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITempPasswordGenerator _tempPasswordGenerator;
    private readonly ITimeProvider _timeProvider;
    private readonly AppDbContext _context;
    private readonly IAuditLogService _auditLogService;

    public UserImportService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITempPasswordGenerator tempPasswordGenerator,
        ITimeProvider timeProvider,
        AppDbContext context,
        IAuditLogService auditLogService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tempPasswordGenerator = tempPasswordGenerator;
        _timeProvider = timeProvider;
        _context = context;
        _auditLogService = auditLogService;
    }

    public async Task<byte[]> ExportUsersAsync(ExportUsersParameters parameters, CancellationToken ct)
    {
        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(parameters.Search))
        {
            var search = parameters.Search.ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(search) ||
                u.FirstName.ToLower().Contains(search) ||
                u.LastName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Role))
            query = query.Where(u => u.Role == parameters.Role);

        if (parameters.IsActive.HasValue)
            query = query.Where(u => u.IsActive == parameters.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(parameters.BranchCode))
            query = query.Where(u => u.BranchId == parameters.BranchCode);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Users");

        // Headers
        var headers = new[] {
            "Username", "First Name", "Middle Name", "Last Name",
            "Branch Code", "Role", "Job Title", "Covered Branches",
            "Status", "Created At"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
            worksheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        // Data rows
        for (int row = 0; row < users.Count; row++)
        {
            var user = users[row];
            var excelRow = row + 2;

            worksheet.Cells[excelRow, 1].Value = user.Username;
            worksheet.Cells[excelRow, 2].Value = user.FirstName;
            worksheet.Cells[excelRow, 3].Value = user.MiddleName ?? "";
            worksheet.Cells[excelRow, 4].Value = user.LastName;
            worksheet.Cells[excelRow, 5].Value = user.BranchId;
            worksheet.Cells[excelRow, 6].Value = user.Role;
            worksheet.Cells[excelRow, 7].Value = user.JobTitle ?? "";

            // Covered branches for approvers
            var coveredBranches = await _context.UserBranchCoverages
                .Where(ubc => ubc.UserId == user.Id)
                .Select(ubc => ubc.BranchCode)
                .ToListAsync(ct);
            worksheet.Cells[excelRow, 8].Value = string.Join(", ", coveredBranches);

            worksheet.Cells[excelRow, 9].Value = user.IsActive ? "Active" : "Suspended";
            worksheet.Cells[excelRow, 10].Value = user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        return await package.GetAsByteArrayAsync(ct);
    }

    public async Task<byte[]> GenerateTemplateAsync(CancellationToken ct)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Template");

        // Headers with instructions
        var headers = new[] {
            "Username *", "First Name *", "Middle Name", "Last Name *",
            "Branch Code *", "Role *", "Job Title", "Covered Branches",
            "Email", "Phone"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
            worksheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        // Example row
        worksheet.Cells[2, 1].Value = "jdoe";
        worksheet.Cells[2, 2].Value = "Juan";
        worksheet.Cells[2, 3].Value = "Dela";
        worksheet.Cells[2, 4].Value = "Cruz";
        worksheet.Cells[2, 5].Value = "011";
        worksheet.Cells[2, 6].Value = "Encoder";
        worksheet.Cells[2, 7].Value = "";
        worksheet.Cells[2, 8].Value = "";
        worksheet.Cells[2, 9].Value = "jdoe@enterprisebank.ph";
        worksheet.Cells[2, 10].Value = "+639123456789";

        // Instructions sheet
        var instructions = package.Workbook.Worksheets.Add("Instructions");
        instructions.Cells[1, 1].Value = "User Import Instructions";
        instructions.Cells[1, 1].Style.Font.Size = 14;
        instructions.Cells[1, 1].Style.Font.Bold = true;

        var instructionText = new[]
        {
            "Required fields are marked with *",
            "Username must be unique (no spaces, 3-50 characters)",
            "Branch Code must match a valid branch code (e.g., 011, 023)",
            "Role must be one of: Encoder, Recommender, Evaluator, Approver, Admin",
            "Job Title is required for Approvers (must match an approval authority key)",
            "Covered Branches: comma-separated branch codes for Branch-scope approvers",
            "Passwords will be auto-generated; users must change on first login",
            "Email and Phone are optional but recommended for contact tracing",
            "",
            "Security: All imported users will be created with MustChangePassword = true",
            "",
            "Row numbers in validation errors are the Excel row numbers (header = row 1).",
            "Completely empty rows are ignored — clear a row's contents to exclude it."
        };

        for (int i = 0; i < instructionText.Length; i++)
        {
            instructions.Cells[i + 2, 1].Value = instructionText[i];
        }

        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        instructions.Cells[instructions.Dimension.Address].AutoFitColumns();

        return await package.GetAsByteArrayAsync(ct);
    }

    public async Task<UserImportResult> ImportUsersAsync(Stream file, int operatorId, string operatorName, CancellationToken ct)
    {
        var errors = new List<UserImportValidationError>();
        var createdUsernames = new List<string>();
        var totalRows = 0;

        using var package = new ExcelPackage(file);
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        if (worksheet == null || worksheet.Dimension == null)
        {
            errors.Add(new UserImportValidationError(0, "File", "Excel file is empty or corrupted"));
            return new UserImportResult(0, 0, 0, errors, createdUsernames);
        }

        // Skip header row (row 1)
        totalRows = 0;

        // Load lookup data once
        var validBranchCodes = (await _context.Branches.Select(b => b.Code).ToListAsync(ct)).ToHashSet();
        var validRoles = new[] { "Encoder", "Recommender", "Evaluator", "Approver", "Admin" };
        var approvalAuthorities = await _context.ApprovalAuthorities.ToListAsync(ct);

        for (int excelRow = 2; excelRow <= worksheet.Dimension.Rows; excelRow++)
        {
            // Skip rows that EPPlus considers "used" but carry no data
            // (formatted or cleared but never deleted — same false-positive
            // source as the loan product import).
            if (IsBlankRow(worksheet, excelRow)) continue;

            totalRows++;
            var rowNumber = excelRow;   // report the real Excel row, not a data index
            var username = GetCellString(worksheet, excelRow, 1);
            var firstName = GetCellString(worksheet, excelRow, 2);
            var middleName = GetCellString(worksheet, excelRow, 3);
            var lastName = GetCellString(worksheet, excelRow, 4);
            var branchCode = GetCellString(worksheet, excelRow, 5);
            var role = GetCellString(worksheet, excelRow, 6);
            var jobTitle = GetCellString(worksheet, excelRow, 7);
            var coveredBranchesStr = GetCellString(worksheet, excelRow, 8);
            var email = GetCellString(worksheet, excelRow, 9);
            var phone = GetCellString(worksheet, excelRow, 10);

            // Validation
            if (string.IsNullOrWhiteSpace(username))
                errors.Add(new UserImportValidationError(rowNumber, "Username", "Username is required"));
            else if (username.Length < 3 || username.Length > 50)
                errors.Add(new UserImportValidationError(rowNumber, "Username", "Username must be 3-50 characters"));
            else if (await _userRepository.UsernameExistsAsync(username))
                errors.Add(new UserImportValidationError(rowNumber, "Username", "Username already exists"));

            if (string.IsNullOrWhiteSpace(firstName))
                errors.Add(new UserImportValidationError(rowNumber, "First Name", "First name is required"));
            if (string.IsNullOrWhiteSpace(lastName))
                errors.Add(new UserImportValidationError(rowNumber, "Last Name", "Last name is required"));

            if (string.IsNullOrWhiteSpace(branchCode))
                errors.Add(new UserImportValidationError(rowNumber, "Branch Code", "Branch code is required"));
            else if (!validBranchCodes.Contains(branchCode))
                errors.Add(new UserImportValidationError(rowNumber, "Branch Code", $"Invalid branch code: {branchCode}"));

            if (string.IsNullOrWhiteSpace(role))
                errors.Add(new UserImportValidationError(rowNumber, "Role", "Role is required"));
            else if (!validRoles.Contains(role))
                errors.Add(new UserImportValidationError(rowNumber, "Role", $"Invalid role: {role}"));

            // Approver-specific validation
            ApprovalAuthority? authority = null;
            if (role == "Approver")
            {
                if (string.IsNullOrWhiteSpace(jobTitle))
                    errors.Add(new UserImportValidationError(rowNumber, "Job Title", "Job title is required for approvers"));
                else
                {
                    authority = approvalAuthorities.FirstOrDefault(a => a.Key == jobTitle);
                    if (authority == null)
                        errors.Add(new UserImportValidationError(rowNumber, "Job Title", $"Invalid approval authority: {jobTitle}"));
                }
            }

            // Skip this row if there are validation errors
            if (errors.Any(e => e.RowNumber == rowNumber))
                continue;

            // Create user
            var tempPassword = _tempPasswordGenerator.Generate();
            var user = new User
            {
                Username = username!,
                PasswordHash = _passwordHasher.HashPassword(tempPassword),
                FirstName = firstName!,
                MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName,
                LastName = lastName!,
                BranchId = branchCode!,
                Role = role!,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = _timeProvider.UtcNow,
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
            };

            if (authority != null)
            {
                user.ApprovalAuthorityKey = authority.Key;
                user.JobTitle = authority.DisplayName;
            }
            else
            {
                user.JobTitle = string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle;
            }

            await _userRepository.AddUserAsync(user);

            // Multi-branch coverage for approvers
            if (authority?.ScopeType == AuthorityScope.Branch && !string.IsNullOrWhiteSpace(coveredBranchesStr))
            {
                var coveredBranchCodes = coveredBranchesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var code in coveredBranchCodes.Distinct())
                {
                    if (validBranchCodes.Contains(code))
                    {
                        _context.UserBranchCoverages.Add(new UserBranchCoverage
                        {
                            UserId = user.Id,
                            BranchCode = code
                        });
                    }
                }
                await _context.SaveChangesAsync(ct);
            }

            createdUsernames.Add(username!);

            // Audit log
            await _auditLogService.LogAsync(
                operatorId,
                operatorName,
                "Import",
                "User",
                user.Id.ToString(),
                user.Username,
                $"User imported via batch upload"
            );
        }

        return new UserImportResult(
            totalRows,
            createdUsernames.Count,
            errors.Count,
            errors,
            createdUsernames
        );
    }

    /** True when every cell in the row's used range is null/whitespace. */
    private static bool IsBlankRow(ExcelWorksheet worksheet, int row)
    {
        var endCol = worksheet.Dimension?.End.Column ?? 1;
        for (var col = 1; col <= endCol; col++)
        {
            var value = worksheet.Cells[row, col].Value;
            if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                return false;
        }
        return true;
    }

    private static string? GetCellString(ExcelWorksheet worksheet, int row, int col)
    {
        var value = worksheet.Cells[row, col].Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
