namespace TestPointTrigger
{
    /// <summary>
    /// Shared developer/version credit shown in the main window's footer
    /// and in the Help dialog. Version comes from the csproj's
    /// &lt;Version&gt; (Assembly version) at build time; bump ReleaseDate
    /// by hand whenever a meaningful change ships.
    /// </summary>
    internal static class AppInfo
    {
        public const string DeveloperName = "HaKDMoDz™";
        public const string ReleaseDate = "2026-09-10";

        public static string VersionText
        {
            get
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
            }
        }

        public static string FooterText => $"{DeveloperName}   •   v{VersionText}   •   {ReleaseDate}";
    }
}
