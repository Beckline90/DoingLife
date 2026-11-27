namespace DoingLife.WebApi.Shared.Constant
{
    public static class RoleNames
    {
        public const string SuperAdmin = "SuperAdmin";   // can edit, publish and discard
        public const string Editor = "Editor";           // can edit and save drafts only
    }

    public static class PolicyNames
    {
        public const string CatalogEdit = "CatalogEdit";         // SuperAdmin or Editor
        public const string CatalogPublish = "CatalogPublish";   // SuperAdmin only
    }
}
