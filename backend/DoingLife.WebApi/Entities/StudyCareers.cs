namespace DoingLife.WebApi.Entities
{
    /// <summary>
    /// One official occupation class from the National Occupational Classification (NOC).
    /// Reference data: the same class is shared by every program that leads to it.
    /// </summary>
    public class Career : VerifiedEntity
    {
        public string NocCode { get; set; } = "";       // five digits, e.g. "11100"
        public string NocVersion { get; set; } = "";    // the NOC edition the code comes from
        public string Title { get; set; } = "";         // the class title, e.g. "Financial auditors and accountants"
        public int TeerCategory { get; set; }           // 0 to 5
    }

    /// <summary>How we know a program leads to a career. Users should be able to see this.</summary>
    public enum CareerLinkBasis
    {
        InstitutionStated = 1,     // the school's own page names the career
        ThirdParty = 2,            // an official source (for example ALIS) links the field to it
        ResearcherInferred = 3     // one of us judged that it fits
    }

    /// <summary>
    /// "This program can lead to this job title." One row per example title, so
    /// "Accountant" and "Auditor" are two rows that can point at the same Career.
    /// The title is stored as the source states it; the NOC class is linked once verified.
    /// </summary>
    public class ProgramCareer : VerifiedEntity
    {
        public int ProgramId { get; set; }
        public StudyProgram Program { get; set; } = null!;
        public int? CareerId { get; set; }          // null until someone has checked the NOC class
        public Career? Career { get; set; }

        public string ExampleTitle { get; set; } = "";  // "Accountant"
        public CareerLinkBasis Basis { get; set; }
    }
}
