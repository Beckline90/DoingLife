namespace DoingLife.WebApi.Entities
{
    // ---------------------------------------------------------------
    // Study-in-Canada reference data. This is NOT personal data:
    // it is the same for every user, it is loaded by a seed/admin
    // endpoint, and users only ever read it.
    // ---------------------------------------------------------------

    /// <summary>Every row says where the fact came from and whether a human checked it.</summary>
    public abstract class VerifiedEntity
    {
        public int Id { get; set; }
        public string? SourceUrl { get; set; }
        public DateOnly? LastVerifiedOn { get; set; }
        public bool IsVerified { get; set; }
        public string? Notes { get; set; }
    }

    public class Institution : VerifiedEntity
    {
        public string DliNumber { get; set; } = "";       // e.g. O18713200642 (unique)
        public string Name { get; set; } = "";
        public string Province { get; set; } = "";         // "AB" (2-letter code)
        public string InstitutionType { get; set; } = "";  // "Public" for now
        public string? Website { get; set; }

        public List<Campus> Campuses { get; set; } = new();
        public List<Department> Departments { get; set; } = new();
        public List<StudyProgram> Programs { get; set; } = new();
    }

    public class Campus : VerifiedEntity
    {
        public int InstitutionId { get; set; }
        public Institution Institution { get; set; } = null!;

        public string Name { get; set; } = "";             // what we show: "South Campus"
        public string SourceName { get; set; } = "";       // what the source says: "Souch Campus"
        public string City { get; set; } = "";
    }

    public class Department : VerifiedEntity
    {
        public int InstitutionId { get; set; }
        public Institution Institution { get; set; } = null!;

        public string Name { get; set; } = "";             // "School of Media and Information Technology"
        public List<StudyProgram> Programs { get; set; } = new();
    }

    public class StudyProgram : VerifiedEntity
    {
        public int InstitutionId { get; set; }
        public Institution Institution { get; set; } = null!;
        public int? DepartmentId { get; set; }             // some institutions have no departments
        public Department? Department { get; set; }

        public string Name { get; set; } = "";
        public string Credential { get; set; } = "";       // "Bachelor of Business Administration"
        public string? FieldOfStudy { get; set; }
        public string? DurationText { get; set; }          // "4 Years (Full-time)"
        public bool OpenToInternational { get; set; }

        // Entry requirements. Kept as text until we see enough programs to structure them.
        public string? EnglishTest { get; set; }
        public int? EnglishMinimum { get; set; }
        public string? AcademicMinimum { get; set; }

        public List<ProgramIntake> Intakes { get; set; } = new();
        public List<TuitionTerm> TuitionTerms { get; set; } = new();
    }

    /// <summary>One start date of one program. Deadlines and status differ per intake.</summary>
    public class ProgramIntake : VerifiedEntity
    {
        public int ProgramId { get; set; }
        public StudyProgram Program { get; set; } = null!;
        public int? CampusId { get; set; }
        public Campus? Campus { get; set; }

        public DateOnly StartDate { get; set; }
        public string? Season { get; set; }                // "Winter" | "Spring" | "Fall"
        public string? DeliveryMode { get; set; }          // "Your Choice"

        public string? InternationalStatus { get; set; }   // "Open" | "Limited" | "Upcoming" | "Closed"
        public DateOnly? InternationalApplicationOpens { get; set; }
        public DateOnly? InternationalDeadline { get; set; }

        // Post-Graduation Work Permit eligibility as the institution states it for this intake.
        // null = not checked yet (never read null as "not eligible").
        public bool? PgwpEligible { get; set; }
        public string? PgwpNotes { get; set; }

        public string? DomesticStatus { get; set; }
        public DateOnly? DomesticApplicationOpens { get; set; }
        public DateOnly? DomesticDeadline { get; set; }
    }

    public enum StudentType { Domestic = 1, International = 2 }

    /// <summary>
    /// One term's cost for one student type. Fees are nullable because
    /// a source sometimes shows only the term total.
    /// </summary>
    public class TuitionTerm : VerifiedEntity
    {
        public int ProgramId { get; set; }
        public StudyProgram Program { get; set; } = null!;

        public StudentType StudentType { get; set; }
        public string AcademicYear { get; set; } = "";     // "2026-27"
        public int YearOfStudy { get; set; }               // 1..4
        public int TermNumber { get; set; }                // 1 or 2
        public string? TermLabel { get; set; }             // "Term 1 (Winter)"

        public decimal? Tuition { get; set; }
        public decimal? RecreationAthleticsFee { get; set; }
        public decimal? OneCardSupportFee { get; set; }
        public decimal? StudentTechnologyFee { get; set; }
        public decimal? StudentAssociationFee { get; set; }
        public decimal? BooksSupplies { get; set; }
        public string? TuitionRateText { get; set; }       // "$735.00 per credit"

        public decimal? TermTotalSite { get; set; }        // the total as the website shows it

        // Calculated, never stored: sum of the fees we have. Null if we have none.
        public decimal? TermTotalCalculated
        {
            get
            {
                var parts = new[] { Tuition, RecreationAthleticsFee, OneCardSupportFee,
                                    StudentTechnologyFee, StudentAssociationFee, BooksSupplies };
                return parts.Any(p => p.HasValue) ? parts.Sum(p => p ?? 0m) : null;
            }
        }
    }

    /// <summary>
    /// Graduate employment survey result for one credential and one period.
    /// Long format: one row per period, not 18 columns.
    /// </summary>
    public class EmploymentStat : VerifiedEntity
    {
        public int InstitutionId { get; set; }
        public Institution Institution { get; set; } = null!;
        public int? ProgramId { get; set; }                // null until matched by a human
        public StudyProgram? Program { get; set; }

        public string SourceProgramName { get; set; } = ""; // "Bachelor of Business Admin"
        public string Period { get; set; } = "";           // "All" | "2023/24" | "2022/23" ...
        public int Employed { get; set; }
        public int Available { get; set; }                 // employed + looking for work

        // Calculated: employed / available. Never stored, so it cannot disagree with the counts.
        public decimal? Rate => Available > 0 ? Math.Round((decimal)Employed / Available, 4) : null;
    }
}
