namespace DoingLife.WebApi.Entities
{
    public enum DraftStatus
    {
        Open = 1,             // being edited by anyone with CatalogEdit
        ReadyForReview = 2,   // an editor marked it ready; waiting for the SuperAdmin
        Published = 3,        // applied to the live tables
        Discarded = 4         // thrown away without publishing
    }

    /// <summary>
    /// The pending changes for ONE school, kept apart from the live tables until the
    /// SuperAdmin publishes. At most one open draft per school (see the configuration).
    /// </summary>
    public class CatalogDraft
    {
        public int Id { get; set; }

        // The school's natural key, same as the seed uses. A brand-new school has no
        // Institution row yet, so the draft is identified by DLI number, not by an id.
        public string DliNumber { get; set; } = "";
        public string SchoolName { get; set; } = "";     // for lists; the payload is the truth

        public DraftStatus Status { get; set; } = DraftStatus.Open;

        // JSON object holding the school's campuses, programs, intakes, tuition, careers...
        public string Payload { get; set; } = "{}";

        // Conflict check. The client sends the Version it loaded; a save only works if it
        // still matches, and then Version goes up by one. Otherwise the API answers 409.
        public int Version { get; set; } = 1;

        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UpdatedByUserId { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Set when the draft is published or discarded.
        public int? ClosedByUserId { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? PublishSummary { get; set; }      // e.g. "3 programs added, 14 tuition rows changed"
    }
}
