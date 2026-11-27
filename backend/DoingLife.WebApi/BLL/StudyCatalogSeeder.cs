using DoingLife.WebApi.DAL;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Helper;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace DoingLife.WebApi.BLL
{
    /// <summary>
    /// Loads the study catalog from CSV files into the database.
    /// Safe to run again: a row that already exists is updated, a new row is inserted.
    /// Everything happens in one transaction: if any row is wrong, nothing is saved.
    /// </summary>
    public class StudyCatalogSeeder
    {
        private static readonly Regex DliPattern = new(@"^O\d+$", RegexOptions.Compiled);

        private readonly AppDbContext _context;
        private readonly ILogger<StudyCatalogSeeder> _logger;

        public StudyCatalogSeeder(AppDbContext context, ILogger<StudyCatalogSeeder> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<SeedReport> SeedAsync(string folder, CancellationToken ct = default)
        {
            var report = new SeedReport();

            if (!Directory.Exists(folder))
            {
                report.Errors.Add($"The folder '{folder}' does not exist.");
                return report;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            try
            {
                // Order matters: each table points at the ones before it.
                var institutions = await SeedInstitutionsAsync(folder, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                var campuses = await SeedCampusesAsync(folder, institutions, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                var departments = await SeedDepartmentsAsync(folder, institutions, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                var programs = await SeedProgramsAsync(folder, institutions, departments, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                await SeedIntakesAsync(folder, institutions, programs, campuses, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                await SeedTuitionAsync(folder, institutions, programs, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                await SeedEmploymentAsync(folder, institutions, report, ct);
                if (report.HasErrors) return await RollBackAsync(transaction, report, ct);

                await transaction.CommitAsync(ct);
                _logger.LogInformation("Study catalog seed finished.");
                return report;
            }
            catch (DbUpdateException ex)
            {
                // For example: a value longer than its column, or a duplicate key.
                report.Errors.Add("The database refused the data: " + (ex.InnerException?.Message ?? ex.Message));
                return await RollBackAsync(transaction, report, ct);
            }
        }

        private static async Task<SeedReport> RollBackAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
            SeedReport report, CancellationToken ct)
        {
            await transaction.RollbackAsync(ct);
            report.RolledBack = true;
            return report;
        }

        // ---- institutions -------------------------------------------------

        private async Task<Dictionary<string, Institution>> SeedInstitutionsAsync(
            string folder, SeedReport report, CancellationToken ct)
        {
            const string table = "institutions";
            var existing = await _context.Institutions.ToDictionaryAsync(x => x.DliNumber, ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "institutions.csv")))
            {
                Guard(report, row, () =>
                {
                    var dli = row.Required("dli_number");
                    if (!DliPattern.IsMatch(dli))
                    {
                        throw row.Error($"'{dli}' is not a DLI number (expected the letter O and digits)");
                    }

                    if (!existing.TryGetValue(dli, out var entity))
                    {
                        entity = new Institution { DliNumber = dli };
                        _context.Institutions.Add(entity);
                        existing[dli] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    entity.Name = row.Required("name");
                    entity.Province = row.Required("province");
                    entity.InstitutionType = row.Required("institution_type");
                    entity.Website = row.Optional("website");
                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);
            return existing;
        }

        // ---- campuses -----------------------------------------------------

        private async Task<Dictionary<(int InstitutionId, string Name), Campus>> SeedCampusesAsync(
            string folder, Dictionary<string, Institution> institutions, SeedReport report, CancellationToken ct)
        {
            const string table = "campuses";
            var existing = await _context.Campuses.ToDictionaryAsync(x => (x.InstitutionId, x.SourceName), ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "campuses.csv")))
            {
                Guard(report, row, () =>
                {
                    var institution = FindInstitution(institutions, row);
                    var sourceName = row.Required("source_campus_name");
                    var key = (institution.Id, sourceName);

                    if (!existing.TryGetValue(key, out var entity))
                    {
                        entity = new Campus { Institution = institution, SourceName = sourceName };
                        _context.Campuses.Add(entity);
                        existing[key] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    entity.Name = row.Required("campus_name");
                    entity.City = row.Required("city");
                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);

            // Other files refer to a campus by the name we display, so index it that way too.
            var byDisplayName = new Dictionary<(int InstitutionId, string Name), Campus>();
            foreach (var campus in existing.Values)
            {
                byDisplayName.TryAdd((campus.InstitutionId, campus.Name), campus);
            }
            return byDisplayName;
        }

        // ---- departments --------------------------------------------------

        private async Task<Dictionary<(int InstitutionId, string Name), Department>> SeedDepartmentsAsync(
            string folder, Dictionary<string, Institution> institutions, SeedReport report, CancellationToken ct)
        {
            const string table = "departments";
            var existing = await _context.Departments.ToDictionaryAsync(x => (x.InstitutionId, x.Name), ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "departments.csv")))
            {
                Guard(report, row, () =>
                {
                    var institution = FindInstitution(institutions, row);
                    var name = row.Required("department_name");
                    var key = (institution.Id, name);

                    if (!existing.TryGetValue(key, out var entity))
                    {
                        entity = new Department { Institution = institution, Name = name };
                        _context.Departments.Add(entity);
                        existing[key] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);
            return existing;
        }

        // ---- programs -----------------------------------------------------

        private async Task<Dictionary<(int InstitutionId, string Name), StudyProgram>> SeedProgramsAsync(
            string folder, Dictionary<string, Institution> institutions,
            Dictionary<(int InstitutionId, string Name), Department> departments,
            SeedReport report, CancellationToken ct)
        {
            const string table = "programs";
            var existing = await _context.StudyPrograms.ToDictionaryAsync(x => (x.InstitutionId, x.Name), ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "programs.csv")))
            {
                Guard(report, row, () =>
                {
                    var institution = FindInstitution(institutions, row);
                    var name = row.Required("program_name");
                    var key = (institution.Id, name);

                    if (!existing.TryGetValue(key, out var entity))
                    {
                        entity = new StudyProgram { Institution = institution, Name = name };
                        _context.StudyPrograms.Add(entity);
                        existing[key] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    var departmentName = row.Optional("department_name");
                    if (departmentName == null)
                    {
                        entity.Department = null;
                    }
                    else if (departments.TryGetValue((institution.Id, departmentName), out var department))
                    {
                        entity.Department = department;
                    }
                    else
                    {
                        throw row.Error($"unknown department '{departmentName}' for {institution.Name}");
                    }

                    entity.Credential = row.Required("credential");
                    entity.FieldOfStudy = row.Optional("field_of_study");
                    entity.DurationText = row.Optional("duration");
                    entity.OpenToInternational = row.Bool("open_to_international");
                    entity.EnglishTest = row.Optional("english_test");
                    entity.EnglishMinimum = row.OptionalInt("english_minimum");
                    entity.AcademicMinimum = row.Optional("academic_minimum");
                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);
            return existing;
        }

        // ---- intakes ------------------------------------------------------

        private async Task SeedIntakesAsync(
            string folder, Dictionary<string, Institution> institutions,
            Dictionary<(int InstitutionId, string Name), StudyProgram> programs,
            Dictionary<(int InstitutionId, string Name), Campus> campuses,
            SeedReport report, CancellationToken ct)
        {
            const string table = "program_intakes";
            var existing = await _context.ProgramIntakes
                .ToDictionaryAsync(x => (x.ProgramId, x.StartDate, x.CampusId), ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "program_intakes.csv")))
            {
                Guard(report, row, () =>
                {
                    var institution = FindInstitution(institutions, row);
                    var program = FindProgram(programs, institution, row);

                    Campus? campus = null;
                    var campusName = row.Optional("campus_name");
                    if (campusName != null && !campuses.TryGetValue((institution.Id, campusName), out campus))
                    {
                        throw row.Error($"unknown campus '{campusName}' for {institution.Name}");
                    }

                    var startDate = row.RequiredDate("start_date");
                    var key = (program.Id, startDate, campus?.Id);

                    if (!existing.TryGetValue(key, out var entity))
                    {
                        entity = new ProgramIntake { Program = program, StartDate = startDate };
                        _context.ProgramIntakes.Add(entity);
                        existing[key] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    entity.Campus = campus;
                    entity.Season = row.Optional("season");
                    entity.DeliveryMode = row.Optional("delivery_mode");
                    entity.InternationalStatus = row.Optional("international_status");
                    entity.InternationalApplicationOpens = row.OptionalDate("international_application_opens");
                    entity.InternationalDeadline = row.OptionalDate("international_deadline");
                    entity.DomesticStatus = row.Optional("domestic_status");
                    entity.DomesticApplicationOpens = row.OptionalDate("domestic_application_opens");
                    entity.DomesticDeadline = row.OptionalDate("domestic_deadline");
                    entity.PgwpEligible = row.OptionalBool("pgwp_eligible");
                    entity.PgwpNotes = row.Optional("pgwp_notes");
                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);
        }

        // ---- tuition ------------------------------------------------------

        private async Task SeedTuitionAsync(
            string folder, Dictionary<string, Institution> institutions,
            Dictionary<(int InstitutionId, string Name), StudyProgram> programs,
            SeedReport report, CancellationToken ct)
        {
            const string table = "tuition_terms";
            var existing = await _context.TuitionTerms.ToDictionaryAsync(
                x => (x.ProgramId, x.StudentType, x.AcademicYear, x.YearOfStudy, x.TermNumber), ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "tuition_terms.csv")))
            {
                Guard(report, row, () =>
                {
                    var institution = FindInstitution(institutions, row);
                    var program = FindProgram(programs, institution, row);

                    var studentTypeText = row.Required("student_type");
                    if (!Enum.TryParse<StudentType>(studentTypeText, ignoreCase: true, out var studentType))
                    {
                        throw row.Error($"student_type must be Domestic or International, not '{studentTypeText}'");
                    }

                    var academicYear = row.Required("academic_year");
                    var yearOfStudy = row.RequiredInt("year_of_study");
                    var termNumber = row.RequiredInt("term_number");
                    var key = (program.Id, studentType, academicYear, yearOfStudy, termNumber);

                    if (!existing.TryGetValue(key, out var entity))
                    {
                        entity = new TuitionTerm
                        {
                            Program = program,
                            StudentType = studentType,
                            AcademicYear = academicYear,
                            YearOfStudy = yearOfStudy,
                            TermNumber = termNumber
                        };
                        _context.TuitionTerms.Add(entity);
                        existing[key] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    entity.TermLabel = row.Optional("term_label");
                    entity.Tuition = row.OptionalDecimal("tuition");
                    entity.RecreationAthleticsFee = row.OptionalDecimal("recreation_athletics_fee");
                    entity.OneCardSupportFee = row.OptionalDecimal("one_card_support_fee");
                    entity.StudentTechnologyFee = row.OptionalDecimal("student_technology_fee");
                    entity.StudentAssociationFee = row.OptionalDecimal("student_association_fee");
                    entity.BooksSupplies = row.OptionalDecimal("books_supplies");
                    entity.TuitionRateText = row.Optional("tuition_rate_text");
                    entity.TermTotalSite = row.OptionalDecimal("term_total_site");

                    // The site's own total is the check on our numbers: if we have the fees
                    // and they do not add up to it, say so instead of loading a wrong figure.
                    var calculated = entity.TermTotalCalculated;
                    if (calculated.HasValue && entity.TermTotalSite.HasValue
                        && calculated.Value != entity.TermTotalSite.Value)
                    {
                        throw row.Error(
                            $"the fees add up to {calculated.Value:0.00} but the site total is {entity.TermTotalSite.Value:0.00}");
                    }

                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);
        }

        // ---- employment ---------------------------------------------------

        private async Task SeedEmploymentAsync(
            string folder, Dictionary<string, Institution> institutions,
            SeedReport report, CancellationToken ct)
        {
            const string table = "employment_stats";
            var existing = await _context.EmploymentStats.ToDictionaryAsync(
                x => (x.InstitutionId, x.SourceProgramName, x.Period), ct);

            foreach (var row in CsvReader.Read(Path.Combine(folder, "employment_stats.csv")))
            {
                Guard(report, row, () =>
                {
                    var institution = FindInstitution(institutions, row);
                    var sourceName = row.Required("source_program_name");
                    var period = row.Required("period");
                    var key = (institution.Id, sourceName, period);

                    var employed = row.RequiredInt("employed");
                    var available = row.RequiredInt("available");
                    if (employed < 0 || available < employed)
                    {
                        throw row.Error($"employed ({employed}) must be between 0 and available ({available})");
                    }

                    if (!existing.TryGetValue(key, out var entity))
                    {
                        entity = new EmploymentStat
                        {
                            Institution = institution,
                            SourceProgramName = sourceName,
                            Period = period
                        };
                        _context.EmploymentStats.Add(entity);
                        existing[key] = entity;
                        report.Inserted(table);
                    }
                    else
                    {
                        report.Updated(table);
                    }

                    // ProgramId is left alone on purpose: matching a survey row to one of our
                    // programs is a human decision, and a re-run must not undo it.
                    entity.Employed = employed;
                    entity.Available = available;
                    ApplyCommon(entity, row);
                });
            }

            if (!report.HasErrors) await _context.SaveChangesAsync(ct);
        }

        // ---- helpers ------------------------------------------------------

        private static Institution FindInstitution(Dictionary<string, Institution> institutions, CsvRow row)
        {
            var dli = row.Required("dli_number");
            if (!institutions.TryGetValue(dli, out var institution))
            {
                throw row.Error($"no institution with DLI number '{dli}'");
            }
            return institution;
        }

        private static StudyProgram FindProgram(
            Dictionary<(int InstitutionId, string Name), StudyProgram> programs,
            Institution institution, CsvRow row)
        {
            var name = row.Required("program_name");
            if (!programs.TryGetValue((institution.Id, name), out var program))
            {
                throw row.Error($"no program '{name}' at {institution.Name}");
            }
            return program;
        }

        // The fields every table shares. A seed can mark a row verified, but it never
        // un-verifies one: a person who checked a row in the database keeps that check.
        private static void ApplyCommon(VerifiedEntity entity, CsvRow row)
        {
            entity.SourceUrl = row.Optional("source_url");
            entity.LastVerifiedOn = row.OptionalDate("last_verified");
            entity.IsVerified = entity.IsVerified || row.Bool("verified");
            entity.Notes = row.OptionalIfPresent("notes");
        }

        // One bad row must not hide the others: collect every error, then stop.
        private static void Guard(SeedReport report, CsvRow row, Action work)
        {
            try
            {
                work();
            }
            catch (InvalidDataException ex)
            {
                report.Errors.Add(ex.Message);
            }
        }
    }

    public class SeedReport
    {
        private readonly Dictionary<string, (int Inserted, int Updated)> _tables = new();

        public List<string> Errors { get; } = new();
        public bool HasErrors => Errors.Count > 0;
        public bool RolledBack { get; set; }

        public void Inserted(string table)
        {
            _tables.TryGetValue(table, out var counts);
            _tables[table] = (counts.Inserted + 1, counts.Updated);
        }

        public void Updated(string table)
        {
            _tables.TryGetValue(table, out var counts);
            _tables[table] = (counts.Inserted, counts.Updated + 1);
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.AppendLine(HasErrors
                ? "Seed FAILED. Nothing was saved."
                : "Seed finished.");

            foreach (var (table, counts) in _tables)
            {
                text.AppendLine($"  {table}: {counts.Inserted} inserted, {counts.Updated} updated");
            }

            foreach (var error in Errors)
            {
                text.AppendLine($"  ERROR {error}");
            }

            return text.ToString();
        }
    }
}
